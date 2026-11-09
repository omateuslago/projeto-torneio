import math
from collections import defaultdict
from datetime import timedelta

from ortools.sat.python import cp_model

from .models import Assignment, GenerateRequest, Result
from .fairness import add_objective, assess


def solve(request: GenerateRequest, time_limit: float = 5.0) -> Result:
    """Integer minutes, grid-aligned starts, exact durations, half-open intervals."""
    model = cp_model.CpModel()
    origin = request.horizon_start
    horizon = math.floor((request.horizon_end - origin).total_seconds() / 60)
    duration = request.match_duration_minutes
    step = request.granularity_minutes
    if not request.matches:
        return Result(status="optimal", fairness=assess(request, []))
    if horizon < duration:
        return Result(
            status="infeasible",
            warnings=["Horizonte menor que a duração de uma partida."],
        )
    teams = {t.id: t for t in request.teams}
    occupancy = defaultdict(list)

    def ranges(resource):
        return [
            (
                max(0, math.ceil((w.starts_at - origin).total_seconds() / 60)),
                min(horizon, math.floor((w.ends_at - origin).total_seconds() / 60))
                - duration,
            )
            for w in resource.availability
            if max(origin, w.starts_at) + timedelta(minutes=duration)
            <= min(request.horizon_end, w.ends_at)
        ]

    def restrict(start, resource, enabled=None):
        allowed = ranges(resource)
        if not allowed:
            if enabled is None:
                model.add_bool_or([])
            else:
                model.add(enabled == 0)
        else:
            constraint = model.add_linear_expression_in_domain(
                start, cp_model.Domain.from_intervals(allowed)
            )
            if enabled is not None:
                constraint.only_enforce_if(enabled)

    # Round fixed occupations outwards, conservatively preserving sub-minute times.
    for fixed in request.existing_schedule:
        begin = max(0, math.floor((fixed.starts_at - origin).total_seconds() / 60))
        end = min(horizon, math.ceil((fixed.ends_at - origin).total_seconds() / 60))
        if begin >= end:
            continue
        interval = model.new_fixed_size_interval_var(
            begin, end - begin, str(fixed.match_id)
        )
        for kind, resource_id in [
            ("team", fixed.home_team_id),
            ("team", fixed.away_team_id),
            ("area", fixed.playing_area_id),
            ("referee", fixed.referee_id),
        ]:
            if resource_id is not None:
                occupancy[kind, resource_id].append(interval)

    decisions = []
    for match in sorted(request.matches, key=lambda m: str(m.id)):
        start = model.new_int_var(0, horizon - duration, f"start_{match.id}")
        slot = model.new_int_var(0, (horizon - duration) // step, f"slot_{match.id}")
        model.add(start == slot * step)
        interval = model.new_fixed_size_interval_var(start, duration, str(match.id))
        for team_id in (match.home_team_id, match.away_team_id):
            restrict(start, teams[team_id])
            occupancy["team", team_id].append(interval)
        choices = {}
        for kind, resources in [
            ("area", request.playing_areas),
            ("referee", request.referees if request.require_referee else []),
        ]:
            selected = []
            for resource in sorted(resources, key=lambda r: str(r.id)):
                enabled = model.new_bool_var(f"{match.id}_{kind}_{resource.id}")
                optional = model.new_optional_fixed_size_interval_var(
                    start, duration, enabled, str(resource.id)
                )
                restrict(start, resource, enabled)
                occupancy[kind, resource.id].append(optional)
                selected.append((resource.id, enabled))
            if kind == "area" or request.require_referee:
                model.add_exactly_one([enabled for _, enabled in selected])
            choices[kind] = selected
        decisions.append((match, start, choices))
    for intervals in occupancy.values():
        model.add_no_overlap(intervals)
    add_objective(model, request, {match.id: start for match, start, _ in decisions})
    solver = cp_model.CpSolver()
    solver.parameters.max_time_in_seconds = time_limit
    solver.parameters.num_search_workers = 1
    solver.parameters.random_seed = 0
    status = solver.solve(model)
    if status == cp_model.UNKNOWN:
        return Result(
            status="timeout",
            warnings=[
                "Limite de cálculo atingido; ainda não foi encontrada solução. Isso não prova impossibilidade."
            ],
        )
    if status == cp_model.INFEASIBLE:
        warnings = [
            f"Equipe {t.name} não possui janela suficiente no horizonte."
            for t in request.teams
            if not ranges(t)
        ]
        if not any(ranges(a) for a in request.playing_areas):
            warnings.append(
                "Nenhuma área de jogo possui janela suficiente no horizonte."
            )
        if request.require_referee and not any(ranges(r) for r in request.referees):
            warnings.append("Árbitros não cobrem o período necessário.")
        return Result(
            status="infeasible",
            warnings=warnings
            or [
                "Não há combinação compatível de equipes, áreas e árbitros para todas as partidas nesta grade. Amplie a disponibilidade ou o horizonte; tente uma granularidade menor."
            ],
        )
    if status not in (cp_model.OPTIMAL, cp_model.FEASIBLE):
        return Result(
            status="error", warnings=["O modelo de scheduling não pôde ser resolvido."]
        )
    assignments = []
    for match, start, choices in decisions:
        area = next(
            id for id, enabled in choices["area"] if solver.boolean_value(enabled)
        )
        referee = next(
            (id for id, enabled in choices["referee"] if solver.boolean_value(enabled)),
            None,
        )
        assignments.append(
            Assignment(
                match_id=match.id,
                starts_at=origin + timedelta(minutes=solver.value(start)),
                playing_area_id=area,
                referee_id=referee,
            )
        )
    return Result(
        status="optimal" if status == cp_model.OPTIMAL else "feasible",
        scheduled_matches=sorted(
            assignments, key=lambda a: (a.starts_at, str(a.match_id))
        ),
        fairness=assess(request, assignments),
    )
