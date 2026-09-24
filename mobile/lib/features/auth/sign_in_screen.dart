import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';

class SignInScreen extends StatelessWidget {
  const SignInScreen({super.key, required this.controller, required this.onToggleLocale});

  final AppController controller;
  final VoidCallback onToggleLocale;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final message = controller.message;
    return ActionScaffold(
      title: l10n.signInTitle,
      secondary: OutlinedButton(key: const Key('toggle-language'), onPressed: onToggleLocale, child: Text(l10n.languageToggle)),
      primary: FilledButton(
        key: const Key('sign-in'),
        onPressed: controller.busy ? null : controller.signIn,
        child: Text(l10n.signInButton),
      ),
      children: [
        Text(l10n.environmentLabel(controller.config.environment), key: const Key('environment')),
        const SizedBox(height: 16),
        Text(l10n.signInBody),
        if (message != null) ...[
          const SizedBox(height: 16),
          Text(
            message == 'offline' ? l10n.offlineMessage : l10n.errorCode(message),
            key: const Key('sign-in-message'),
            style: const TextStyle(color: Color(0xFFB42318)),
          ),
        ],
      ],
    );
  }
}
