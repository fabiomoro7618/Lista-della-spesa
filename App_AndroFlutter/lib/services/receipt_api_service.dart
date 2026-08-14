import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:http/http.dart' as http;

import '../config.dart';
import '../models/process_result.dart';

class ApiException implements Exception {
  final String message;
  ApiException(this.message);

  @override
  String toString() => message;
}

/// Client per le API FastAPI esposte da App_Python/main.py.
class ReceiptApiService {
  final String baseUrl;

  ReceiptApiService({this.baseUrl = ApiConfig.baseUrl});

  /// POST /process/ — invia lo ZIP di backup + la foto dello scontrino,
  /// riceve il riepilogo dell'elaborazione (vedi ProcessResult).
  Future<ProcessResult> processReceipt({
    required File zipFile,
    required File imageFile,
  }) async {
    final uri = Uri.parse('$baseUrl/process/');
    final request = http.MultipartRequest('POST', uri)
      ..files.add(await http.MultipartFile.fromPath('zip_file', zipFile.path))
      ..files
          .add(await http.MultipartFile.fromPath('image_file', imageFile.path));

    final http.StreamedResponse streamed;
    try {
      streamed = await request.send();
    } on SocketException {
      throw ApiException(
        'Impossibile raggiungere il server (${ApiConfig.baseUrl}). '
        'Verifica che l\'API sia avviata e l\'indirizzo sia corretto.',
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
