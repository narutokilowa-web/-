import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';

class PrivacyScreen extends StatelessWidget {
  const PrivacyScreen({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.privacyTitle,
      primary: FilledButton(key: const Key('privacy-accept'), onPressed: controller.acceptPrivacy, child: Text(l10n.privacyAccept)),
      children: [Text(l10n.privacyBody, style: Theme.of(context).textTheme.bodyLarge)],
    );
  }
}
