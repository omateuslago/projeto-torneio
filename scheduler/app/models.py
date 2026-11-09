from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class Contract(BaseModel):
    model_config = ConfigDict(
        alias_generator=to_camel, populate_by_name=True, extra="forbid"
    )


class Window(Contract):
    starts_at: AwareDatetime
    ends_at: AwareDatetime

    @model_validator(mode="after")
    def interval(self):
        if self.starts_at >= self.ends_at:
            raise ValueError("O início deve preceder o fim.")
        return self


class Resource(Contract):
    id: UUID
    name: str = Field(max_length=120)
    availability: list[Window] = Field(max_length=1000)


class Match(Contract):
    id: UUID
    home_team_id: UUID
    away_team_id: UUID


class FixedMatch(Window):
    match_id: UUID
    home_team_id: UUID
    away_team_id: UUID
    playing_area_id: UUID
    referee_id: UUID | None = None
    counts_for_fairness: bool = False


class GenerateRequest(Contract):
    tournament_id: UUID
    horizon_start: AwareDatetime
    horizon_end: AwareDatetime
    match_duration_minutes: int = Field(ge=5, le=480)
    granularity_minutes: int = Field(default=15, ge=1, le=60)
    require_referee: bool = False
    preferred_rest_minutes: int = Field(default=60, ge=0, le=240)
    matches: list[Match] = Field(max_length=200)
    teams: list[Resource] = Field(max_length=128)
    playing_areas: list[Resource] = Field(max_length=100)
    referees: list[Resource] = Field(max_length=100)
    existing_schedule: list[FixedMatch] = Field(max_length=20000)

    @model_validator(mode="after")
    def validate_request(self):
        if (
            sum(
                len(r.availability)
                for r in self.teams + self.playing_areas + self.referees
            )
            > 10000
        ):
            raise ValueError("Limite de 10000 intervalos por solicitação.")
        seconds = (self.horizon_end - self.horizon_start).total_seconds()
        if not 0 < seconds <= 31 * 86400:
            raise ValueError("Horizonte deve ter até 31 dias.")
        for group in (self.matches, self.teams, self.playing_areas, self.referees):
            if len({x.id for x in group}) != len(group):
                raise ValueError("IDs duplicados.")
        ids = {t.id for t in self.teams}
        if any(
            m.home_team_id not in ids
            or m.away_team_id not in ids
            or m.home_team_id == m.away_team_id
            for m in self.matches
        ):
            raise ValueError("Equipes inválidas.")
        if {m.id for m in self.matches} & {m.match_id for m in self.existing_schedule}:
            raise ValueError("Partida fixa não pode ser movida.")
        return self


class Assignment(Contract):
    match_id: UUID
    starts_at: datetime
    playing_area_id: UUID
    referee_id: UUID | None = None


class Fairness(Contract):
    score: int = Field(ge=0, le=100)
    rest_score: int = Field(ge=0, le=100)
    back_to_back_score: int = Field(ge=0, le=100)
    time_balance_score: int = Field(ge=0, le=100)
    observations: list[str]


class Result(Contract):
    status: Literal["optimal", "feasible", "infeasible", "timeout", "error"]
    scheduled_matches: list[Assignment] = Field(default_factory=list)
    warnings: list[str] = Field(default_factory=list)
    fairness: Fairness | None = None
