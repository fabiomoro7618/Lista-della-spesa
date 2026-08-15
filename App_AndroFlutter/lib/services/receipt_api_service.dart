import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

import '../config.dart';
import '../models/process_result.dart';

class ApiException implements Exception {
  final String message;
  ApiException(this.message);

  @override
  String toString() => message;
}

/// Client per le API FastAPI esposte da App_Python/main.py.
///
/// I file vengono inviati sempre come byte in memoria (mai tramite path su
/// disco), per funzionare in modo identico su mobile, desktop e Web — su
/// Web non esiste un vero filesystem e `File.path`/`openRead()` sollevano
/// eccezioni.
class ReceiptApiService {
  final String baseUrl;

  // ApiConfig.baseUrl dipende dall'origine della pagina su Web, quindi non è
  // più una costante di compilazione: il default va calcolato a runtime.
  ReceiptApiService({String? baseUrl}) : baseUrl = baseUrl ?? ApiConfig.baseUrl;

  /// POST /process/ — invia lo ZIP di backup + la foto dello scontrino,
  /// riceve il riepilogo dell'elaborazione (vedi ProcessResult).
  Future<ProcessResult> processReceipt({
    required Uint8List zipBytes,
    required String zipFilename,
    required Uint8List imageBytes,
    required String imageFilename,
    String? imageContentType,
  }) async {
    final uri = Uri.parse('$baseUrl/process/');
    final request = http.MultipartRequest('POST', uri)
      ..files.add(http.MultipartFile.fromBytes(
        'zip_file',
        zipBytes,
        filename: zipFilename,
      ))
      ..files.add(http.MultipartFile.fromBytes(
        'image_file',
        imageBytes,
        filename: imageFilename,
        contentType:
            imageContentType != null ? MediaType.parse(imageContentType) : null,
      ));

    final http.StreamedResponse streamed;
    try {
      streamed = await request.send();
    } on http.ClientException catch (e) {
      throw ApiException(
        'Impossibile raggiungere il server ($baseUrl). '
        'Verifica che l\'API sia avviata e l\'indirizzo sia corretto. ($e)',
      );
    }
    final response = await http.Response.fromStream(streamed);

    if (response.statusCode != 200) {
      throw ApiException(_extractErrorDetail(response));
    }

    return ProcessResult.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  /// GET {downloadUrl} — scarica lo ZIP di backup aggiornato.
  Future<Uint8List> downloadBackup(String downloadUrl) async {
    final uri = Uri.parse('$baseUrl$downloadUrl');
    final response = await http.get(uri);

    if (response.statusCode != 200) {
      throw ApiException(_extractErrorDetail(response));
    }

    return response.bodyBytes;
  }

  String _extractErrorDetail(http.Response response) {
    try {
      final body = jsonDecode(response.body);
      if (body is Map && body['detail'] != null) {
        return body['detail'].toString();
      }
    } catch (_) {
      // Corpo non JSON: resta il codice di stato.
    }
    return 'Errore ${response.statusCode}';
  }
}
