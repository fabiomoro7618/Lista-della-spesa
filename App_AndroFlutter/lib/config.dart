import 'package:flutter/foundation.dart' show kIsWeb;

/// Indirizzo del backend FastAPI (App_Python).
///
/// Priorità di risoluzione:
/// 1. `--dart-define=API_BASE_URL=...` (sovrascrittura esplicita, qualsiasi
///    piattaforma):
///      flutter run --dart-define=API_BASE_URL=http://192.168.1.50:8000
/// 2. Su Web, se non c'è una sovrascrittura: origine della pagina corrente
///    con la porta sostituita da 8000 (es. l'app servita su
///    "https://xxx-8080.app.github.dev" chiamerà l'API su
///    "https://xxx-8000.app.github.dev" — tipico di Codespaces/Gitpod dove
///    la porta è codificata nel sottodominio, non come ":porta" esplicita).
/// 3. Altrimenti: 10.0.2.2, l'indirizzo con cui l'emulatore Android
///    raggiunge il "localhost" del computer host.
class ApiConfig {
  static const String _override = String.fromEnvironment('API_BASE_URL');

  static String get baseUrl {
    if (_override.isNotEmpty) return _override;
    if (kIsWeb) return _webBaseUrlFromPageOrigin();
    return 'http://10.0.2.2:8000';
  }

  static String _webBaseUrlFromPageOrigin() {
    final page = Uri.base;

    // Servizi come GitHub Codespaces espongono ogni porta come sottodominio
    // dedicato (es. "nome-codespace-8080.app.github.dev") invece che come
    // porta esplicita nell'URL: cerchiamo "-8080" nell'host e lo sostituiamo
    // con "-8000".
    final hostPortMatch = RegExp(r'^(.*-)8080(\..+)$').firstMatch(page.host);
    if (hostPortMatch != null) {
      final apiHost = '${hostPortMatch.group(1)}8000${hostPortMatch.group(2)}';
      return Uri(scheme: page.scheme, host: apiHost).toString();
    }

    // Caso standard: porta esplicita nell'URL (es. http://localhost:8080).
    return Uri(scheme: page.scheme, host: page.host, port: 8000).toString();
  }
}
