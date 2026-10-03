from __future__ import annotations

from io import BytesIO

from fastapi.testclient import TestClient
from PIL import Image

from src.api import server

PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


def _png(width: int = 1, height: int = 1) -> bytes:
    output = BytesIO()
    Image.new("RGB", (width, height), color=(255, 255, 255)).save(output, format="PNG")
    return output.getvalue()


def test_health_reports_model_not_loaded_when_checkpoint_missing() -> None:
    with TestClient(server.app) as client:
        response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "model_not_loaded"}


def test_segment_rejects_large_upload(monkeypatch) -> None:
    monkeypatch.setattr(server, "_model", object())
    monkeypatch.setattr(server, "segment_isoline_image", lambda *args, **kwargs: [])

    payload = b"0" * (server.MAX_UPLOAD_BYTES + 1)

    with TestClient(server.app) as client:
        response = client.post(
            "/segment",
            files={"file": ("large.png", payload, "image/png")},
        )

    assert response.status_code == 413
    assert "too large" in response.json()["detail"].lower()


def test_segment_returns_empty_result_for_stubbed_model(monkeypatch) -> None:
    monkeypatch.setattr(server, "_model", object())
    monkeypatch.setattr(server, "segment_isoline_image", lambda *args, **kwargs: [])

    with TestClient(server.app) as client:
        response = client.post(
            "/segment",
            files={"file": ("sample.png", _png(), "image/png")},
        )

    assert response.status_code == 200
    assert response.json() == {"zones": [], "total_zones": 0}


def test_segment_rejects_content_that_is_not_an_image(monkeypatch) -> None:
    monkeypatch.setattr(server, "_model", object())
    monkeypatch.setattr(server, "segment_isoline_image", lambda *args, **kwargs: [])

    with TestClient(server.app) as client:
        response = client.post(
            "/segment",
            files={"file": ("evil.png", b"#!/bin/sh\necho", "image/png")},
        )

    assert response.status_code == 415


def test_segment_rejects_negative_min_area(monkeypatch) -> None:
    monkeypatch.setattr(server, "_model", object())
    monkeypatch.setattr(server, "segment_isoline_image", lambda *args, **kwargs: [])

    with TestClient(server.app) as client:
        response = client.post(
            "/segment?min_area=-1",
            files={"file": ("sample.png", _png(), "image/png")},
        )

    assert response.status_code == 422


def test_segment_rejects_image_with_too_many_pixels(monkeypatch) -> None:
    monkeypatch.setattr(server, "_model", object())
    monkeypatch.setattr(server, "MAX_IMAGE_PIXELS", 100)
    monkeypatch.setattr(server, "segment_isoline_image", lambda *args, **kwargs: [])

    with TestClient(server.app) as client:
        response = client.post(
            "/segment",
            files={"file": ("wide.png", _png(11, 10), "image/png")},
        )

    assert response.status_code == 413
    assert "too many pixels" in response.json()["detail"].lower()
