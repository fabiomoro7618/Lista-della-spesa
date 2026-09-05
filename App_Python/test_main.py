"""Test strutturali dell'API FastAPI (App_Python/main.py).

Non esercitano POST /process/ end-to-end: richiederebbe una vera
ANTHROPIC_API_KEY e una chiamata reale (a pagamento) a Claude Vision.
Qui viene impostata una chiave fittizia solo per permettere l'import
del modulo (il client Anthropic viene istanziato a livello di modulo).
"""

import os

os.environ.setdefault("ANTHROPIC_API_KEY", "sk-test-dummy-for-import-check")

from fastapi.testclient import TestClient

import main

client = TestClient(main.app)


def test_home_page_ok():
    response = client.get("/")
    assert response.status_code == 200
    assert "text/html" in response.headers["content-type"]
    assert "Aggiorna Spesa" in response.text


def test_health_check_ok():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


def test_cors_preflight_allows_any_origin():
    # Con allow_credentials=False, Starlette restituisce "*" letterale
    # (ammesso dallo spec CORS solo quando le credenziali sono disabilitate).
    response = client.options(
        "/process/",
        headers={
            "Origin": "http://example.com",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type",
        },
    )
    assert response.status_code == 200
    assert response.headers.get("access-control-allow-origin") == "*"
    assert response.headers.get("access-control-allow-credentials") is None
    assert "POST" in response.headers.get("access-control-allow-methods", "")


def test_cors_header_present_on_get():
    response = client.get("/", headers={"Origin": "http://example.com"})
    assert response.status_code == 200
    assert response.headers.get("access-control-allow-origin") == "*"


def test_unhandled_exception_still_has_cors_header():
    # Un errore non gestito deve comunque ricevere gli header CORS, altrimenti
    # il browser lo segnala come "Failed to fetch" invece di mostrare
    # l'errore reale (vedi unhandled_exception_handler in main.py).
    response = client.post(
        "/process/",
        headers={"Origin": "http://example.com"},
        files={
            "zip_file": ("backup.zip", b"non e' uno zip valido", "application/zip"),
            "image_file": ("scontrino.jpg", b"\xff\xd8\xff", "image/jpeg"),
        },
    )
    assert response.status_code == 400
    assert response.json()["detail"] == "Il file ZIP di backup non è valido."
    assert response.headers.get("access-control-allow-origin") == "*"


def test_download_with_malformed_token_returns_404():
    response = client.get("/download/tokenNonValido")
    assert response.status_code == 404
    assert response.json()["detail"] == "Download non trovato"


def test_download_with_valid_but_missing_token_returns_404():
    token = "a" * 32
    response = client.get(f"/download/{token}")
    assert response.status_code == 404
    assert response.json()["detail"] == "Download non trovato o scaduto"
