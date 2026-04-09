"""
FastAPI server exposing the segmentation model as an HTTP service.
C# infrastructure calls this via HTTP to get polygon zone data from PNG isolines.

Usage:
    uvicorn ml.src.api.server:app --host 0.0.0.0 --port 8101
"""

from __future__ import annotations

import tempfile
from pathlib import Path

from fastapi import FastAPI, File, UploadFile, HTTPException
from pydantic import BaseModel

from ml.src.segmentation.model import IsolineUNet
from ml.src.segmentation.predict import load_model, segment_isoline_image

app = FastAPI(
    title="A101 Isoline Segmentation API",
    version="0.1.0",
    description="Segmentation service for LIRA-SAPR isoline images",
)

# Global model instance (loaded on startup)
_model: IsolineUNet | None = None
_device: str = "cpu"
MODEL_PATH = Path("models/isoline_unet.pt")


class PolygonZone(BaseModel):
    class_id: int
    polygon: list[tuple[int, int]]
    area: float
    bbox: tuple[int, int, int, int]


class SegmentationResponse(BaseModel):
    zones: list[PolygonZone]
    total_zones: int


@app.on_event("startup")
async def startup() -> None:
    global _model
    if MODEL_PATH.exists():
        _model = load_model(MODEL_PATH, device=_device)
    # If model not found, endpoints will return 503


@app.get("/health")
async def health() -> dict[str, str]:
    status = "ok" if _model is not None else "model_not_loaded"
    return {"status": status}


@app.post("/segment", response_model=SegmentationResponse)
async def segment(
    file: UploadFile = File(..., description="PNG/JPG isoline image"),
    min_area: float = 1000.0,
) -> SegmentationResponse:
    if _model is None:
        raise HTTPException(503, "Model not loaded. Place model at models/isoline_unet.pt")

    # Save upload to temp file
    suffix = Path(file.filename or "image.png").suffix
    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        content = await file.read()
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
