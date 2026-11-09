from datetime import timedelta
from uuid import UUID

import pytest

from app.resilience import assess, percentage
from tests.test_fairness import assignments, fixed
from tests.test_solver import make_request


def test_deterministic_analysis_does_not_mutate_and_excludes_external_targets():
    r = make_request(hours=3)
    rows = assignments(r, [0])
    external = fixed(r, own=False, start=75, end=135)
    external.playing_area_id = UUID(int=999)
    r.existing_schedule = [external]
    before = r.model_dump(), [a.model_dump() for a in rows]
    score = assess(r, rows)
    assert score == assess(r, rows)
    assert score.delay_score == 50  # +15 adjacent, +30 overlaps shared teams
    assert score.area_score == 0
    assert score.referee_score is None
    assert score.score == 25
    assert score.scenarios_evaluated == 3
    assert score.vulnerable_scenarios == 2
    assert before == (r.model_dump(), [a.model_dump() for a in rows])


@pytest.mark.parametrize("kind", ["teams", "playing_areas", "referees", "horizon"])
def test_delay_cannot_exceed_availability_or_horizon(kind):
    r = make_request(hours=3, referee=True)
    rows = assignments(r, [0])
    rows[0].referee_id = r.referees[0].id
    if kind == "horizon":
        r.horizon_end = r.horizon_start + timedelta(minutes=75)
    else:
        getattr(r, kind)[0].availability[0].ends_at = r.horizon_start + timedelta(
            minutes=75
        )
    assert assess(r, rows).delay_score == 50


@pytest.mark.parametrize(
    "resource,attribute",
    [("playing_areas", "playing_area_id"), ("referees", "referee_id")],
)
@pytest.mark.parametrize("occupied", [False, True])
def test_same_time_fallback_requires_free_resource(resource, attribute, occupied):
    r = make_request(hours=3, referee=True)
    rows = assignments(r, [0])
    rows[0].referee_id = r.referees[0].id
    alternate = getattr(r, resource)[0].model_copy(
        deep=True, update={"id": UUID(int=888)}
    )
    getattr(r, resource).append(alternate)
    if occupied:
        block = fixed(r, own=False)
        setattr(block, attribute, alternate.id)
        r.existing_schedule = [block]
    result = assess(r, rows)
    assert getattr(
        result, "area_score" if resource == "playing_areas" else "referee_score"
    ) == (0 if occupied else 100)


def test_empty_and_operational_fixed_scope():
    r = make_request(hours=3)
    r.matches = []
    assert assess(r, []) is None
    game = fixed(r)
    r.existing_schedule = [game]
    assert assess(r, []) is None
    game.counts_for_resilience = True
    assert assess(r, []).scenarios_evaluated == 3
    game.ends_at = r.horizon_end + timedelta(minutes=1)
    assert assess(r, []) is None


def test_all_components_and_integer_rounding():
    r = make_request(hours=3, referee=True)
    rows = assignments(r, [0])
    rows[0].referee_id = r.referees[0].id
    score = assess(r, rows)
    assert (score.delay_score, score.area_score, score.referee_score) == (100, 0, 0)
    assert score.score == 33
    assert score.scenarios_evaluated == 4
    assert score.vulnerable_scenarios == 2
    assert percentage(1, 8) == 13


@pytest.mark.parametrize("attribute", ["playing_area_id", "referee_id"])
def test_delay_checks_other_occupations_for_area_and_referee(attribute):
    r = make_request(hours=3, referee=True)
    rows = assignments(r, [0])
    rows[0].referee_id = r.referees[0].id
    block = fixed(r, own=False, start=75, end=135)
    block.home_team_id, block.away_team_id = UUID(int=800), UUID(int=801)
    block.playing_area_id = UUID(int=802)
    setattr(block, attribute, getattr(rows[0], attribute))
    r.existing_schedule = [block]
    assert assess(r, rows).delay_score == 50
