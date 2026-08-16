# Flutter Web UI

La UI vive in [`App_AndroFlutter/lib/`](https://github.com/fabiomoro7618/Lista-della-spesa/tree/main/App_AndroFlutter/lib)
ed è pensata per funzionare **prima di tutto su Web** (servita da un
Codespace), pur mantenendo la struttura multipiattaforma generata da
Flutter (Android/iOS/desktop restano scaffolding non attivamente
mantenuto per il momento).

## Schermata principale (`home_page.dart`)

`HomePage` è uno `StatefulWidget` con due fasi:

1. **Form di selezione** — due pulsanti per scegliere lo ZIP di backup e la
   foto dello scontrino, più il pulsante "Elabora Scontrino".
2. **Riepilogo risultati** — mostrato al posto del form una volta ricevuta
   la risposta da `POST /process/`:
      - tre schede (**Aggiornati**, **Aggiunti**, **Invariati**) con i
        conteggi;
      - lista **Prezzi aggiornati**: nome, vecchio prezzo barrato, nuovo
        prezzo, e un badge verde col **risparmio** (`- 0,30 €`) — oppure
        l'etichetta *"Prezzo aggiunto"* quando il prodotto non aveva ancora
        un prezzo registrato (caso in cui non esiste un vero risparmio da
        mostrare);
      - lista **Nuovi prodotti aggiunti**: nome + prezzo;
      - sezione **Prodotti invariati**, in un `ExpansionTile` chiuso di
        default (per non affollare la schermata), con il prezzo dello
        scontrino mostrato in piccolo quando differisce da quello in
        archivio;
      - pulsante **"Scarica il Backup aggiornato"**.

## Selezione dei file: solo byte, mai `path` o URL `blob:`

Sia lo ZIP che la foto vengono selezionati con
[`file_picker`](https://pub.dev/packages/file_picker) e letti **subito** in
memoria (`withData: true` → `PlatformFile.bytes`), non lette pigramente al
momento dell'invio.

!!! danger "Perché non `image_picker` / `XFile.readAsBytes()`"
    La UI usava inizialmente `image_picker` per la foto: `XFile` espone i
    byte solo tramite `readAsBytes()`, che su Flutter Web legge da un URL
    `blob:` creato al momento della selezione. Se quell'URL viene revocato
    nel frattempo (refresh, garbage collection del browser), la chiamata
    fallisce con `Could not load Blob from its URL. Has it been revoked?`.
    La soluzione è stata sostituire `image_picker` con `file_picker` anche
    per la foto: i byte arrivano subito in `PlatformFile.bytes`, senza mai
    passare da un URL che possa scadere.

Questo vale anche per l'invio: `ReceiptApiService.processReceipt` accetta
sempre `Uint8List`, mai un percorso di file — necessario perché su Flutter
Web non esiste un vero filesystem (`File.path` solleva eccezioni).

## Risoluzione dell'URL del backend (`config.dart`)

`ApiConfig.baseUrl` calcola l'indirizzo dell'API a runtime, con questa
priorità:

1. `--dart-define=API_BASE_URL=...` (override esplicito);
2. su Web: **origine della pagina corrente**, con la porta sostituita. Su
   servizi come Codespaces la porta è codificata nel sottodominio
   (`nome-codespace-8080.app.github.dev`) e non come `:porta` esplicita,
   quindi la logica cerca `-8080` nell'host e lo sostituisce con `-8000`;
   in caso standard (es. `http://localhost:8080`) usa invece la porta
   esplicita nell'URL;
3. altrimenti: `http://10.0.2.2:8000` (indirizzo con cui l'emulatore
   Android raggiunge il `localhost` del computer host).

## Download del backup: Blob (`dart:html`), non API native

Il pulsante "Scarica il Backup aggiornato" chiama
`saveBytesAsFile(bytes, 'ShoppingList_BACKUP_aggiornato.zip')`, definita in
[`lib/services/web_downloader_web.dart`](https://github.com/fabiomoro7618/Lista-della-spesa/blob/main/App_AndroFlutter/lib/services/web_downloader_web.dart):

```dart
void saveBytesAsFile(Uint8List bytes, String filename) {
  final blob = html.Blob([bytes], 'application/zip');
  final url = html.Url.createObjectUrlFromBlob(blob);
  html.AnchorElement(href: url)
    ..setAttribute('download', filename)
    ..click();
  html.Url.revokeObjectUrl(url);
}
```

!!! danger "Perché non `FilePicker.platform.saveFile()`"
    La prima implementazione usava `FilePicker.platform.saveFile(bytes: ...)`,
    che su desktop/mobile apre un vero dialogo di salvataggio. Su questa
    configurazione di Flutter Web, però, quel metodo **non è implementato**
    e solleva `UnimplementedError: saveFile() has not been implemented.` —
    per questo il download passa direttamente da un `Blob` DOM, senza
    coinvolgere `file_picker` in nessun modo.

### Import condizionale (`web_downloader.dart`)

`dart:html` **non esiste** fuori dal Web: importarlo senza precauzioni
romperebbe la compilazione per Android/iOS/desktop. La libreria è quindi
divisa in tre file con **import condizionale**:

```dart title="lib/services/web_downloader.dart"
export 'web_downloader_stub.dart' if (dart.library.html) 'web_downloader_web.dart';
```

| File | Ruolo |
|---|---|
| `web_downloader.dart` | Punto di importazione unico usato da `home_page.dart` |
| `web_downloader_web.dart` | Implementazione reale con `dart:html` (Blob + `<a download>`) |
| `web_downloader_stub.dart` | Stub per le altre piattaforme: lancia `UnsupportedError` |

## Versioning e cache busting

Flutter Web genera un `main.dart.js` il cui contenuto può restare **in
cache nel browser** (service worker) anche dopo un nuovo
`flutter build web --release`, mascherando il fatto che la build servita
non è quella più recente. Per riconoscere a colpo d'occhio se il browser
sta mostrando l'ultima build, il numero di versione è visibile nel titolo
dell'AppBar:

```dart title="lib/version.dart"
const String appVersion = 'v1.0.1';
```

```dart title="lib/screens/home_page.dart"
appBar: AppBar(title: const Text('Aggiorna Spesa da Scontrino $appVersion')),
```

`appVersion` va incrementata **manualmente** ad ogni rilascio, insieme al
campo `version:` in `App_AndroFlutter/pubspec.yaml` (stessa cifra, per
coerenza). Se dopo una nuova build la versione mostrata in pagina non
cambia, il browser sta ancora servendo la vecchia build: fai un **hard
refresh** (`Ctrl+Shift+R`) o apri la pagina in incognito.
