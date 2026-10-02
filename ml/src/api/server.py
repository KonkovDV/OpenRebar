"""
FastAPI server exposing the segmentation model as an HTTP service.
C# infrastructure calls this via HTTP to get polygon zone data from PNG isolines.

Usage from repository root:
    uvicorn ml.src.api.server:app --host 127.0.0.1 --port 8101

Usage from ml/ directory:
    uvicorn src.api.server:app --host 127.0.0.1 --port 8101

Bind to 127.0.0.1. The service has no authentication; expose it only behind a proxy that adds it.
"""

from __future__ import annotations

from contextlib import asynccontextmanager
import os
import tempfile
from pathlib import Path

from fastapi import FastAPI, File, HTTPException, Query, UploadFile
from pydantic import BaseModel

from ..segmentation.model import IsolineUNet
from ..segmentation.predict import load_model, segment_isoline_image

MAX_UPLOAD_BYTES = 20 * 1024 * 1024
MAX_MIN_AREA = 1.0e9
_READ_CHUNK_BYTES = 1024 * 1024
_MAGIC_SUFFIXES: tuple[tuple[bytes, str], ...] = (
    (b"\x89PNG\r\n\x1a\n", ".png"),
    (b"\xff\xd8\xff", ".jpg"),
)
BASE_DIR = Path(__file__).resolve().parents[2]
MODEL_PATH = Path(os.environ.get("OpenRebar_MODEL_PATH", str(BASE_DIR / "models" / "isoline_unet.pt")))


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _model
    if MODEL_PATH.exists():
        _model = load_model(MODEL_PATH, device=_device)
    try:
        yield
    finally:
        _model = None

app = FastAPI(
    title="OpenRebar Isoline Segmentation API",
    version="0.1.0",
    description="Segmentation service for LIRA-SAPR isoline images",
    lifespan=lifespan,
)

# Global model instance (loaded on startup)
_model: IsolineUNet | None = None
_device: str = "cpu"


class PolygonZone(BaseModel):
    class_id: int
    polygon: list[tuple[int, int]]
    area: float
    bbox: tuple[int, int, int, int]


class SegmentationResponse(BaseModel):
    zones: list[PolygonZone]
    total_zones: int


@app.get("/health")
async def health() -> dict[str, str]:
    status = "ok" if _model is not None else "model_not_loaded"
    return {"status": status}


@app.post("/segment", response_model=SegmentationResponse)
async def segment(
    file: UploadFile = File(..., description="PNG/JPG isoline image"),
    min_area: float = Query(1000.0, ge=0.0, le=MAX_MIN_AREA),
) -> SegmentationResponse:
    if _model is None:
        raise HTTPException(503, "Model not loaded. Place model at models/isoline_unet.pt")

    content = await _read_limited(file, MAX_UPLOAD_BYTES)
    # The suffix comes from the file content, never from the client file name.
    suffix = _suffix_from_magic(content)
    if suffix is None:
        raise HTTPException(415, "Only PNG and JPEG images are accepted.")

    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        tmp.write(content)
        tmp_path = Path(tmp.name)

    try:
        zones = segment_isoline_image(_model, tmp_path, _device, min_area)
        return SegmentationResponse(
            zones=[
                PolygonZone(
                    class_id=z["class_id"],
                    polygon=z["polygon"],
                    area=z["area"],
                    bbox=z["bbox"],
                )
                for z in zones
            ],
            total_zones=len(zones),
        )
    finally:
        tmp_path.unlink(missing_ok=True)


async def _read_limited(file: UploadFile, limit: int) -> bytes:
    """Read the upload in chunks and stop as soon as it passes the limit."""
    buffer = bytearray()
    while True:
        chunk = await file.read(_READ_CHUNK_BYTES)
        if not chunk:
            return bytes(buffer)
        buffer.extend(chunk)
        if len(buffer) > limit:
            raise HTTPException(413, f"Uploaded file is too large. Limit: {limit} bytes")


def _suffix_from_magic(content: bytes) -> str | None:
    for magic, suffix in _MAGIC_SUFFIXES:
        if content.startswith(magic):
            return suffix
    return None
