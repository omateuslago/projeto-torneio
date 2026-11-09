"""The CP-SAT objective and its reported score share scope, units and weights."""

import math
from collections import defaultdict
from dataclasses import dataclass

from .models import Fairness

SCALE = 1000
BACK_TO_BACK_WEIGHT = 100
REST_WEIGHT = 10
TIME_BALANCE_WEIGHT = 1
TOTAL_WEIGHT = BACK_TO_BACK_WEIGHT + REST_WEIGHT + TIME_BALANCE_WEIGHT


@dataclass
class Event:
    start: object
    end: object
    fixed: bool


def events_by_team(request, starts):
    """Fairness covers only this tournament's games fully inside the horizon.

    Seconds retain sub-minute fixed times (timestamps truncated to whole seconds).
    External occupations still participate in the solver's hard constraints.
    """
    events = defaultdict(list)
    for match in sorted(request.matches, key=lambda m: str(m.id)):
        start = starts[match.id] * 60
        event = Event(start, start + request.match_duration_minutes * 60, False)
        for team in (match.home_team_id, match.away_team_id):
            events[team].append(event)
    known_teams = {t.id for t in request.teams}
    for match in sorted(request.existing_schedule, key=lambda m: str(m.match_id)):
        if not (
            match.counts_for_fairness
            and request.horizon_start <= match.starts_at
            and match.ends_at <= request.horizon_end
        ):
            continue
        event = Event(
            math.floor((match.starts_at - request.horizon_start).total_seconds()),
            math.floor((match.ends_at - request.horizon_start).total_seconds()),
            True,
        )
        for team in (match.home_team_id, match.away_team_id):
            if team in known_teams:
                events[team].append(event)
    return dict(sorted(events.items(), key=lambda item: str(item[0])))


def normalized(value, maximum):
    return (value * SCALE + maximum - 1) // maximum if maximum else 0


def add_objective(model, request, starts):
    events = events_by_team(request, starts)
    horizon = math.ceil((request.horizon_end - request.horizon_start).total_seconds())
    rest = request.preferred_rest_minutes * 60
    sentinel = horizon + rest + 1
    deficits, consecutive, means = [], [], []
    transitions = sum(max(0, len(rows) - 1) for rows in events.values())

    def variable(low, high, name):
        return model.new_int_var(low, high, name)

    for team, rows in events.items():
        total_start = variable(0, len(rows) * horizon, f"total_start_{team}")
        model.add(total_start == sum(row.start for row in rows))
        mean = variable(0, SCALE, f"mean_{team}")
        model.add_division_equality(mean, total_start * SCALE, len(rows) * horizon)
        means.append(mean)
        fixed_rows = sorted(
            (row for row in rows if row.fixed), key=lambda row: row.start
        )
        pending_rows = [row for row in rows if not row.fixed]
        fixed_successors = {
            id(first): max(0, second.start - first.end)
            for first, second in zip(fixed_rows, fixed_rows[1:])
        }
        for index, row in enumerate(rows):
            # The nearest later game is the chronological successor. No penalty
            # for distant/non-consecutive pairs or for the last game of a team.
            candidates = [sentinel]
            if row.fixed and id(row) in fixed_successors:
                candidates.append(fixed_successors[id(row)])
            for other in pending_rows if row.fixed else rows:
                if other is row:
                    continue
                later = model.new_bool_var("later")
                model.add(other.start >= row.end).only_enforce_if(later)
                model.add(other.start < row.end).only_enforce_if(later.Not())
                gap = variable(0, sentinel, "successor_gap")
                model.add(gap == other.start - row.end).only_enforce_if(later)
                model.add(gap == sentinel).only_enforce_if(later.Not())
                candidates.append(gap)
            nearest = variable(0, sentinel, f"rest_{team}_{index}")
            model.add_min_equality(nearest, candidates)
            deficit = variable(0, rest, "rest_deficit")
            model.add_max_equality(deficit, [0, rest - nearest])
            deficits.append(deficit)
            adjacent = model.new_bool_var("back_to_back")
            model.add(nearest == 0).only_enforce_if(adjacent)
            model.add(nearest > 0).only_enforce_if(adjacent.Not())
            consecutive.append(adjacent)

    def normalize(expression, maximum, name):
        if maximum == 0:
            return 0
        total = variable(0, maximum, f"total_{name}")
        model.add(total == expression)
        penalty = variable(0, SCALE, name)
        model.add_division_equality(penalty, total * SCALE + maximum - 1, maximum)
        return penalty

    balance = 0
    if means:
        earliest = variable(0, SCALE, "earliest_mean")
        latest = variable(0, SCALE, "latest_mean")
        model.add_min_equality(earliest, means)
        model.add_max_equality(latest, means)
        balance = latest - earliest
    objective = (
        BACK_TO_BACK_WEIGHT * normalize(sum(consecutive), transitions, "b2b_penalty")
        + REST_WEIGHT * normalize(sum(deficits), transitions * rest, "rest_penalty")
        + TIME_BALANCE_WEIGHT * balance
    )
    model.minimize(objective)
    return objective


def assess(request, assignments):
    starts = {
        a.match_id: round((a.starts_at - request.horizon_start).total_seconds() / 60)
        for a in assignments
    }
    events = events_by_team(request, starts)
    horizon = math.ceil((request.horizon_end - request.horizon_start).total_seconds())
    rest = request.preferred_rest_minutes * 60
    names = {t.id: t.name for t in request.teams}
    deficits = consecutive = transitions = 0
    means, observations = [], []
    for team, rows in events.items():
        ordered = sorted(rows, key=lambda row: (row.start, row.end))
        gaps = [max(0, b.start - a.end) for a, b in zip(ordered, ordered[1:])]
        transitions += len(gaps)
        deficits += sum(max(0, rest - gap) for gap in gaps)
        count = sum(gap == 0 for gap in gaps)
        consecutive += count
        short = [gap for gap in gaps if gap < rest]
        if short:
            observations.append(
                f"{names[team]} possui descanso abaixo da meta em {len(short)} "
                f"intervalo(s); menor descanso: {min(short) / 60:g} minutos."
            )
        if count:
            observations.append(
                f"{names[team]} possui {count} intervalo(s) sem descanso."
            )
        means.append(sum(row.start for row in rows) * SCALE // (len(rows) * horizon))
    b2b = normalized(consecutive, transitions)
    deficit = normalized(deficits, transitions * rest)
    balance = max(means) - min(means) if means else 0
    objective = (
        BACK_TO_BACK_WEIGHT * b2b
        + REST_WEIGHT * deficit
        + TIME_BALANCE_WEIGHT * balance
    )
    if not consecutive:
        observations.append("Nenhuma equipe possui partidas consecutivas sem descanso.")
    if not deficits:
        observations.append(
            "Todos os intervalos entre partidas atendem ao descanso preferido."
        )
    observations.append(
        "A distribuição dos horários está equilibrada entre as equipes."
        if balance == 0
        else f"Diferença entre médias de início das equipes: {balance / 10:g}% do horizonte."
    )

    def score(penalty, maximum=SCALE):
        # Integer round-half-up, avoiding float/banker's-rounding differences.
        return max(0, min(100, ((maximum - penalty) * 100 + maximum // 2) // maximum))

    return Fairness(
        score=score(objective, SCALE * TOTAL_WEIGHT),
        rest_score=score(deficit),
        back_to_back_score=score(b2b),
        time_balance_score=score(balance),
        observations=observations,
    )
