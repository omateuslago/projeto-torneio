from datetime import datetime, timedelta, timezone
from itertools import combinations
from uuid import UUID

import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.models import FixedMatch, GenerateRequest, Match, Resource, Window
from app.solver import solve

START = datetime(2026, 11, 14, 18, tzinfo=timezone.utc)


def make_request(count=2, hours=8, referee=False, duration=60):
    teams = [
        Resource(
            id=UUID(int=i + 1),
            name=f"Equipe {i + 1}",
            availability=[
                Window(starts_at=START, ends_at=START + timedelta(hours=hours))
            ],
        )
        for i in range(count)
    ]
    area = Resource(
        id=UUID(int=100), name="Quadra 1", availability=teams[0].availability
    )
    ref = Resource(id=UUID(int=200), name="Carlos", availability=teams[0].availability)
    return GenerateRequest(
        tournament_id=UUID(int=500),
        horizon_start=START,
        horizon_end=START + timedelta(hours=hours),
        match_duration_minutes=duration,
        matches=[
            Match(id=UUID(int=1000 + i), home_team_id=h.id, away_team_id=a.id)
            for i, (h, a) in enumerate(combinations(teams, 2))
        ],
        teams=teams,
        playing_areas=[area],
        referees=[ref],
        require_referee=referee,
        existing_schedule=[],
    )


def test_simple_and_http_contract():
    request = make_request()
    client = TestClient(app)
    response = client.post(
        "/schedule/generate", json=request.model_dump(mode="json", by_alias=True)
    )
    assert response.status_code == 200
    assert response.json()["status"] in ("optimal", "feasible")
    assert len(response.json()["scheduledMatches"]) == 1
    assert client.get("/health").json() == {"status": "healthy"}


def test_six_matches_no_resource_overlap_and_deterministic():
    request = make_request(4, referee=True)
    result = solve(request)
    assert result == solve(request)
    assert len(result.scheduled_matches) == 6
    for first, second in combinations(result.scheduled_matches, 2):
        assert (
            first.starts_at + timedelta(hours=1) <= second.starts_at
            or second.starts_at + timedelta(hours=1) <= first.starts_at
        )
    assert all(a.referee_id == request.referees[0].id for a in result.scheduled_matches)


@pytest.mark.parametrize("resource", ["team", "area", "referee"])
def test_unavailable_resource(resource):
    request = make_request(referee=True)
    target = {
        "team": request.teams[0],
        "area": request.playing_areas[0],
        "referee": request.referees[0],
    }[resource]
    target.availability = []
    result = solve(request)
    assert result.status == "infeasible"
    assert result.warnings and not result.scheduled_matches


def test_area_capacity_insufficient():
    result = solve(make_request(4, hours=4))
    assert result.status == "infeasible"


def test_adjacent_matches_fit_exactly():
    result = solve(make_request(3, hours=3))
    assert [a.starts_at for a in result.scheduled_matches] == [
        START + timedelta(hours=i) for i in range(3)
    ]


def test_fixed_match_preserved_and_blocks_shared_team_on_other_area():
    request = make_request(hours=2)
    request.existing_schedule = [
        FixedMatch(
            match_id=UUID(int=900),
            home_team_id=request.teams[0].id,
            away_team_id=UUID(int=99),
            starts_at=START,
            ends_at=START + timedelta(hours=1),
            playing_area_id=UUID(int=199),
        )
    ]
    result = solve(request)
    assert len(result.scheduled_matches) == 1
    assert result.scheduled_matches[0].starts_at == START + timedelta(hours=1)


def test_duration_is_not_rounded_to_grid():
    request = make_request(hours=1, duration=50)
    request.teams[0].availability = [
        Window(starts_at=START, ends_at=START + timedelta(minutes=50))
    ]
    assert solve(request).scheduled_matches[0].starts_at == START


def test_timeout_not_infeasible():
    assert solve(make_request(4), time_limit=0).status == "timeout"


def test_invalid_naive_time_or_foreign_team_rejected():
    data = make_request().model_dump(mode="json", by_alias=True)
    data["horizonStart"] = "2026-11-14T18:00:00"
    assert TestClient(app).post("/schedule/generate", json=data).status_code == 422
    data["horizonStart"] = START.isoformat()
    data["matches"][0]["homeTeamId"] = str(UUID(int=9999))
    assert TestClient(app).post("/schedule/generate", json=data).status_code == 422


def test_optional_referee_not_assigned():
    request = make_request()
    request.referees[0].availability = []
    assert solve(request).scheduled_matches[0].referee_id is None


def test_same_team_cannot_overlap_across_areas():
    request = make_request(3, hours=2)
    request.playing_areas.append(
        Resource(
            id=UUID(int=101),
            name="Quadra 2",
            availability=request.playing_areas[0].availability,
        )
    )
    assert solve(request).status == "infeasible"


def test_empty_schedule_is_success():
    request = make_request()
    request.matches = []
    assert solve(request).status == "optimal"
