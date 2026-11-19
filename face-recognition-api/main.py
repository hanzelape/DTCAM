"""FastAPI backend for facial recognition.

Endpoints:
    GET  /health            -> service availability check
    POST /encode            -> create a face encoding from an image
    POST /recognize         -> match a captured face against known faces
    POST /recognize-live    -> head-turn liveness check + match (check-in)

Every request must carry the shared secret in the X-Api-Key header
(FACE_API_KEY env var). The service refuses to start without it.
"""

import hmac
import json
import logging
import os
import time
from contextlib import asynccontextmanager

from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import JSONResponse
from starlette.concurrency import run_in_threadpool

from services.face_analysis import get_face_analysis
from services.face_encoder import FaceEncodeError, encode_from_bytes, decode_encoding
from services.face_matcher import match_face, match_encoding
from services.liveness import check_liveness

logger = logging.getLogger("uvicorn.error")

API_KEY = os.getenv("FACE_API_KEY", "")
MAX_LIVE_FRAMES = 15
MAX_FRAME_BYTES = 2 * 1024 * 1024


@asynccontextmanager
async def lifespan(_: FastAPI):
    if len(API_KEY) < 16:
        raise RuntimeError(
            "FACE_API_KEY is not set (or shorter than 16 characters). "
            "Set the same value as FaceRecognition__ApiKey in the .NET app."
        )
    started = time.perf_counter()
    await run_in_threadpool(get_face_analysis)
    logger.info("face_model_loaded elapsed_ms=%.1f", (time.perf_counter() - started) * 1000)
    yield


app = FastAPI(title="Face Recognition API", version="1.0.0", lifespan=lifespan)


@app.middleware("http")
async def require_api_key(request: Request, call_next):
    supplied = request.headers.get("x-api-key", "")
    if not API_KEY or not hmac.compare_digest(supplied.encode(), API_KEY.encode()):
        return JSONResponse(status_code=401, content={"detail": "Invalid or missing API key."})
    return await call_next(request)


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/encode")
async def create_encoding(image: UploadFile = File(...), known_faces: str | None = Form(None)):
    """Create a face encoding from an uploaded image.

    Returns:
        {"encoding": "<json array string>"}
    """
    started = time.perf_counter()
    try:
        image_bytes = await image.read()

        known = None
        if known_faces is not None:
            try:
                known = json.loads(known_faces)
            except json.JSONDecodeError:
                raise HTTPException(status_code=400, detail="Invalid known_faces JSON.")
            if not isinstance(known, list):
                raise HTTPException(status_code=400, detail="known_faces must be a JSON array.")

        try:
            encoding = await run_in_threadpool(encode_from_bytes, image_bytes)
        except FaceEncodeError as exc:
            raise HTTPException(status_code=400, detail=str(exc))

        result = {"encoding": encoding}
        if known is not None:
            result.update(match_encoding(decode_encoding(encoding), known).to_dict())
        return result
    finally:
        logger.info(
            "face_api_operation=encode elapsed_ms=%.1f image_bytes=%d",
            (time.perf_counter() - started) * 1000,
            len(image_bytes) if "image_bytes" in locals() else 0,
        )


@app.post("/recognize")
async def recognize(
    image: UploadFile = File(...),
    known_faces: str = Form(...),
):
    """Match a captured face against known faces.

    known_faces is a JSON string: [{ "employee_id": int, "employee_name": str, "encoding": "<json>" }]
    """
    started = time.perf_counter()
    try:
        image_bytes = await image.read()

        try:
            known = json.loads(known_faces)
        except json.JSONDecodeError:
            raise HTTPException(status_code=400, detail="Invalid known_faces JSON.")

        result = await run_in_threadpool(match_face, image_bytes, known)
        return result.to_dict()
    finally:
        logger.info(
            "face_api_operation=recognize elapsed_ms=%.1f image_bytes=%d",
            (time.perf_counter() - started) * 1000,
            len(image_bytes) if "image_bytes" in locals() else 0,
        )


@app.post("/recognize-live")
async def recognize_live(
    images: list[UploadFile] = File(...),
    challenge: str = Form(...),
    known_faces: str = Form(...),
):
    """Head-turn liveness check over an ordered burst of frames, then identify.

    The challenge ("left"/"right") is chosen by the .NET server, not the client.
    """
    started = time.perf_counter()
    try:
        if not 1 <= len(images) <= MAX_LIVE_FRAMES:
            raise HTTPException(status_code=400, detail=f"Send between 1 and {MAX_LIVE_FRAMES} frames.")

        frames = []
        for upload in images:
            data = await upload.read(MAX_FRAME_BYTES + 1)
            if len(data) > MAX_FRAME_BYTES:
                raise HTTPException(status_code=400, detail="Frame too large.")
            frames.append(data)

        try:
            known = json.loads(known_faces)
        except json.JSONDecodeError:
            raise HTTPException(status_code=400, detail="Invalid known_faces JSON.")
        if not isinstance(known, list):
            raise HTTPException(status_code=400, detail="known_faces must be a JSON array.")

        try:
            liveness = await run_in_threadpool(check_liveness, frames, challenge)
        except FaceEncodeError as exc:
            raise HTTPException(status_code=400, detail=str(exc))

        if not liveness.passed:
            return {
                "recognized": False,
                "employee_id": None,
                "employee_name": None,
                "confidence": 0.0,
                "liveness_passed": False,
                "liveness_reason": liveness.reason,
            }

        result = match_encoding(liveness.reference_embedding, known).to_dict()
        result.update({"liveness_passed": True, "liveness_reason": None})
        return result
    finally:
        logger.info(
            "face_api_operation=recognize_live elapsed_ms=%.1f frames=%d",
            (time.perf_counter() - started) * 1000,
            len(images),
        )


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host=os.getenv("HOST", "127.0.0.1"), port=int(os.getenv("PORT", "5000")))
