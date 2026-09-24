// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Arabic (`ar`).
class AppLocalizationsAr extends AppLocalizations {
  AppLocalizationsAr([String locale = 'ar']) : super(locale);

  @override
  String get appTitle => 'تطبيق العمليات الميدانية';

  @override
  String get m0Title => 'المرحلة M0 — هيكل التطبيق';

  @override
  String get m0Body =>
      'هذا هيكل أولي. تسجيل الدخول ومسح رمز QR يُضافان في المرحلة M2.';

  @override
  String environmentLabel(String env) {
    return 'البيئة: $env';
  }

  @override
  String get languageToggle => 'English';
}
