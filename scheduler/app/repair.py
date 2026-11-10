"""Minimal-change repair, sharing the generation hard constraints."""

from datetime import timedelta
from typing import Literal
from uuid import UUID

from pydantic import AwareDatetime, Field, model_validator

from .models import Contract, GenerateRequest, Match, Result, Window
from .solver import solve


class Incident(Contract):
    kind: Literal[
        "playingAreaUnavailable",
        "refereeUnavailable",
        "teamUnavailable",
        "matchDelayed",
    ]
    resource_id: UUID | None = None
    starts_at: AwareDatetime | None = None
    ends_at: AwareDatetime | None = None
    match_id: UUID | None = None
    delay_minutes: int | None = Field(default=None, ge=1, le=240)

    @model_validator(mode="after")
    def valid(self):
        if self.kind == "matchDelayed":
            if (
                self.match_id is None
                or self.delay_minutes is None
                or any(
                    x is not None
                    for x in (self.resource_id, self.starts_at, self.ends_at)
                )
            ):
                raise ValueError("Informe somente partida e atraso.")
        elif (
            self.resource_id is None
            or self.starts_at is None
            or self.ends_at is None
            or self.starts_at >= self.ends_at
            or self.match_id is not None
            or self.delay_minutes is not None
        ):
            raise ValueError("Informe recurso e intervalo válido.")
        return self


class RepairRequest(Contract):
    schedule: GenerateRequest
    incident: Incident


def affected(m, incident, delayed=None):
    if delayed is not None:
        return (
            m.match_id != delayed.match_id
            and m.starts_at < delayed.ends_at
            and delayed.starts_at < m.ends_at
            and (
                bool(
                    {m.home_team_id, m.away_team_id}
                    & {delayed.home_team_id, delayed.away_team_id}
                )
                or m.playing_area_id == delayed.playing_area_id
                or (m.referee_id is not None and m.referee_id == delayed.referee_id)
            )
        )
    if not (m.starts_at < incident.ends_at and incident.starts_at < m.ends_at):
        return False
    return (
        incident.kind == "playingAreaUnavailable"
        and m.playing_area_id == incident.resource_id
        or incident.kind == "refereeUnavailable"
        and m.referee_id == incident.resource_id
        or incident.kind == "teamUnavailable"
        and incident.resource_id in (m.home_team_id, m.away_team_id)
    )


def repair(request: RepairRequest, time_limit=5.0):
    source, incident = request.schedule, request.incident
    data = source.model_copy(deep=True)
    original = {m.match_id: m for m in data.existing_schedule}
    delayed = None
    if incident.kind == "matchDelayed":
        delayed = original.get(incident.match_id)
        if delayed is None or not delayed.counts_for_resilience:
            raise ValueError("Partida incidente inválida ou finalizada.")
        delayed.ends_at += timedelta(minutes=incident.delay_minutes)
    else:
        resources = {
            "playingAreaUnavailable": data.playing_areas,
            "refereeUnavailable": data.referees,
            "teamUnavailable": data.teams,
        }[incident.kind]
        resource = next((r for r in resources if r.id == incident.resource_id), None)
        if resource is None:
            raise ValueError("Recurso incidente inválido.")
        windows = []
        for w in resource.availability:
            if w.ends_at <= incident.starts_at or w.starts_at >= incident.ends_at:
                windows.append(w)
            else:
                if w.starts_at < incident.starts_at:
                    windows.append(
                        Window(starts_at=w.starts_at, ends_at=incident.starts_at)
                    )
                if incident.ends_at < w.ends_at:
                    windows.append(
                        Window(starts_at=incident.ends_at, ends_at=w.ends_at)
                    )
        resource.availability = windows
    candidates = [
        m
        for m in source.existing_schedule
        if m.counts_for_resilience
        and source.horizon_start <= m.starts_at
        and m.ends_at <= source.horizon_end
        and m.match_id != incident.match_id
    ]
    if len(candidates) > 200:
        raise ValueError("Limite de 200 partidas para reparo.")
    ids = {m.match_id for m in candidates}
    impacted = [m for m in source.existing_schedule if affected(m, incident, delayed)]
    if any(m.match_id not in ids for m in impacted):
        return Result(
            status="infeasible",
            warnings=[
                "Incidente afeta partida imutável. Amplie o horizonte quando aplicável."
            ],
        )
    if not impacted:
        return Result(status="optimal", warnings=["Nenhuma alteração necessária."])
    data.matches = [
        Match(id=m.match_id, home_team_id=m.home_team_id, away_team_id=m.away_team_id)
        for m in candidates
    ]
    data.existing_schedule = [
        m for m in data.existing_schedule if m.match_id not in ids
    ]
    horizon = int((data.horizon_end - data.horizon_start).total_seconds() // 60)
    originals = {m.match_id: m for m in candidates}
    # Grid compatibility is explicit: no silently rounding existing reservations.
    if any(
        (m.starts_at - data.horizon_start).total_seconds()
        % (60 * data.granularity_minutes)
        or (m.ends_at - m.starts_at).total_seconds() != data.match_duration_minutes * 60
        for m in candidates
    ):
        return Result(
            status="error",
            warnings=[
                "Reservas existentes incompatíveis com a grade/duração solicitada."
            ],
        )

    def objective(model, decisions):
        changed, displacement, resources_changed = [], [], []
        for match, start, choices in decisions:
            old = originals[match.id]
            before = int((old.starts_at - data.horizon_start).total_seconds() // 60)
            moved = model.new_bool_var("time_changed")
            model.add(start != before).only_enforce_if(moved)
            model.add(start == before).only_enforce_if(moved.Not())
            delta = model.new_int_var(0, horizon, "displacement")
            model.add_abs_equality(delta, start - before)
            displacement.append(delta)
            changes = [moved]
            for kind, old_id in [
                ("area", old.playing_area_id),
                ("referee", old.referee_id),
            ]:
                same = next(
                    (selected for key, selected in choices[kind] if key == old_id), None
                )
                difference = (
                    0
                    if kind == "referee" and old_id is None
                    else 1
                    if same is None
                    else 1 - same
                )
                changes.append(difference)
                resources_changed.append(difference)
            change = model.new_bool_var("match_changed")
            model.add_max_equality(change, changes)
            changed.append(change)
        n = len(decisions)
        resource_bound = 2 * n
        lower_bound = n * horizon * (resource_bound + 1) + resource_bound
        model.minimize(
            sum(changed) * (lower_bound + 1)
            + sum(displacement) * (resource_bound + 1)
            + sum(resources_changed)
        )

    return solve(
        data,
        time_limit,
        repair_objective=objective,
        referee_matches={m.match_id for m in candidates if m.referee_id is not None},
    )
