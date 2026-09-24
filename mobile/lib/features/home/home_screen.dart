import 'package:flutter/material.dart';

import '../../l10n/app_localizations.dart';

/// M0 placeholder screen. Real screens (login, scan, tasks) arrive in M2/M3.
class HomeScreen extends StatelessWidget {
  const HomeScreen({
    super.key,
    required this.environment,
    required this.onToggleLocale,
  });

  final String environment;
  final VoidCallback onToggleLocale;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.appTitle)),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              l10n.environmentLabel(environment),
              key: const Key('environment'),
            ),
            const SizedBox(height: 16),
            Text(l10n.m0Title, style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 8),
            Text(l10n.m0Body),
            const Spacer(),
            FilledButton(
              key: const Key('toggle-language'),
              onPressed: onToggleLocale,
              child: Text(l10n.languageToggle),
            ),
          ],
        ),
      ),
    );
  }
}
