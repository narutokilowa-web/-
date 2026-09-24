import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import '../features/home/home_screen.dart';
import '../l10n/app_localizations.dart';

/// Root widget. Arabic (right-to-left) is the default locale; English is secondary.
class MfopApp extends StatefulWidget {
  const MfopApp({super.key, this.environment = 'DEV'});

  final String environment;

  @override
  State<MfopApp> createState() => _MfopAppState();
}

class _MfopAppState extends State<MfopApp> {
  Locale _locale = const Locale('ar');

  void _toggleLocale() {
    setState(() {
      _locale = _locale.languageCode == 'ar' ? const Locale('en') : const Locale('ar');
    });
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      onGenerateTitle: (context) => AppLocalizations.of(context).appTitle,
      locale: _locale,
      supportedLocales: AppLocalizations.supportedLocales,
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      theme: ThemeData(
        colorSchemeSeed: const Color(0xFF1B5E20),
        useMaterial3: true,
        // Field use: large touch targets (spec 08 §8.1: ≥ 56 dp).
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56)),
        ),
      ),
      home: HomeScreen(
        environment: widget.environment,
        onToggleLocale: _toggleLocale,
      ),
    );
  }
}
