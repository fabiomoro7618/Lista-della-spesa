import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../models/process_result.dart';
import '../services/receipt_api_service.dart';
import '../services/web_downloader.dart';
import '../version.dart';

class HomePage extends StatefulWidget {
  const HomePage({super.key});

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  final _api = ReceiptApiService();

  // Solo `name`/`bytes` (mai `path`): su Flutter Web non esiste un vero
  // filesystem, quindi i file vengono tenuti sempre come byte in memoria.
  // Anche la foto viene selezionata con file_picker (withData: true) invece
  // di image_picker: XFile.readAsBytes() su Web legge da un URL blob: che il
  // browser puo' revocare nel frattempo, causando "Could not load Blob from
  // its URL. Has it been revoked?" all'invio. file_picker restituisce invece
  // i byte gia' pronti in PlatformFile.bytes, senza mai passare da un URL.
  PlatformFile? _zipPickedFile;
  Uint8List? _imageBytes;
  String? _imageName;
  String? _imageMimeType;
  bool _loading = false;
  String? _error;
  ProcessResult? _result;

  String get _zipLabel =>
      _zipPickedFile == null ? 'Nessun file selezionato' : _zipPickedFile!.name;

  String get _imageLabel => _imageName ?? 'Nessuna foto selezionata';

  Future<void> _pickZip() async {
    try {
      // file_picker non popola mai `path` su Web (solo `bytes`): la
      // presenza del file viene comunque verificata sotto tramite
      // `name`/`bytes`, non tramite `path`.
      final result = await FilePicker.platform.pickFiles(
        type: FileType.custom,
        allowedExtensions: ['zip'],
        withData: true,
        allowMultiple: false,
      );
      if (result == null || result.files.isEmpty) return;
      final picked = result.files.single;

      if (picked.name.isEmpty || picked.bytes == null) {
        setState(() => _error = 'Impossibile leggere il file selezionato.');
        return;
      }
      if (!picked.name.toLowerCase().endsWith('.zip')) {
        setState(() => _error = 'Seleziona un file con estensione .zip.');
        return;
      }

      setState(() {
        _error = null;
        _zipPickedFile = picked;
      });
    } catch (e, stackTrace) {
      debugPrint('Errore durante la selezione del file ZIP: $e\n$stackTrace');
      setState(() => _error = 'Errore durante la selezione del file ZIP: $e');
    }
  }

  Future<void> _pickImage() async {
    try {
      final result = await FilePicker.platform.pickFiles(
        type: FileType.image,
        withData: true,
        allowMultiple: false,
      );
      if (result == null || result.files.isEmpty) return;
      final picked = result.files.first;

      if (picked.name.isEmpty || picked.bytes == null) {
        setState(() => _error = 'Impossibile leggere la foto selezionata.');
        return;
      }

      setState(() {
        _error = null;
        _imageBytes = picked.bytes;
        _imageName = picked.name;
        _imageMimeType = _mimeTypeFromName(picked.name);
      });
    } catch (e, stackTrace) {
      debugPrint('Errore durante la selezione della foto: $e\n$stackTrace');
      setState(() => _error = 'Errore durante la selezione della foto: $e');
    }
  }

  static String? _mimeTypeFromName(String name) {
    final ext = name.toLowerCase().split('.').last;
    switch (ext) {
      case 'jpg':
      case 'jpeg':
        return 'image/jpeg';
      case 'png':
        return 'image/png';
      case 'gif':
        return 'image/gif';
      case 'webp':
        return 'image/webp';
      default:
        return null;
    }
  }

  Future<void> _submit() async {
    final zipPicked = _zipPickedFile;
    final imageBytes = _imageBytes;
    if (zipPicked == null || zipPicked.bytes == null || imageBytes == null) {
      setState(() => _error = 'Seleziona sia lo ZIP di backup sia la foto dello scontrino.');
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final result = await _api.processReceipt(
        zipBytes: zipPicked.bytes!,
        zipFilename: zipPicked.name,
        imageBytes: imageBytes,
        imageFilename: _imageName!,
        imageContentType: _imageMimeType,
      );
      setState(() => _result = result);
    } catch (e) {
      setState(() => _error = 'Elaborazione fallita: $e');
    } finally {
      setState(() => _loading = false);
    }
  }

  Future<void> _downloadAndSave() async {
    final result = _result;
    if (result == null) return;

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final bytes = await _api.downloadBackup(result.downloadUrl);
      // Download diretto via Blob (dart:html), MAI tramite FilePicker: su
      // Flutter Web FilePicker.platform.saveFile() non e' implementato e
      // solleva UnimplementedError.
      saveBytesAsFile(bytes, 'ShoppingList_BACKUP_aggiornato.zip');

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Backup scaricato con successo.')),
      );
    } catch (e) {
      setState(() => _error = 'Download fallito: $e');
    } finally {
      setState(() => _loading = false);
    }
  }

  void _restart() {
    setState(() {
      _zipPickedFile = null;
      _imageBytes = null;
      _imageName = null;
      _imageMimeType = null;
      _result = null;
      _error = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Aggiorna Spesa da Scontrino $appVersion')),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 520),
              child: _result == null ? _buildForm(context) : _buildSummary(context, _result!),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildForm(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('1. Carica il file ZIP di Backup (.zip)',
                style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 6),
            OutlinedButton.icon(
              onPressed: _loading ? null : _pickZip,
              icon: const Icon(Icons.folder_zip_outlined),
              label: Text(_zipLabel, overflow: TextOverflow.ellipsis),
            ),
            const SizedBox(height: 18),
            Text('2. Carica la foto dello Scontrino',
                style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 6),
            OutlinedButton.icon(
              onPressed: _loading ? null : _pickImage,
              icon: const Icon(Icons.receipt_long_outlined),
              label: Text(_imageLabel, overflow: TextOverflow.ellipsis),
            ),
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _loading ? null : _submit,
              child: _loading
                  ? const SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('Elabora Scontrino'),
            ),
            if (_loading) ...[
              const SizedBox(height: 14),
              Text(
                'Analisi dello scontrino in corso, può richiedere qualche minuto...',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 18),
              _ErrorBanner(message: _error!),
            ],
          ],
        ),
      ),
    );
  }

  Widget _buildSummary(BuildContext context, ProcessResult result) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Riepilogo elaborazione', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 16),
            Row(
              children: [
                _StatTile(
                  label: 'Aggiornati',
                  value: result.updatedCount,
                  color: const Color(0xFF2563EB),
                ),
                const SizedBox(width: 10),
                _StatTile(
                  label: 'Aggiunti',
                  value: result.insertedCount,
                  color: const Color(0xFF16A34A),
                ),
                const SizedBox(width: 10),
                _StatTile(
                  label: 'Invariati',
                  value: result.unchangedCount,
                  color: const Color(0xFF64748B),
                ),
              ],
            ),
            const SizedBox(height: 20),
            Text('Prezzi aggiornati', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 8),
            if (result.updated.isEmpty)
              const _EmptyNote('Nessun prezzo aggiornato: quelli in archivio erano già più bassi.')
            else
              _ItemList(
                children:
                    result.updated.map((item) => _UpdatedItemRow(item: item)).toList(),
              ),
            const SizedBox(height: 18),
            Text('Nuovi prodotti aggiunti', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 8),
            if (result.inserted.isEmpty)
              const _EmptyNote('Nessun nuovo prodotto: erano tutti già in archivio.')
            else
              _ItemList(
                children: result.inserted
                    .map((item) => _ItemRow(
                          name: item.name,
                          trailing: Text(
                            _formatMoney(item.price),
                            style: const TextStyle(
                              color: Color(0xFF16A34A),
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ))
                    .toList(),
              ),
            const SizedBox(height: 18),
            if (result.unchanged.isNotEmpty)
              Theme(
                data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
                child: ExpansionTile(
                  tilePadding: EdgeInsets.zero,
                  childrenPadding: EdgeInsets.zero,
                  title: Text(
                    'Prodotti invariati (${result.unchanged.length})',
                    style: Theme.of(context).textTheme.labelLarge,
                  ),
                  children: [
                    _ItemList(
                      children: result.unchanged
                          .map((item) => _UnchangedItemRow(item: item))
                          .toList(),
                    ),
                  ],
                ),
              ),
            const SizedBox(height: 24),
            FilledButton.icon(
              style: FilledButton.styleFrom(backgroundColor: const Color(0xFF16A34A)),
              onPressed: _loading ? null : _downloadAndSave,
              icon: const Icon(Icons.download),
              label: const Text('Scarica il Backup aggiornato'),
            ),
            const SizedBox(height: 10),
            OutlinedButton(
              onPressed: _loading ? null : _restart,
              child: const Text('Elabora un altro scontrino'),
            ),
            if (_error != null) ...[
              const SizedBox(height: 18),
              _ErrorBanner(message: _error!),
            ],
          ],
        ),
      ),
    );
  }

}

String _formatMoney(double v) => '${v.toStringAsFixed(2).replaceAll('.', ',')} €';

class _StatTile extends StatelessWidget {
  final String label;
  final int value;
  final Color color;

  const _StatTile({required this.label, required this.value, required this.color});

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 6),
        decoration: BoxDecoration(
          color: const Color(0xFFF8FAFC),
          border: Border.all(color: const Color(0xFFE2E8F0)),
          borderRadius: BorderRadius.circular(8),
        ),
        child: Column(
          children: [
            Text(
              '$value',
              style: TextStyle(fontSize: 22, fontWeight: FontWeight.w700, color: color),
            ),
            const SizedBox(height: 2),
            Text(
              label.toUpperCase(),
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 10, color: Color(0xFF64748B), letterSpacing: 0.4),
            ),
          ],
        ),
      ),
    );
  }
}

class _ItemList extends StatelessWidget {
  final List<Widget> children;

  const _ItemList({required this.children});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        border: Border.all(color: const Color(0xFFE2E8F0)),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(children: children),
    );
  }
}

class _ItemRow extends StatelessWidget {
  final String name;
  final Widget trailing;

  const _ItemRow({required this.name, required this.trailing});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Color(0xFFF1F5F9))),
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(name, style: const TextStyle(color: Color(0xFF1E293B))),
          ),
          const SizedBox(width: 12),
          trailing,
        ],
      ),
    );
  }
}

class _UpdatedItemRow extends StatelessWidget {
  final UpdatedItem item;

  const _UpdatedItemRow({required this.item});

  @override
  Widget build(BuildContext context) {
    // Il backend aggiorna solo quando old_price == 0 (prezzo mai registrato
    // prima) oppure quando il nuovo prezzo e' piu' basso: nel primo caso non
    // esiste un vero risparmio da mostrare, solo un prezzo aggiunto.
    final hasOldPrice = item.oldPrice > 0;
    final savings = item.oldPrice - item.newPrice;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Color(0xFFF1F5F9))),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Text(item.name, style: const TextStyle(color: Color(0xFF1E293B))),
          ),
          const SizedBox(width: 12),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (hasOldPrice) ...[
                    Text(
                      _formatMoney(item.oldPrice),
                      style: const TextStyle(
                        color: Color(0xFF94A3B8),
                        decoration: TextDecoration.lineThrough,
                        fontSize: 13,
                      ),
                    ),
                    const SizedBox(width: 6),
                  ],
                  Text(
                    _formatMoney(item.newPrice),
                    style: const TextStyle(
                      color: Color(0xFF2563EB),
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 2),
              if (hasOldPrice && savings > 0)
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                  decoration: BoxDecoration(
                    color: const Color(0xFFDCFCE7),
                    borderRadius: BorderRadius.circular(4),
                  ),
                  child: Text(
                    '- ${_formatMoney(savings)}',
                    style: const TextStyle(
                      color: Color(0xFF16A34A),
                      fontSize: 11,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                )
              else if (!hasOldPrice)
                const Text(
                  'Prezzo aggiunto',
                  style: TextStyle(
                    color: Color(0xFF94A3B8),
                    fontSize: 11,
                    fontStyle: FontStyle.italic,
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _UnchangedItemRow extends StatelessWidget {
  final UnchangedItem item;

  const _UnchangedItemRow({required this.item});

  @override
  Widget build(BuildContext context) {
    final priceDiffers = item.receiptPrice != item.currentPrice;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Color(0xFFF1F5F9))),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(item.name, style: const TextStyle(color: Color(0xFF1E293B))),
                if (priceDiffers) ...[
                  const SizedBox(height: 2),
                  Text(
                    'Prezzo sullo scontrino: ${_formatMoney(item.receiptPrice)}',
                    style: const TextStyle(color: Color(0xFF94A3B8), fontSize: 11),
                  ),
                ],
              ],
            ),
          ),
          const SizedBox(width: 12),
          Text(
            _formatMoney(item.currentPrice),
            style: const TextStyle(color: Color(0xFF64748B), fontWeight: FontWeight.w600),
          ),
        ],
      ),
    );
  }
}

class _EmptyNote extends StatelessWidget {
  final String text;

  const _EmptyNote(this.text);

  @override
  Widget build(BuildContext context) {
    return Text(
      text,
      style: const TextStyle(color: Color(0xFF94A3B8), fontStyle: FontStyle.italic, fontSize: 13),
    );
  }
}

class _ErrorBanner extends StatelessWidget {
  final String message;

  const _ErrorBanner({required this.message});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: const Color(0xFFFEF2F2),
        border: Border.all(color: const Color(0xFFFECACA)),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(message, style: const TextStyle(color: Color(0xFF991B1B))),
    );
  }
}
