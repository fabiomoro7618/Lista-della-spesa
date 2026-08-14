import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../models/process_result.dart';
import '../services/receipt_api_service.dart';

class HomePage extends StatefulWidget {
  const HomePage({super.key});

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  final _api = ReceiptApiService();

  File? _zipFile;
  File? _imageFile;
  bool _loading = false;
  String? _error;
  ProcessResult? _result;

  String get _zipLabel =>
      _zipFile == null ? 'Nessun file selezionato' : _zipFile!.path.split('/').last;

  String get _imageLabel =>
      _imageFile == null ? 'Nessuna foto selezionata' : _imageFile!.path.split('/').last;

  Future<void> _pickZip() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: ['zip'],
    );
    if (result == null || result.files.single.path == null) return;
    setState(() => _zipFile = File(result.files.single.path!));
  }

  Future<void> _pickImage() async {
    final source = await showModalBottomSheet<ImageSource>(
      context: context,
      builder: (context) => SafeArea(
        child: Wrap(
          children: [
            ListTile(
              leading: const Icon(Icons.photo_camera),
              title: const Text('Scatta foto'),
              onTap: () => Navigator.pop(context, ImageSource.camera),
            ),
            ListTile(
              leading: const Icon(Icons.photo_library),
              title: const Text('Scegli dalla galleria'),
              onTap: () => Navigator.pop(context, ImageSource.gallery),
            ),
          ],
        ),
      ),
    );
    if (source == null) return;

    final picked = await ImagePicker().pickImage(source: source, imageQuality: 90);
    if (picked == null) return;
    setState(() => _imageFile = File(picked.path));
  }

  Future<void> _submit() async {
    if (_zipFile == null || _imageFile == null) {
      setState(() => _error = 'Seleziona sia lo ZIP di backup sia la foto dello scontrino.');
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final result = await _api.processReceipt(zipFile: _zipFile!, imageFile: _imageFile!);
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
      final savedPath = await FilePicker.platform.saveFile(
        dialogTitle: 'Salva il backup aggiornato',
        fileName: 'Backup_Aggiornato.zip',
        bytes: bytes,
      );

      if (!mounted) return;
      if (savedPath != null) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Backup salvato con successo.')),
        );
      }
    } catch (e) {
      setState(() => _error = 'Download fallito: $e');
    } finally {
      setState(() => _loading = false);
    }
  }

  void _restart() {
    setState(() {
      _zipFile = null;
      _imageFile = null;
      _result = null;
      _error = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Aggiorna Spesa da Scontrino')),
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
                children: result.updated
                    .map((item) => _ItemRow(
                          name: item.name,
                          trailing: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Text(
                                _money(item.oldPrice),
                                style: const TextStyle(
                                  color: Color(0xFF94A3B8),
                                  decoration: TextDecoration.lineThrough,
                                ),
                              ),
                              const SizedBox(width: 6),
                              Text(
                                _money(item.newPrice),
                                style: const TextStyle(
                                  color: Color(0xFF2563EB),
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ],
                          ),
                        ))
                    .toList(),
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
                            _money(item.price),
                            style: const TextStyle(
                              color: Color(0xFF16A34A),
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ))
                    .toList(),
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

  String _money(double v) => '${v.toStringAsFixed(2).replaceAll('.', ',')} €';
}

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
