import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import '../features/auth/sign_in_screen.dart';
import '../features/device/device_screens.dart';
import '../features/home/home_shell.dart';
import '../features/pin/pin_screens.dart';
import '../features/privacy/privacy_screen.dart';
import '../l10n/app_localizations.dart';
import 'app_controller.dart';

/// Root widget. Arabic (right-to-left) is the default locale; English is secondary.
class MfopApp extends StatefulWidget {
  const MfopApp({super.key, required this.controller, this.showCamera = true});

  final AppController controller;
  final bool showCamera;

  @override
  State<MfopApp> createState() => _MfopAppState();
}

class _MfopAppState extends State<MfopApp> {
  Locale _locale = const Locale('ar');

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(widget.controller);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(widget.controller);
    super.dispose();
  }

  void _toggleLocale() => setState(() => _locale = _locale.languageCode == 'ar' ? const Locale('en') : const Locale('ar'));

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
        filledButtonTheme: FilledButtonThemeData(style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56))),
        outlinedButtonTheme: OutlinedButtonThemeData(style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(56))),
      ),
      darkTheme: ThemeData(colorSchemeSeed: const Color(0xFF1B5E20), useMaterial3: true, brightness: Brightness.dark),
      home: ListenableBuilder(listenable: widget.controller, builder: (context, _) => _screen()),
    );
  }

  Widget _screen() {
    final c = widget.controller;
    return switch (c.stage) {
      AppStage.starting => const Scaffold(body: Center(child: CircularProgressIndicator())),
      AppStage.privacy => PrivacyScreen(controller: c),
      AppStage.signedOut => SignInScreen(controller: c, onToggleLocale: _toggleLocale),
      AppStage.waitingApproval => WaitingApprovalScreen(controller: c),
      AppStage.deviceRejected => DeviceRejectedScreen(controller: c),
      AppStage.deviceRevoked => DeviceRevokedScreen(controller: c),
      AppStage.setPin => PinSetupScreen(controller: c),
      AppStage.locked => PinLockScreen(controller: c),
      AppStage.ready => HomeShell(controller: c, onToggleLocale: _toggleLocale, showCamera: widget.showCamera),
    };
  }
}
