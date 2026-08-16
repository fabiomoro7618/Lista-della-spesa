import 'dart:html' as html;
import 'dart:typed_data';

/// Scarica i byte come file nel browser tramite un Blob + link <a download>
/// del DOM. Necessario su Flutter Web perché FilePicker.platform.saveFile()
/// non e' implementato per questa piattaforma (UnimplementedError).
void saveBytesAsFile(Uint8List bytes, String filename) {
  final blob = html.Blob([bytes], 'application/zip');
  final url = html.Url.createObjectUrlFromBlob(blob);
  html.AnchorElement(href: url)
    ..setAttribute('download', filename)
    ..click();
  html.Url.revokeObjectUrl(url);
}
