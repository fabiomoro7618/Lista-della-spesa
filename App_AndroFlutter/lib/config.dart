/// Indirizzo del backend FastAPI (App_Python).
///
/// 10.0.2.2 è l'indirizzo con cui l'emulatore Android raggiunge il
/// "localhost" del computer host. Su un device fisico, o se l'API gira
/// altrove, sovrascrivi con:
///   flutter run --dart-define=API_BASE_URL=http://192.168.1.50:8000
class ApiConfig {
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:8000',
  );
}
