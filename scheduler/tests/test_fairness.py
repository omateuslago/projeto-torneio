from datetime import timedelta
from uuid import UUID

import pytest
from fastapi.testclient import TestClient
from ortools.sat.python import cp_model

from app.fairness import SCALE, TOTAL_WEIGHT, add_objective, assess
from app.main import app
from app.models import Assignment, FixedMatch, Match, Resource, Window
from app.solver import solve
from tests.test_solver import START, make_request


def two_games(hours=4):
    request = make_request(hours=hours)
    request.matches.append(request.matches[0].model_copy(update={"id": UUID(int=1001)}))
    return request


def assignments(request, minutes):
    return [
        Assignment(
            match_id=match.id,
            starts_at=START + timedelta(minutes=start),
            playing_area_id=request.playing_areas[0].id,
        )
        for match, start in zip(request.matches, minutes, strict=True)
    ]


@pytest.mark.parametrize(
    "gap,rest_score,b2b_score,total",
    [(0, 0, 0, 1), (30, 50, 100, 95), (60, 100, 100, 100)],
)
def test_rest_components_normalization_and_explanations(
    gap, rest_score, b2b_score, total
):
    request = two_games()
    fairness = assess(request, assignments(request, [0, 60 + gap]))
    assert fairness.rest_score == rest_score
    assert fairness.back_to_back_score == b2b_score
    assert fairness.time_balance_score == 100
    assert fairness.score == total
    assert any("Equipe 1" in line for line in fairness.observations) == (gap < 60)
    if gap == 30:
        assert any("30 minutos" in line for line in fairness.observations)


def test_solver_prefers_rest_over_another_hard_valid_schedule():
    request = two_games(hours=3)
    unfair = assess(request, assignments(request, [0, 60]))
    result = solve(request)
    assert result.status == "optimal"
    assert [a.starts_at for a in result.scheduled_matches] == [
        START,
        START + timedelta(hours=2),
    ]
    assert result.fairness.score == 100 > unfair.score
    assert result == solve(request)


def test_rest_and_back_to_back_take_priority_over_better_time_balance():
    request = make_request(3, hours=3)
    request.matches = request.matches[:2]  # A-B and A-C, sharing team A
    compact = assess(request, assignments(request, [0, 60]))
    result = solve(request)
    assert result.status == "optimal"
    assert result.fairness.rest_score == 100
    assert result.fairness.back_to_back_score == 100
    assert result.fairness.time_balance_score < compact.time_balance_score
    assert result.fairness.score > compact.score


def test_back_to_back_remains_soft_when_only_solution_and_rest_zero():
    request = two_games(hours=2)
    result = solve(request)
    assert result.status == "optimal"
    assert len(result.scheduled_matches) == 2
    assert result.fairness.back_to_back_score == 0
    assert result.fairness.rest_score == 0
    request.preferred_rest_minutes = 0
    result = solve(request)
    assert result.fairness.rest_score == 100
    assert result.fairness.back_to_back_score == 0  # independent of the rest target


def test_solver_improves_time_balance_for_disjoint_teams():
    request = make_request(4, hours=4)
    request.matches = [
        Match(
            id=UUID(int=1000),
            home_team_id=request.teams[0].id,
            away_team_id=request.teams[1].id,
        ),
        Match(
            id=UUID(int=1001),
            home_team_id=request.teams[2].id,
            away_team_id=request.teams[3].id,
        ),
    ]
    request.playing_areas.append(
        Resource(
            id=UUID(int=101),
            name="Quadra 2",
            availability=request.playing_areas[0].availability,
        )
    )
    worse = assess(request, assignments(request, [0, 180]))
    result = solve(request)
    assert result.status == "optimal"
    assert result.fairness.time_balance_score == 100 > worse.time_balance_score
    assert (
        result.scheduled_matches[0].starts_at == result.scheduled_matches[1].starts_at
    )


def fixed(request, own=True, start=0, end=60):
    return FixedMatch(
        match_id=UUID(int=900),
        home_team_id=request.teams[0].id,
        away_team_id=request.teams[1].id,
        starts_at=START + timedelta(minutes=start),
        ends_at=START + timedelta(minutes=end),
        playing_area_id=request.playing_areas[0].id,
        counts_for_fairness=own,
    )


def test_own_fixed_game_influences_optimization_and_is_never_moved():
    request = make_request(hours=3)
    request.existing_schedule = [fixed(request)]
    result = solve(request)
    assert result.scheduled_matches[0].starts_at == START + timedelta(hours=2)
    assert result.fairness.score == 100
    assert request.existing_schedule[0].starts_at == START
    assert all(
        a.match_id != request.existing_schedule[0].match_id
        for a in result.scheduled_matches
    )


def test_external_fixed_game_blocks_resources_without_distorting_score():
    request = make_request(hours=2)
    request.existing_schedule = [fixed(request, own=False)]
    result = solve(request)
    assert result.scheduled_matches[0].starts_at == START + timedelta(hours=1)
    assert result.fairness.score == 100
    request.existing_schedule[0].counts_for_fairness = True
    result = solve(request)
    assert result.fairness.back_to_back_score == 0
    assert result.fairness.score < 100


def test_no_pending_games_still_scores_own_fixed_games():
    request = make_request(hours=3)
    request.existing_schedule = [
        fixed(request),
        fixed(request, start=60, end=120).model_copy(
            update={"match_id": UUID(int=901)}
        ),
    ]
    request.matches = []
    result = solve(request)
    assert result.status == "optimal"
    assert result.scheduled_matches == []
    assert result.fairness.back_to_back_score == 0
    assert result.fairness.rest_score == 0


@pytest.mark.parametrize("start,end", [(-60, 0), (240, 300), (-30, 30)])
def test_fixed_games_outside_fairness_horizon_are_excluded(start, end):
    request = make_request(hours=4)
    request.existing_schedule = [fixed(request, start=start, end=end)]
    assert assess(request, assignments(request, [60])).score == 100


@pytest.mark.parametrize("rest", [0, 30, 60, 240])
@pytest.mark.parametrize("minutes", [[0, 60, 120], [0, 90, 180], [0, 120, 300]])
def test_cp_sat_objective_and_reported_score_use_identical_criteria(rest, minutes):
    request = make_request(3, hours=8)
    request.preferred_rest_minutes = rest
    request.existing_schedule = [fixed(request, start=420, end=480)]
    model = cp_model.CpModel()
    objective = add_objective(
        model,
        request,
        {m.id: start for m, start in zip(request.matches, minutes, strict=True)},
    )
    assert model.validate() == ""
    solver = cp_model.CpSolver()
    assert solver.solve(model) == cp_model.OPTIMAL
    penalty = solver.value(objective)
    maximum = SCALE * TOTAL_WEIGHT
    expected = ((maximum - penalty) * 100 + maximum // 2) // maximum
    result = assess(request, assignments(request, minutes))
    assert result.score == expected
    assert all(
        0 <= value <= 100
        for key, value in result.model_dump().items()
        if key != "observations"
    )


def test_only_chronological_neighbors_count_for_short_games():
    request = make_request(3, hours=1, duration=5)
    result = assess(request, assignments(request, [0, 5, 10]))
    # Every team has exactly two games; deficits are 60 + 60 + 55 minutes.
    assert result.rest_score == 3
    assert result.back_to_back_score == 33


def test_three_games_of_same_team_do_not_count_non_neighbor_pairs():
    request = make_request(hours=1, duration=5)
    request.matches += [
        request.matches[0].model_copy(update={"id": UUID(int=1001)}),
        request.matches[0].model_copy(update={"id": UUID(int=1002)}),
    ]
    rows = assignments(request, [0, 5, 10])
    fairness = assess(request, rows)
    assert fairness.rest_score == 0
    assert fairness.back_to_back_score == 0
    assert any("em 2 intervalo(s)" in line for line in fairness.observations)
    model = cp_model.CpModel()
    objective = add_objective(
        model, request, {m.id: i * 5 for i, m in enumerate(request.matches)}
    )
    solver = cp_model.CpSolver()
    assert solver.solve(model) == cp_model.OPTIMAL
    assert solver.value(objective) == 110000


def test_subminute_fixed_game_uses_same_units_in_objective_and_score():
    request = make_request(hours=3)
    game = fixed(request)
    game.ends_at += timedelta(seconds=30, microseconds=500000)
    request.existing_schedule = [game]
    model = cp_model.CpModel()
    objective = add_objective(model, request, {request.matches[0].id: 90})
    solver = cp_model.CpSolver()
    assert solver.solve(model) == cp_model.OPTIMAL
    result = assess(request, assignments(request, [90]))
    maximum = SCALE * TOTAL_WEIGHT
    assert (
        result.score
        == ((maximum - solver.value(objective)) * 100 + maximum // 2) // maximum
    )


@pytest.mark.parametrize("value", [-1, 241])
def test_invalid_rest_http_contract(value):
    data = make_request().model_dump(mode="json", by_alias=True)
    data["preferredRestMinutes"] = value
    assert TestClient(app).post("/schedule/generate", json=data).status_code == 422


def test_http_returns_fairness_and_no_score_for_infeasible():
    request = make_request()
    client = TestClient(app)
    response = client.post(
        "/schedule/generate", json=request.model_dump(mode="json", by_alias=True)
    )
    assert response.json()["fairness"]["score"] == 100
    assert response.json()["fairness"]["observations"]
    request.teams[0].availability = []
    response = client.post(
        "/schedule/generate", json=request.model_dump(mode="json", by_alias=True)
    )
    assert response.json()["fairness"] is None


def test_unavoidable_short_rest_does_not_override_availability():
    request = two_games(hours=3)
    request.teams[0].availability = [
        Window(starts_at=START, ends_at=START + timedelta(minutes=150))
    ]
    result = solve(request)
    assert result.status == "optimal"
    assert result.fairness.rest_score == 50
    assert result.fairness.back_to_back_score == 100
    assert max(a.starts_at for a in result.scheduled_matches) + timedelta(
        hours=1
    ) <= START + timedelta(minutes=150)
