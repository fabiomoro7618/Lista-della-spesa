import 'dart:typed_data';

/// Implementazione di riserva per le piattaforme non-Web (Android, iOS,
/// desktop): qui il download va gestito con FilePicker.platform.saveFile(),
/// non con questa funzione (vedi web_downloader.dart).
void saveBytesAsFile(Uint8List bytes, String filename) {
  throw UnsupportedError('Download diretto via Blob disponibile solo su Flutter Web.');
}
