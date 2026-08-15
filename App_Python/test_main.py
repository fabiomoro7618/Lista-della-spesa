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


def test_cors_preflight_allows_any_origin():
    # Con allow_credentials=True, Starlette rispecchia l'Origin esatta della
    # richiesta invece di restituire "*" letterale (richiesto dallo spec
    # CORS quando le credenziali sono abilitate).
    response = client.options(
        "/process/",
        headers={
            "Origin": "http://example.com",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type",
        },
    )
    assert response.status_code == 200
    assert response.headers.get("access-control-allow-origin") == "http://example.com"
    assert response.headers.get("access-control-allow-credentials") == "true"
    assert "POST" in response.headers.get("access-control-allow-methods", "")


def test_cors_header_present_on_get():
    response = client.get("/", headers={"Origin": "http://example.com"})
    assert response.status_code == 200
    assert response.headers.get("access-control-allow-origin") == "http://example.com"
    assert response.headers.get("access-control-allow-credentials") == "true"


def test_download_with_malformed_token_returns_404():
    response = client.get("/download/tokenNonValido")
    assert response.status_code == 404
    assert response.json()["detail"] == "Download non trovato"


def test_download_with_valid_but_missing_token_returns_404():
    token = "a" * 32
    response = client.get(f"/download/{token}")
    assert response.status_code == 404
    assert response.json()["detail"] == "Download non trovato o scaduto"
