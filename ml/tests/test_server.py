from __future__ import annotations

from fastapi.testclient import TestClient

from src.api import server


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
            files={"file": ("sample.png", b"fake-png-bytes", "image/png")},
        )

    assert response.status_code == 200
    assert response.json() == {"zones": [], "total_zones": 0}