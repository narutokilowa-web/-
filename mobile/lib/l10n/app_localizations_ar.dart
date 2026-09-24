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
  String environmentLabel(String env) {
    return 'البيئة: $env';
  }

  @override
  String get languageToggle => 'English';

  @override
  String get privacyTitle => 'قبل البدء: خصوصيتك';

  @override
  String get privacyBody =>
      'هذا التطبيق يعمل على هاتفك الشخصي.\n\n• يستخدم الكاميرا لمسح رمز QR على أعمدة الإنارة فقط، ولا يصل إلى معرض الصور أو الملفات أو جهات الاتصال أو أي تطبيق آخر.\n• لا يتتبع موقعك. (في مراحل لاحقة يُسجَّل الموقع فقط لحظة التقاط دليل عمل، والتطبيق مفتوح أمامك.)\n• بياناته مشفّرة داخل مساحة التطبيق الخاصة ومحميّة برمز PIN.\n• البلدية لا تستطيع مسح هاتفك أو إدارته. إذا أُلغي جهازك، يرفضه الخادم، ويحذف التطبيق بياناته هو فقط.\n• ما تسجّله من أعمال يُحفظ في سجل تدقيق يمكن التحقق منه.';

  @override
  String get privacyAccept => 'فهمت وأوافق';

  @override
  String get signInTitle => 'تسجيل الدخول';

  @override
  String get signInBody => 'سيُفتح متصفح الهاتف لتسجيل الدخول بحسابك في البلدية.';

  @override
  String get signInButton => 'تسجيل الدخول';

  @override
  String get offlineMessage => 'لا يوجد اتصال بالخادم. تحقق من الشبكة ثم حاول مجددًا.';

  @override
  String errorCode(String code) {
    return 'تعذّر إكمال العملية ($code).';
  }

  @override
  String get waitingTitle => 'بانتظار الموافقة على الجهاز';

  @override
  String get waitingBody => 'سُجّل هذا الهاتف. يجب أن يوافق عليه مشرف أو مدير النظام من لوحة الويب قبل استخدامه.';

  @override
  String get checkAgain => 'تحقق الآن';

  @override
  String deviceIdLabel(String id) {
    return 'رقم الجهاز: $id';
  }

  @override
  String get rejectedTitle => 'رُفض طلب الجهاز';

  @override
  String get rejectedBody => 'لم تتم الموافقة على هذا الهاتف. تواصل مع مشرفك، ثم أرسل طلبًا جديدًا.';

  @override
  String get registerAgain => 'إرسال طلب جديد';

  @override
  String get revokedTitle => 'أُلغي هذا الجهاز';

  @override
  String get revokedBody =>
      'ألغت البلدية صلاحية هذا الهاتف، فلم يعد الخادم يقبل منه أي عملية. حذف التطبيق بياناته الخاصة المخزّنة على الهاتف. لم يُلمس أي شيء آخر على هاتفك.';

  @override
  String get startAgain => 'البدء من جديد';

  @override
  String get pinSetupTitle => 'اختر رمز PIN للتطبيق';

  @override
  String pinSetupBody(int minutes) {
    return 'من 4 إلى 8 أرقام. يُطلب عند فتح التطبيق بعد $minutes دقائق في الخلفية.';
  }

  @override
  String get pinLabel => 'رمز PIN';

  @override
  String get pinConfirmLabel => 'أعد إدخال الرمز';

  @override
  String get pinMismatch => 'الرمزان غير متطابقين.';

  @override
  String get pinFormat => 'استخدم من 4 إلى 8 أرقام.';

  @override
  String get save => 'حفظ';

  @override
  String get lockTitle => 'التطبيق مقفل';

  @override
  String get unlock => 'فتح';

  @override
  String pinWrong(int left) {
    return 'رمز غير صحيح. المحاولات المتبقية: $left';
  }

  @override
  String get tabTasks => 'مهامي';

  @override
  String get tabScan => 'مسح';

  @override
  String get tabMe => 'أنا';

  @override
  String get online => 'متصل';

  @override
  String get offline => 'غير متصل';

  @override
  String waitingToSend(int count) {
    return '$count بانتظار الإرسال';
  }

  @override
  String lastSync(String time) {
    return 'آخر مزامنة: $time';
  }

  @override
  String get neverSynced => 'لم تتم المزامنة بعد';

  @override
  String get noTasks => 'لا توجد مهام مُسندة إليك الآن.';

  @override
  String get notTechnician => 'قائمة المهام للفنيين. يمكنك مسح رموز الأعمدة من تبويب «مسح».';

  @override
  String taskTitle(String code) {
    return 'أمر العمل $code';
  }

  @override
  String get faultLabel => 'العطل';

  @override
  String get noteLabel => 'ملاحظة المشرف';

  @override
  String assignedAt(String time) {
    return 'أُسند في $time';
  }

  @override
  String dueAt(String time) {
    return 'الموعد: $time';
  }

  @override
  String get taskNextSteps => 'تنفيذ العمل في الموقع (الوصول، الصور، التشخيص...) يُضاف في المرحلة التالية.';

  @override
  String get scanHint => 'وجّه الكاميرا إلى رمز QR على العمود.';

  @override
  String get manualCodeLabel => 'أو اكتب الرمز المطبوع (مثال: 7K3M92QX.4F)';

  @override
  String get resolve => 'عرض';

  @override
  String get invalidCode => 'رمز غير صالح.';

  @override
  String get tagNotFound => 'لا يوجد أصل فعّال بهذا الرمز.';

  @override
  String get cameraUnavailable => 'الكاميرا غير متاحة. استخدم إدخال الرمز يدويًا.';

  @override
  String get assetTypeStreetLight => 'نقطة إنارة';

  @override
  String get statusLabel => 'الحالة';

  @override
  String get powerSourceLabel => 'مصدر الطاقة';

  @override
  String get responsibleLabel => 'المسؤولية';

  @override
  String get addressLabel => 'العنوان';

  @override
  String openFaultsLabel(int count) {
    return 'أعطال مفتوحة: $count';
  }

  @override
  String get myTaskOnAsset => 'لديك مهمة على هذه النقطة';

  @override
  String get openTask => 'فتح المهمة';

  @override
  String get recentEvents => 'آخر الأحداث';

  @override
  String get meTitle => 'حسابي';

  @override
  String rolesLabel(String roles) {
    return 'الأدوار: $roles';
  }

  @override
  String get syncNow => 'إرسال ومزامنة الآن';

  @override
  String get signOut => 'تسجيل الخروج';

  @override
  String get outboxEmpty => 'لا يوجد ما ينتظر الإرسال.';

  @override
  String opStatus(String status) {
    String _temp0 = intl.Intl.selectLogic(status, {
      'OPERATIONAL': 'يعمل',
      'FAULTY': 'معطّل',
      'UNKNOWN': 'غير معروف',
      'UNDER_MAINTENANCE': 'قيد الصيانة',
      'other': '$status',
    });
    return '$_temp0';
  }

  @override
  String powerSourceName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {'GRID': 'الشبكة', 'SOLAR': 'شمسية', 'HYBRID': 'هجينة', 'other': '$value'});
    return '$_temp0';
  }

  @override
  String responsibleName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'MUNICIPALITY': 'البلدية',
      'ELECTRICITY_AUTHORITY': 'جهة الكهرباء',
      'OTHER_PUBLIC_ENTITY': 'جهة عامة أخرى',
      'SHARED': 'مشتركة',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String faultCategoryName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'LIGHT_OFF': 'لا تضيء',
      'FLICKERING': 'وميض متقطع',
      'DAY_BURNING': 'تضيء نهارًا',
      'DAMAGED_POLE': 'عمود متضرر',
      'EXPOSED_WIRES': 'أسلاك مكشوفة',
      'MISSING_PARTS': 'قطع مفقودة',
      'OTHER': 'أخرى',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String powerAtInspectionName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'CONFIRMED_AVAILABLE': 'الكهرباء متوفرة',
      'CONFIRMED_UNAVAILABLE': 'الكهرباء مقطوعة',
      'UNKNOWN': 'الكهرباء: غير معروف',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String priorityName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'P1': 'P1 عاجل جدًا',
      'P2': 'P2 عاجل',
      'P3': 'P3 عادي',
      'P4': 'P4 منخفض',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String workOrderStateName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'ASSIGNED': 'مُسند',
      'ON_SITE': 'في الموقع',
      'IN_PROGRESS': 'قيد التنفيذ',
      'PENDING_REVIEW': 'بانتظار المراجعة',
      'RETURNED': 'مُعاد',
      'other': '$value',
    });
    return '$_temp0';
  }
}
