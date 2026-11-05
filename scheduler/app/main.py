import logging
import time

from fastapi import FastAPI

from .models import GenerateRequest, Result
from .solver import solve

logger = logging.getLogger("uvicorn.error")
app = FastAPI(title="Tournament Scheduling Engine", version="1.0")


@app.get("/health")
def health():
    return {"status": "healthy"}


@app.post("/schedule/generate", response_model=Result)
def generate(request: GenerateRequest):
    started = time.monotonic()
    result = solve(request)
    logger.info(
        "tournament=%s matches=%d areas=%d referees=%d elapsed=%.3f status=%s",
        request.tournament_id,
        len(request.matches),
        len(request.playing_areas),
        len(request.referees),
        time.monotonic() - started,
        result.status,
    )
    return result
