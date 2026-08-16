# Guida Riavvio Server (GitHub Codespaces)

Comandi rapidi per (ri)avviare backend e frontend in un Codespace e
renderli raggiungibili dal browser. Tutti i comandi assumono la radice del
repository come working directory, salvo indicazione diversa.

## 1. Rendi pubbliche le porte

Le porte `8000` (API) e `8080` (Flutter Web) devono essere `public`,
altrimenti il browser non riesce a raggiungerle da fuori il Codespace:

```bash
gh codespace ports visibility 8000:public 8080:public -c "$CODESPACE_NAME"
```

Verifica lo stato attuale con:

```bash
gh codespace ports -c "$CODESPACE_NAME"
```

## 2. Avvia il backend FastAPI (porta 8000)

```bash
cd App_Python
nohup uvicorn main:app --host 0.0.0.0 --port 8000 > /tmp/uvicorn.log 2>&1 &
disown
```

Requisiti:

- dipendenze Python installate (`fastapi`, `uvicorn`, `python-dotenv`,
  `anthropic`);
- `App_Python/.env` presente con una `ANTHROPIC_API_KEY` valida (vedi
  [Architettura & Backend](architettura.md#configurazione-env)).

Verifica che sia attivo:

```bash
curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:8000/docs
```

## 3. Compila e avvia il frontend Flutter Web (porta 8080)

Ad ogni modifica al codice Dart va **ricompilata** la build release prima
di riavviare (o semplicemente ri-servire, se il server statico resta
attivo) il server:

```bash
cd App_AndroFlutter
flutter pub get
flutter build web --release
```

```bash
cd /workspaces/Lista-della-spesa
nohup python3 -m http.server 8080 --directory App_AndroFlutter/build/web > /tmp/http_server.log 2>&1 &
disown
```

!!! tip "Il server statico non va riavviato ad ogni build"
    `python3 -m http.server` legge i file direttamente dal disco ad ogni
    richiesta: dopo una nuova `flutter build web --release` la build più
    recente è già servita, senza bisogno di riavviare il processo Python.
    Il riavvio serve solo se il processo non è più in esecuzione.

Verifica:

```bash
curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:8080
```

## 4. URL pubblici del Codespace

```bash
gh codespace ports -c "$CODESPACE_NAME" | grep -E "8000|8080"
```

Esempio di output:

```
8000  public  https://<nome-codespace>-8000.app.github.dev
8080  public  https://<nome-codespace>-8080.app.github.dev
```

L'app Flutter calcola da sola l'URL del backend a partire da quello del
frontend (vedi [`ApiConfig.baseUrl`](flutter_web.md#risoluzione-dellurl-del-backend-configdart)):
non serve configurarlo a mano finché entrambe le porte seguono la
convenzione `*-8080` / `*-8000`.

## 5. Diagnosi rapida

| Sintomo | Causa probabile | Verifica |
|---|---|---|
| `502` sulla porta 8080 | Server statico non in esecuzione | `ps aux \| grep "http.server 8080"` |
| `Failed to fetch` su `/process/` | Porta 8000 privata, backend non attivo, o errore 500 senza header CORS | `gh codespace ports`, `ps aux \| grep uvicorn`, log Uvicorn |
| `500 Internal Server Error` su `/process/` | Spesso `ANTHROPIC_API_KEY` mancante/non valida | `tail` del log Uvicorn (traceback con dettaglio dell'eccezione) |
| La pagina non riflette le ultime modifiche | Cache del service worker di Flutter Web | Controlla il numero di versione in AppBar, poi hard refresh (`Ctrl+Shift+R`) |

## 6. Riavvio completo (entrambi i server)

Sequenza usata in questo progetto quando serve un riavvio pulito di
entrambi i processi:

```bash
pkill -f "uvicorn main:app"
pkill -f "http.server 8080"

cd /workspaces/Lista-della-spesa
nohup python3 -m http.server 8080 --directory App_AndroFlutter/build/web > /tmp/http_server.log 2>&1 &
disown

cd App_Python
nohup uvicorn main:app --host 0.0.0.0 --port 8000 > /tmp/uvicorn.log 2>&1 &
disown

gh codespace ports visibility 8000:public 8080:public -c "$CODESPACE_NAME"
```
