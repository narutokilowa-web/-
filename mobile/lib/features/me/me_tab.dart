import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../app/app_controller.dart';
import '../../core/secure_store.dart';
import '../../l10n/app_localizations.dart';

class MeTab extends StatelessWidget {
  const MeTab({super.key, required this.controller, required this.onToggleLocale});

  final AppController controller;
  final VoidCallback onToggleLocale;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final pending = controller.syncStatus?.pending ?? controller.outbox?.pendingCount ?? 0;
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text(controller.userName ?? '—', key: const Key('me-name'), style: Theme.of(context).textTheme.headlineSmall),
        Text(l10n.rolesLabel(controller.roles.join('، '))),
        FutureBuilder<String?>(
          future: controller.store.read(SecureKeys.deviceId),
          builder: (context, snap) => Text(l10n.deviceIdLabel(snap.data ?? '—'), style: Theme.of(context).textTheme.bodySmall),
        ),
        Text(l10n.environmentLabel(controller.config.environment), style: Theme.of(context).textTheme.bodySmall),
        const SizedBox(height: 16),
        Text(pending == 0 ? l10n.outboxEmpty : l10n.waitingToSend(pending), key: const Key('outbox-count')),
        if ((controller.outbox?.heldCount ?? 0) > 0) Text(l10n.heldCount(controller.outbox!.heldCount), key: const Key('held-count')),
        Text(
          controller.grants?.current == null
              ? l10n.grantNone
              : l10n.grantValidUntil(DateFormat('yyyy-MM-dd HH:mm').format(controller.grants!.current!.expiresAt.toLocal())),
          key: const Key('grant-until'),
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 8),
        FilledButton(key: const Key('sync-now'), onPressed: controller.busy ? null : controller.syncNow, child: Text(l10n.syncNow)),
        const SizedBox(height: 8),
        OutlinedButton(key: const Key('toggle-language'), onPressed: onToggleLocale, child: Text(l10n.languageToggle)),
        const SizedBox(height: 8),
        OutlinedButton(key: const Key('sign-out'), onPressed: controller.signOut, child: Text(l10n.signOut)),
      ],
    );
  }
}
