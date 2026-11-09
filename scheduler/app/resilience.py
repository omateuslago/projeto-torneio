"""Independent single-incident analysis. Never changes assignments or solves again."""

from datetime import timedelta

from .models import FixedMatch, Resilience


def percentage(safe, total):
    return (200 * safe + total) // (2 * total)


def assess(request, assignments):
    matches = {m.id: m for m in request.matches}
    generated = [
        FixedMatch(
            match_id=a.match_id,
            home_team_id=matches[a.match_id].home_team_id,
            away_team_id=matches[a.match_id].away_team_id,
            starts_at=a.starts_at,
            ends_at=a.starts_at + timedelta(minutes=request.match_duration_minutes),
            playing_area_id=a.playing_area_id,
            referee_id=a.referee_id,
        )
        for a in assignments
    ]
    targets = generated + [
        m
        for m in request.existing_schedule
        if m.counts_for_resilience
        and request.horizon_start <= m.starts_at
        and m.ends_at <= request.horizon_end
    ]
    if not targets:
        return None
    targets.sort(key=lambda m: (m.starts_at, str(m.match_id)))
    occupied = generated + request.existing_schedule
    teams = {r.id: r for r in request.teams}
    areas = {r.id: r for r in request.playing_areas}
    referees = {r.id: r for r in request.referees}

    def available(resources, key, start, end):
        resource = resources.get(key)
        return resource is not None and any(
            w.starts_at <= start and end <= w.ends_at for w in resource.availability
        )

    def overlaps(other, start, end):
        return other.starts_at < end and start < other.ends_at

    def fallback(resources, attribute, match):
        return any(
            key != getattr(match, attribute)
            and available(resources, key, match.starts_at, match.ends_at)
            and not any(
                getattr(other, attribute) == key
                and overlaps(other, match.starts_at, match.ends_at)
                for other in occupied
                if other.match_id != match.match_id
            )
            for key in resources
        )

    delay_safe = area_safe = referee_safe = referee_total = 0
    delay_failures = {15: 0, 30: 0}
    for match in targets:
        for minutes in (15, 30):
            end = match.ends_at + timedelta(minutes=minutes)
            team_ids = {match.home_team_id, match.away_team_id}
            safe = end <= request.horizon_end and all(
                available(teams, team, match.starts_at, end) for team in team_ids
            )
            safe = safe and available(
                areas, match.playing_area_id, match.starts_at, end
            )
            if match.referee_id is not None:
                safe = safe and available(
                    referees, match.referee_id, match.starts_at, end
                )
            safe = safe and not any(
                overlaps(other, match.starts_at, end)
                and (
                    bool(team_ids & {other.home_team_id, other.away_team_id})
                    or other.playing_area_id == match.playing_area_id
                    or (
                        match.referee_id is not None
                        and other.referee_id == match.referee_id
                    )
                )
                for other in occupied
                if other.match_id != match.match_id
            )
            delay_safe += int(safe)
            delay_failures[minutes] += int(not safe)
        area_safe += int(fallback(areas, "playing_area_id", match))
        if match.referee_id is not None:
            referee_total += 1
            referee_safe += int(fallback(referees, "referee_id", match))
    count = len(targets)
    delay = percentage(delay_safe, count * 2)
    area = percentage(area_safe, count)
    referee = percentage(referee_safe, referee_total) if referee_total else None
    components = [delay, area] + ([] if referee is None else [referee])
    total = count * 3 + referee_total
    observations = [
        f"Atraso de {minutes} minutos: {failures} de {count} partidas vulneráveis por conflito, disponibilidade ou limite do horizonte."
        for minutes, failures in delay_failures.items()
    ]
    observations.append(
        f"{area_safe} de {count} partidas possuem uma área alternativa livre no mesmo horário."
    )
    observations.append(
        f"{referee_safe} de {referee_total} partidas com árbitro possuem substituto disponível."
        if referee_total
        else "Árbitros: não aplicável; nenhuma partida analisada possui árbitro."
    )
    return Resilience(
        score=(2 * sum(components) + len(components)) // (2 * len(components)),
        delay_score=delay,
        area_score=area,
        referee_score=referee,
        scenarios_evaluated=total,
        vulnerable_scenarios=total - delay_safe - area_safe - referee_safe,
        observations=observations,
    )
