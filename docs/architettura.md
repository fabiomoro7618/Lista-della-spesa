# Architettura & Backend

Il backend vive in [`App_Python/main.py`](https://github.com/fabiomoro7618/Lista-della-spesa/blob/main/App_Python/main.py):
un'unica applicazione **FastAPI**, servita da **Uvicorn**, senza framework di
persistenza aggiuntivi — l'archivio prodotti è il file SQLite `ekz_db`
contenuto nello ZIP di backup che l'utente carica ad ogni richiesta.

## Endpoint esposti

| Metodo | Percorso | Scopo |
|---|---|---|
| `GET` | `/` | Pagina HTML statica (Jinja2), non usata dalla UI Flutter |
| `POST` | `/process/` | Riceve ZIP + foto scontrino, restituisce il riepilogo dell'elaborazione |
| `GET` | `/download/{token}` | Scarica lo ZIP aggiornato generato da una precedente `/process/` |

## Configurazione (`.env`)

La chiave dell'API Claude non è mai nel codice sorgente: viene letta da
`App_Python/.env` (file **escluso da Git**, vedi `.gitignore`) tramite
`python-dotenv`:

```python
load_dotenv(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env"))
```

```dotenv title="App_Python/.env"
ANTHROPIC_API_KEY=sk-ant-api03-...
```

## Flusso di `POST /process/`

1. **Salvataggio ed estrazione dello ZIP** — il backup caricato viene scritto
   in `work/` ed estratto (`zipfile.ZipFile.extractall`). Uno ZIP non valido
   produce `400 Il file ZIP di backup non è valido.`, non un errore generico.
2. **Codifica dell'immagine** — la foto dello scontrino viene letta e
   convertita in Base64; il `content_type` ricevuto viene validato contro un
   insieme di MIME type supportati (`image/jpeg`, `image/png`, `image/gif`,
   `image/webp`), con fallback a `image/jpeg`.
3. **Chiamata a Claude Vision** — `client.messages.parse(..., output_format=Receipt)`
   usa gli **structured outputs**: il modello restituisce direttamente una
   lista tipizzata di `ReceiptItem(name, price)`, senza bisogno di fare
   parsing manuale di testo libero.
4. **Confronto con l'archivio (`ekz_db`)** — per ogni prodotto estratto:
      - si cerca una corrispondenza con `SELECT ... WHERE name LIKE '%nome%'`;
      - se il prodotto **esiste** e il prezzo attuale è `0` (mai registrato)
        oppure il nuovo prezzo è **più basso**, il prezzo viene aggiornato
        (`updated`); altrimenti resta invariato (`unchanged`);
      - se il prodotto **non esiste**, viene inserito da zero (`inserted`).
5. **Ricostruzione dello ZIP** — un nuovo archivio (`ekz_db` + `settings.ekz`
   se presente) viene scritto sotto un **token univoco** (`uuid4().hex`) in
   `work/output/`, così il download successivo non può mai restituire il
   risultato di un'altra sessione concorrente.
6. **Risposta JSON** con i conteggi e i dettagli di `updated` / `inserted` /
   `unchanged`, più `download_url` per il passo successivo.

!!! note "Perché il matching usa `LIKE`"
    I nomi prodotto estratti da Claude non coincidono mai carattere per
    carattere con quelli in archivio (abbreviazioni, maiuscole/minuscole,
    spazi). Il match parziale (`LIKE '%nome%'`) è una scelta pragmatica: va
    bene per un archivio personale di dimensioni contenute, ma può produrre
    falsi positivi se due prodotti hanno nomi molto simili.

## Pulizia automatica degli ZIP generati

Ogni ZIP prodotto da `/process/` scade dopo `OUTPUT_MAX_AGE_SECONDS` (6 ore):
`cleanup_output_dir()` viene eseguita all'avvio del server e prima di ogni
nuova elaborazione, rimuovendo i file più vecchi in `work/output/`. Un file
bloccato da un download in corso viene semplicemente saltato e ripulito al
giro successivo.

## CORS: perché `allow_credentials=False`

```python
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)
```

Il client Flutter non usa cookie né credenziali, quindi
`allow_credentials=False` permette di restituire `*` **letterale** come
`Access-Control-Allow-Origin` — è la configurazione più semplice e
compatibile con qualunque origine (utile perché su Codespaces l'app Web è
servita su un sottodominio diverso da quello dell'API, es.
`*-8080.app.github.dev` → `*-8000.app.github.dev`).

!!! warning "Errori non gestiti e header CORS"
    Starlette instrada gli handler registrati su `Exception`/`500` "nudi"
    verso `ServerErrorMiddleware`, che si trova **fuori** dal
    `CORSMiddleware` — una risposta 500 generata così **non ha header CORS**
    e il browser la segnala come `Failed to fetch` invece di mostrare
    l'errore reale. Per questo l'intero corpo di `process_receipt` è avvolto
    in un `try/except` che converte **qualsiasi** eccezione in
    `HTTPException`: le `HTTPException` passano dentro `ExceptionMiddleware`
    (interno al `CORSMiddleware`) e ricevono sempre gli header corretti,
    anche in caso di errore.

## Dipendenze principali

```
fastapi
uvicorn
python-dotenv
anthropic
```

Nessun `requirements.txt` è ancora presente nel repository: le dipendenze
vanno installate manualmente nell'ambiente Python (vedi
[Guida Riavvio Server](guida_server.md)).
