import 'dart:async';
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

  // Senza un timeout esplicito, una richiesta che non riceve mai risposta
  // (backend addormentato su Render free tier, problema di rete, CORS
  // bloccato silenziosamente dal browser) lascia l'app in caricamento
  // infinito: il Future di http non si completa mai, ne' con successo ne'
  // con un errore da mostrare all'utente.
  static const Duration _timeout = Duration(seconds: 60);

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
      streamed = await request.send().timeout(_timeout);
    } on TimeoutException {
      throw ApiException(_timeoutMessage);
    } on http.ClientException catch (e) {
      throw ApiException(_connectionErrorMessage(e));
    }

    final http.Response response;
    try {
      response = await http.Response.fromStream(streamed).timeout(_timeout);
    } on TimeoutException {
      throw ApiException(_timeoutMessage);
    }

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

    final http.Response response;
    try {
      response = await http.get(uri).timeout(_timeout);
    } on TimeoutException {
      throw ApiException(_timeoutMessage);
    } on http.ClientException catch (e) {
      throw ApiException(_connectionErrorMessage(e));
    }

    if (response.statusCode != 200) {
      throw ApiException(_extractErrorDetail(response));
    }

    return response.bodyBytes;
  }

  String get _timeoutMessage =>
      'Il server non ha risposto entro ${_timeout.inSeconds} secondi. '
      'Se il backend è ospitato su un piano gratuito (es. Render), potrebbe '
      'essersi "addormentato" per inattività: riprova tra qualche istante.';

  String _connectionErrorMessage(http.ClientException e) =>
      'Impossibile raggiungere il server ($baseUrl). '
      'Verifica la connessione a internet, che il backend sia attivo e che '
      'le impostazioni CORS del server permettano questa origine. ($e)';

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
