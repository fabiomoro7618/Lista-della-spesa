// Import condizionale: su Web usa dart:html (Blob + <a download>), sulle
// altre piattaforme espone uno stub che lancia UnsupportedError (dart:html
// non e' disponibile fuori dal Web e romperebbe la compilazione).
export 'web_downloader_stub.dart' if (dart.library.html) 'web_downloader_web.dart';
