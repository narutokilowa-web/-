import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../app/app_controller.dart';
import '../../core/api_client.dart';
import '../../core/models.dart';
import '../../core/tag_token.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';
import '../tasks/asset_summary_card.dart';
import '../tasks/tasks_screens.dart';

/// Scan tab: live camera QR reader plus manual code entry for damaged tags (docs/lean-pilot/08 §8.3).
/// In-app camera only; nothing is saved to the gallery.
class ScanTab extends StatefulWidget {
  const ScanTab({super.key, required this.controller, this.showCamera = true});

  final AppController controller;

  /// False in widget tests (no camera plugin on the test host).
  final bool showCamera;

  @override
  State<ScanTab> createState() => _ScanTabState();
}

class _ScanTabState extends State<ScanTab> {
  final _code = TextEditingController();
  final _scanner = MobileScannerController(formats: const [BarcodeFormat.qrCode], detectionSpeed: DetectionSpeed.noDuplicates);
  String? _error;
  bool _resolving = false;

  @override
  void dispose() {
    _scanner.dispose();
    super.dispose();
  }

  Future<void> _resolve(String raw) async {
    final l10n = AppLocalizations.of(context);
    if (_resolving) return;
    if (tagTokenFrom(raw) == null) return setState(() => _error = l10n.invalidCode);
    setState(() {
      _resolving = true;
      _error = null;
    });
    try {
      final card = await widget.controller.resolveTag(raw);
      if (!mounted || card == null) return;
      await Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) => AssetCardScreen(controller: widget.controller, card: card),
        ),
      );
    } on OfflineException {
      if (mounted) setState(() => _error = l10n.offlineMessage);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.code == 'TAG_NOT_FOUND' ? l10n.tagNotFound : l10n.errorCode(e.code));
    } finally {
      if (mounted) setState(() => _resolving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        if (widget.showCamera)
          SizedBox(
            height: 280,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: MobileScanner(
                controller: _scanner,
                onDetect: (capture) {
                  final value = capture.barcodes.firstOrNull?.rawValue;
                  if (value != null) _resolve(value);
                },
                errorBuilder: (context, error) => Center(child: Text(l10n.cameraUnavailable)),
              ),
            ),
          ),
        const SizedBox(height: 8),
        Text(l10n.scanHint),
        const SizedBox(height: 16),
        TextField(
          key: const Key('manual-code'),
          controller: _code,
          textCapitalization: TextCapitalization.characters,
          decoration: InputDecoration(labelText: l10n.manualCodeLabel),
          onSubmitted: _resolve,
        ),
        const SizedBox(height: 8),
        FilledButton(
          key: const Key('manual-resolve'),
          onPressed: _resolving ? null : () => _resolve(_code.text),
          child: Text(l10n.resolve),
        ),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.only(top: 12),
            child: Text(
              _error!,
              key: const Key('scan-error'),
              style: const TextStyle(color: Color(0xFFB42318)),
            ),
          ),
      ],
    );
  }
}

class AssetCardScreen extends StatelessWidget {
  const AssetCardScreen({super.key, required this.controller, required this.card});

  final AppController controller;
  final AssetCard card;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: card.asset.assetCode ?? l10n.assetTypeStreetLight,
      children: [
        AssetSummaryCard(asset: card.asset),
        Text(l10n.openFaultsLabel(card.openFaults), key: const Key('open-faults')),
        for (final task in card.myOpenTasks)
          Card(
            color: const Color(0xFFE3F3E6),
            child: ListTile(
              key: Key('card-task-${task.code}'),
              title: Text(l10n.myTaskOnAsset),
              subtitle: Text('${task.code} · ${l10n.faultCategoryName(task.faultCategory)}'),
              trailing: TextButton(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) => TaskDetailScreen(controller: controller, cached: task),
                  ),
                ),
                child: Text(l10n.openTask),
              ),
            ),
          ),
        const SizedBox(height: 12),
        Text(l10n.recentEvents, style: Theme.of(context).textTheme.titleMedium),
        for (final e in card.recentEvents)
          Text('${formatTime(context, e.at)} · ${e.action} · ${e.actorName}', style: Theme.of(context).textTheme.bodySmall),
      ],
    );
  }
}
