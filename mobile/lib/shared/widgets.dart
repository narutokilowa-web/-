import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../core/sync_engine.dart';
import '../l10n/app_localizations.dart';

/// Screen body with one primary action anchored at the bottom (docs/lean-pilot/08 §8.1).
class ActionScaffold extends StatelessWidget {
  const ActionScaffold({super.key, required this.title, required this.children, this.primary, this.secondary});

  final String title;
  final List<Widget> children;
  final Widget? primary;
  final Widget? secondary;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(title)),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(child: ListView(children: children)),
              if (secondary != null) ...[secondary!, const SizedBox(height: 8)],
              ?primary,
            ],
          ),
        ),
      ),
    );
  }
}

String formatTime(BuildContext context, DateTime time) =>
    DateFormat('yyyy-MM-dd HH:mm', Localizations.localeOf(context).languageCode).format(time.toLocal());

/// Persistent status strip: online/offline, commands waiting to send, last sync (docs/lean-pilot/08 §8.1).
class SyncStrip extends StatelessWidget {
  const SyncStrip({super.key, required this.status});

  final SyncStatus? status;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final s = status;
    final online = s?.online ?? true;
    return Container(
      key: const Key('sync-strip'),
      width: double.infinity,
      color: online ? const Color(0xFFE3F3E6) : const Color(0xFFFFF4D6),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Text(
        [
          online ? l10n.online : l10n.offline,
          if ((s?.pending ?? 0) > 0) l10n.waitingToSend(s!.pending),
          if (s?.lastSyncAt != null) l10n.lastSync(formatTime(context, s!.lastSyncAt!)) else l10n.neverSynced,
        ].join(' · '),
      ),
    );
  }
}

Color statusColor(String? status) => switch (status) {
  'OPERATIONAL' => const Color(0xFF1B5E20),
  'FAULTY' => const Color(0xFFB42318),
  'UNDER_MAINTENANCE' => const Color(0xFF7A5300),
  _ => const Color(0xFF5B6B63),
};
