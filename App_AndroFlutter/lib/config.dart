import 'package:flutter/foundation.dart' show kIsWeb;

/// Indirizzo del backend FastAPI (App_Python).
///
/// Priorità di risoluzione:
/// 1. `--dart-define=API_BASE_URL=...` (sovrascrittura esplicita, qualsiasi
///    piattaforma — utile in sviluppo locale, es. contro un backend avviato
///    in un Codespace o su localhost):
///      flutter run --dart-define=API_BASE_URL=http://localhost:8000
/// 2. Su Web, se non c'è una sovrascrittura: il backend di produzione
///    pubblicato su Render.
/// 3. Altrimenti: 10.0.2.2, l'indirizzo con cui l'emulatore Android
///    raggiunge il "localhost" del computer host.
class ApiConfig {
  static const String _override = String.fromEnvironment('API_BASE_URL');

  /// Backend FastAPI pubblicato su Render (vedi render.yaml nella radice
  /// del repository).
  static const String _renderBackendUrl =
      'https://lista-della-spesa-qrof.onrender.com';

  static String get baseUrl {
    if (_override.isNotEmpty) return _override;
    if (kIsWeb) return _renderBackendUrl;
    return 'http://10.0.2.2:8000';
  }
}
