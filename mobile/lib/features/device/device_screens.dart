import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../core/secure_store.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';

class WaitingApprovalScreen extends StatelessWidget {
  const WaitingApprovalScreen({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.waitingTitle,
      secondary: OutlinedButton(onPressed: controller.signOut, child: Text(l10n.signOut)),
      primary: FilledButton(
        key: const Key('check-approval'),
        onPressed: controller.busy ? null : controller.refreshDeviceStatus,
        child: Text(l10n.checkAgain),
      ),
      children: [
        const Icon(Icons.hourglass_top, size: 64),
        const SizedBox(height: 16),
        Text(l10n.waitingBody, key: const Key('waiting-approval')),
        const SizedBox(height: 16),
        FutureBuilder<String?>(
          future: controller.store.read(SecureKeys.deviceId),
          builder: (context, snap) => Text(l10n.deviceIdLabel(snap.data ?? '…'), style: Theme.of(context).textTheme.bodySmall),
        ),
      ],
    );
  }
}

class DeviceRejectedScreen extends StatelessWidget {
  const DeviceRejectedScreen({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.rejectedTitle,
      primary: FilledButton(key: const Key('register-again'), onPressed: controller.refreshDeviceStatus, child: Text(l10n.registerAgain)),
      children: [Text(l10n.rejectedBody, key: const Key('device-rejected'))],
    );
  }
}

class DeviceRevokedScreen extends StatelessWidget {
  const DeviceRevokedScreen({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.revokedTitle,
      primary: FilledButton(key: const Key('start-again'), onPressed: controller.refreshDeviceStatus, child: Text(l10n.startAgain)),
      children: [
        const Icon(Icons.block, size: 64),
        const SizedBox(height: 16),
        Text(controller.blockedCode == 'USER_DISABLED' ? l10n.userDisabledBody : l10n.revokedBody, key: const Key('device-revoked')),
      ],
    );
  }
}

/// The offline grant has run out (or the phone never received one): no new work until it reconnects (docs/lean-pilot/05
/// §5.5). What is already in the outbox is kept and sent once connected.
class GrantExpiredScreen extends StatelessWidget {
  const GrantExpiredScreen({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final waiting = (controller.outbox?.pendingCount ?? 0) + (controller.sync?.evidence.waitingCount ?? 0);
    return ActionScaffold(
      title: l10n.grantExpiredTitle,
      secondary: OutlinedButton(onPressed: controller.signOut, child: Text(l10n.signOut)),
      primary: FilledButton(key: const Key('grant-retry'), onPressed: controller.busy ? null : controller.retryConnection, child: Text(l10n.tryNow)),
      children: [
        const Icon(Icons.wifi_off, size: 64),
        const SizedBox(height: 16),
        Text(l10n.grantExpiredBody, key: const Key('grant-expired')),
        const SizedBox(height: 16),
        Text(l10n.grantPendingItems(waiting), key: const Key('grant-pending-items')),
      ],
    );
  }
}

/// 426 from the server: this version is below the `min_app_version` policy.
class UpdateRequiredScreen extends StatelessWidget {
  const UpdateRequiredScreen({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.updateRequiredTitle,
      primary: FilledButton(key: const Key('update-retry'), onPressed: controller.busy ? null : controller.retryConnection, child: Text(l10n.checkAgain)),
      children: [
        const Icon(Icons.system_update, size: 64),
        const SizedBox(height: 16),
        Text(l10n.updateRequiredBody(controller.minAppVersion ?? '?'), key: const Key('update-required')),
      ],
    );
  }
}
