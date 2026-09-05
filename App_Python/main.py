import os
import re
import time
import uuid
import zipfile
import sqlite3
import base64
from typing import List

from dotenv import load_dotenv
from fastapi import FastAPI, File, UploadFile, Request, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse, FileResponse, JSONResponse
from fastapi.templating import Jinja2Templates
from pydantic import BaseModel
from anthropic import Anthropic

# Carica le variabili da App_Python/.env (es. ANTHROPIC_API_KEY) nell'ambiente
# di processo, senza sovrascrivere eventuali variabili gia' impostate a
# livello di shell/sistema.
load_dotenv(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env"))

app = FastAPI()

# CORS aperto: serve al client Flutter (web/desktop, spesso su un'origine
# diversa dall'API, es. Codespaces con porte forwardate su sottodomini
# distinti) e a eventuali altri frontend per chiamare le API da un'origine
# diversa. Il client non usa cookie/credenziali, quindi allow_credentials
# resta False: questo permette di usare "*" letterale come allow_origins,
# come richiesto dallo spec CORS (con allow_credentials=True "*" non e'
# ammesso e va sostituito con un elenco esplicito di origini).
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


# Definizione del percorso assoluto per i templates
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
templates = Jinja2Templates(directory=os.path.join(BASE_DIR, "templates"))

# La API Key viene letta dalla variabile d'ambiente ANTHROPIC_API_KEY.
# Non inserire mai la chiave nel codice sorgente.
client = Anthropic()

MODEL = "claude-opus-5"

# Tipi immagine accettati dalle API di Claude
SUPPORTED_MEDIA_TYPES = {"image/jpeg", "image/png", "image/gif", "image/webp"}


class ReceiptItem(BaseModel):
    name: str
    price: float


class Receipt(BaseModel):
    items: List[ReceiptItem]


# Ogni elaborazione salva il proprio ZIP con un token univoco, cosi' il download
# successivo al riepilogo non puo' restituire il risultato di un'altra sessione.
OUTPUT_DIR = os.path.join("work", "output")
TOKEN_RE = re.compile(r"^[0-9a-f]{32}$")

# Per quanto tempo resta scaricabile uno ZIP gia' elaborato. Serve a coprire il
# tempo in cui l'utente guarda il riepilogo prima di premere "Scarica".
OUTPUT_MAX_AGE_SECONDS = 6 * 60 * 60

os.makedirs("uploads", exist_ok=True)
os.makedirs("work", exist_ok=True)
os.makedirs(OUTPUT_DIR, exist_ok=True)


def cleanup_output_dir():
    """Elimina gli ZIP piu' vecchi di OUTPUT_MAX_AGE_SECONDS.

    La pulizia non deve mai far fallire una richiesta: se un file e' bloccato
    (su Windows capita mentre e' in corso il download) viene semplicemente
    saltato e verra' rimosso al giro successivo.
    """
    cutoff = time.time() - OUTPUT_MAX_AGE_SECONDS
    removed = 0
    try:
        filenames = os.listdir(OUTPUT_DIR)
    except OSError:
        return 0

    for filename in filenames:
        if not filename.endswith(".zip"):
            continue
        path = os.path.join(OUTPUT_DIR, filename)
        try:
            if os.path.getmtime(path) < cutoff:
                os.remove(path)
                removed += 1
        except OSError:
            continue
    return removed


# Pulizia all'avvio: rimuove gli avanzi delle sessioni precedenti.
cleanup_output_dir()


@app.get("/", response_class=HTMLResponse)
async def home(request: Request):
    # Sintassi aggiornata per Jinja2 / Starlette
    return templates.TemplateResponse(request=request, name="index.html")


@app.get("/health")
async def health_check():
    # Endpoint leggero (nessuna dipendenza da SQLite/Claude) per verificare
    # rapidamente se il server e' raggiungibile e sveglio, es. dopo lo
    # spegnimento per inattivita' del piano gratuito di Render.
    return {"status": "ok"}


@app.post("/process/")
async def process_receipt(zip_file: UploadFile = File(...), image_file: UploadFile = File(...)):
    # L'intero corpo e' avvolto in un try/except che converte qualsiasi
    # eccezione in HTTPException: Starlette instrada gli handler registrati
    # su Exception/500 nudi verso ServerErrorMiddleware, che sta FUORI dal
    # CORSMiddleware, quindi la relativa risposta non avrebbe mai gli header
    # CORS. Le HTTPException invece passano dentro ExceptionMiddleware
    # (interno al CORSMiddleware) e ricevono correttamente quegli header.
    try:
        # 1. Salva ed estrai il backup ZIP
        zip_path = os.path.join("work", zip_file.filename)
        with open(zip_path, "wb") as f:
            f.write(await zip_file.read())

        try:
            with zipfile.ZipFile(zip_path, 'r') as zip_ref:
                zip_ref.extractall("work")
        except zipfile.BadZipFile:
            raise HTTPException(status_code=400, detail="Il file ZIP di backup non è valido.")

        db_path = os.path.join("work", "ekz_db")

        # 2. Converti la foto dello scontrino in base64 per le API di Claude
        image_bytes = await image_file.read()
        image_b64 = base64.standard_b64encode(image_bytes).decode('utf-8')
        media_type = image_file.content_type
        if media_type not in SUPPORTED_MEDIA_TYPES:
            media_type = "image/jpeg"

        # 3. Chiamata a Claude Vision API per estrarre i prodotti e i prezzi.
        # Lo schema JSON e' garantito dagli structured outputs: niente parsing manuale.
        prompt = (
            "Analizza questo scontrino ed estrai tutti gli articoli con i relativi prezzi. "
            "Escludi le righe che non sono prodotti (TOTALE, SUBTOTALE, RESTO, IVA, sconti, "
            "intestazione e dati del negozio). Il prezzo e' quello effettivamente pagato per la riga."
        )

        response = client.messages.parse(
            model=MODEL,
            max_tokens=8000,
            output_config={"effort": "low"},
            messages=[
                {
                    "role": "user",
                    "content": [
                        {"type": "image", "source": {"type": "base64", "media_type": media_type, "data": image_b64}},
                        {"type": "text", "text": prompt}
                    ]
                }
            ],
            output_format=Receipt,
        )

        extracted_data = response.parsed_output

        # 4. Logica SQLite: Aggiorna (se prezzo piu' basso) o Inserisci se manca
        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()

        updated = []
        inserted = []
        unchanged = []

        for item in extracted_data.items:
            name = item.name
            price = float(item.price)

            # Cerca se esiste gia' il prodotto
            cursor.execute("SELECT id, name, price FROM myitems WHERE name LIKE ?", (f"%{name}%",))
            row = cursor.fetchone()

            if row:
                db_id, db_name, current_price = row
                current_price = float(current_price or 0)
                # Aggiorna solo se il nuovo prezzo e' piu' basso o se non c'era prezzo
                if current_price == 0 or price < current_price:
                    cursor.execute("UPDATE myitems SET price = ? WHERE id = ?", (price, db_id))
                    updated.append({
                        "name": db_name,
                        "receipt_name": name,
                        "old_price": current_price,
                        "new_price": price,
                    })
                else:
                    unchanged.append({
                        "name": db_name,
                        "receipt_name": name,
                        "current_price": current_price,
                        "receipt_price": price,
                    })
            else:
                # Inserisci il nuovo prodotto con i campi sanitizzati per evitare crash
                cursor.execute("""
                    INSERT INTO myitems (name, price, last_cat, counttype, barcode, buycount)
                    VALUES (?, ?, '', 0, '', 0)
                """, (name, price))
                inserted.append({"name": name, "price": price})

        # Pulizia di sicurezza generale per i campi NULL
        cursor.execute("""
            UPDATE myitems SET
              last_cat = COALESCE(last_cat, ''),
              counttype = COALESCE(counttype, 0),
              barcode = COALESCE(barcode, ''),
              buycount = COALESCE(buycount, 0)
            WHERE last_cat IS NULL OR counttype IS NULL OR barcode IS NULL OR buycount IS NULL;
        """)

        conn.commit()
        conn.close()

        # 5. Ricrea il file ZIP aggiornato sotto un token univoco
        cleanup_output_dir()
        token = uuid.uuid4().hex
        output_zip = os.path.join(OUTPUT_DIR, f"{token}.zip")
        with zipfile.ZipFile(output_zip, 'w') as zip_out:
            zip_out.write(os.path.join("work", "ekz_db"), arcname="ekz_db")
            if os.path.exists(os.path.join("work", "settings.ekz")):
                zip_out.write(os.path.join("work", "settings.ekz"), arcname="settings.ekz")

        # 6. Riepilogo per l'interfaccia: il download avviene in un secondo momento
        return JSONResponse({
            "extracted_count": len(extracted_data.items),
            "updated_count": len(updated),
            "inserted_count": len(inserted),
            "unchanged_count": len(unchanged),
            "updated": updated,
            "inserted": inserted,
            "unchanged": unchanged,
            "download_url": f"/download/{token}",
        })
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=f"Errore durante l'elaborazione dello scontrino: {exc}",
        ) from exc


@app.get("/download/{token}")
async def download_backup(token: str):
    if not TOKEN_RE.match(token):
        raise HTTPException(status_code=404, detail="Download non trovato")

    output_zip = os.path.join(OUTPUT_DIR, f"{token}.zip")
    if not os.path.exists(output_zip):
        raise HTTPException(status_code=404, detail="Download non trovato o scaduto")

    return FileResponse(output_zip, filename="Backup_Aggiornato.zip", media_type="application/zip")