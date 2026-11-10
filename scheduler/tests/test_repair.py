from datetime import timedelta
from uuid import UUID

from app.models import FixedMatch, Window
from app.repair import Incident, RepairRequest, repair
from tests.test_solver import START, make_request


def prepared(count=2, hours=6):
    r = make_request(count, hours=hours)
    r.existing_schedule = [
        FixedMatch(
            match_id=m.id,
            home_team_id=m.home_team_id,
            away_team_id=m.away_team_id,
            starts_at=START + timedelta(hours=i),
            ends_at=START + timedelta(hours=i + 1),
            playing_area_id=r.playing_areas[0].id,
            counts_for_fairness=True,
            counts_for_resilience=True,
        )
        for i, m in enumerate(r.matches)
    ]
    r.matches = []
    return r


def area_incident(r, minutes=60):
    return Incident(
        kind="playingAreaUnavailable",
        resource_id=r.playing_areas[0].id,
        starts_at=START,
        ends_at=START + timedelta(minutes=minutes),
    )


def test_no_impact_never_reorders():
    r = prepared()
    incident = area_incident(r)
    incident.starts_at += timedelta(hours=4)
    incident.ends_at += timedelta(hours=4)
    result = repair(RepairRequest(schedule=r, incident=incident))
    assert result.status == "optimal" and result.scheduled_matches == []
    assert "Nenhuma" in result.warnings[0]


def test_free_area_changes_only_resource_and_is_deterministic_immutable():
    r = prepared(3)
    r.playing_areas.append(r.playing_areas[0].model_copy(update={"id": UUID(int=800)}))
    before = r.model_dump()
    request = RepairRequest(schedule=r, incident=area_incident(r))
    result = repair(request)
    assert result == repair(request)
    assert result.status == "optimal"
    old = {m.match_id: m for m in r.existing_schedule}
    assert (
        sum(
            a.playing_area_id != old[a.match_id].playing_area_id
            for a in result.scheduled_matches
        )
        == 1
    )
    assert all(
        a.starts_at == old[a.match_id].starts_at for a in result.scheduled_matches
    )
    assert r.model_dump() == before
    assert result.fairness and result.resilience


def test_referee_substitute_keeps_start():
    r = prepared()
    r.existing_schedule[0].referee_id = r.referees[0].id
    alternate = r.referees[0].model_copy(update={"id": UUID(int=801)})
    r.referees.append(alternate)
    i = Incident(
        kind="refereeUnavailable",
        resource_id=r.referees[0].id,
        starts_at=START,
        ends_at=START + timedelta(hours=1),
    )
    result = repair(RepairRequest(schedule=r, incident=i))
    assert result.scheduled_matches[0].referee_id == alternate.id
    assert result.scheduled_matches[0].starts_at == START


def test_team_unavailability_prefers_nearest_time_and_original_area():
    r = prepared()
    r.playing_areas.append(r.playing_areas[0].model_copy(update={"id": UUID(int=800)}))
    i = Incident(
        kind="teamUnavailable",
        resource_id=r.teams[0].id,
        starts_at=START,
        ends_at=START + timedelta(minutes=30),
    )
    result = repair(RepairRequest(schedule=r, incident=i))
    assert result.scheduled_matches[0].starts_at == START + timedelta(minutes=30)
    assert result.scheduled_matches[0].playing_area_id == r.playing_areas[0].id


def test_one_change_wins_over_two_even_with_more_displacement():
    r = prepared(3, hours=4)
    result = repair(RepairRequest(schedule=r, incident=area_incident(r)))
    old = {m.match_id: m for m in r.existing_schedule}
    changed = [
        a for a in result.scheduled_matches if a.starts_at != old[a.match_id].starts_at
    ]
    assert len(changed) == 1
    assert changed[0].starts_at == START + timedelta(hours=3)


def test_cascade_when_second_move_is_necessary():
    r = prepared(3, hours=4)
    # First match's unique teams constrain it to finish by hour 3;
    # freeze third game, forcing the first two games to swap after disruption.
    first, second, third = r.existing_schedule
    third.counts_for_resilience = False
    for team in r.teams:
        if team.id == first.away_team_id:
            team.availability = [
                Window(starts_at=START, ends_at=START + timedelta(hours=3))
            ]
    result = repair(RepairRequest(schedule=r, incident=area_incident(r)))
    assert result.status == "optimal"
    assert len(result.scheduled_matches) == 2
    assert {a.starts_at for a in result.scheduled_matches} == {
        START + timedelta(hours=1),
        START + timedelta(hours=3),
    }


def test_delayed_game_fixed_and_later_game_moves():
    r = prepared(3, hours=5)
    original = r.model_dump()
    i = Incident(
        kind="matchDelayed", match_id=r.existing_schedule[0].match_id, delay_minutes=30
    )
    result = repair(RepairRequest(schedule=r, incident=i))
    assert result.status == "optimal"
    assert all(a.match_id != i.match_id for a in result.scheduled_matches)
    assert all(
        a.starts_at >= START + timedelta(minutes=90) for a in result.scheduled_matches
    )
    assert r.model_dump() == original


def test_immutable_impacted_game_is_infeasible():
    r = prepared()
    r.existing_schedule[0].counts_for_resilience = False
    assert (
        repair(RepairRequest(schedule=r, incident=area_incident(r))).status
        == "infeasible"
    )


def test_impossible_never_relaxes_availability():
    r = prepared(hours=1)
    assert (
        repair(RepairRequest(schedule=r, incident=area_incident(r))).status
        == "infeasible"
    )


def test_external_occupation_is_fixed_and_never_returned_as_target():
    r = prepared(hours=4)
    external = r.existing_schedule[0].model_copy(
        update={
            "match_id": UUID(int=999),
            "starts_at": START + timedelta(hours=1),
            "ends_at": START + timedelta(hours=2),
            "counts_for_fairness": False,
            "counts_for_resilience": False,
        }
    )
    r.existing_schedule.append(external)
    result = repair(RepairRequest(schedule=r, incident=area_incident(r)))
    assert len(result.scheduled_matches) == 1
    assert result.scheduled_matches[0].starts_at == START + timedelta(hours=2)
    assert result.scheduled_matches[0].match_id != external.match_id


def test_outside_horizon_cannot_move():
    r = prepared(3, hours=3)
    r.horizon_end = START + timedelta(hours=2)
    result = repair(RepairRequest(schedule=r, incident=area_incident(r)))
    assert result.status == "infeasible"


def test_http_repair_contract_and_invalid_delay():
    from fastapi.testclient import TestClient
    from app.main import app

    r = prepared()
    data = RepairRequest(schedule=r, incident=area_incident(r)).model_dump(
        mode="json", by_alias=True
    )
    response = TestClient(app).post("/schedule/repair", json=data)
    assert response.status_code == 200
    assert response.json()["status"] == "optimal"
    data["incident"] = {
        "kind": "matchDelayed",
        "matchId": str(r.existing_schedule[0].match_id),
        "delayMinutes": 0,
    }
    assert TestClient(app).post("/schedule/repair", json=data).status_code == 422
