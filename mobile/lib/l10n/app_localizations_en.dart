// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'Field Operations App';

  @override
  String get m0Title => 'M0 — application skeleton';

  @override
  String get m0Body =>
      'Initial skeleton. Sign-in and QR scanning arrive in M2.';

  @override
  String environmentLabel(String env) {
    return 'Environment: $env';
  }

  @override
  String get languageToggle => 'العربية';
}
