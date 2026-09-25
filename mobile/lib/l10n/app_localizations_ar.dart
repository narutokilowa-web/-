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
      'هذا التطبيق يعمل على هاتفك الشخصي.\n\n• يستخدم الكاميرا داخل التطبيق فقط: لمسح رمز QR على أعمدة الإنارة ولالتقاط صور العمل. لا يحفظ الصور في معرض الهاتف، ولا يصل إلى معرض الصور أو الملفات أو جهات الاتصال أو أي تطبيق آخر. تُحذف صور العمل من الهاتف بعد رفعها.\n• لا يتتبع موقعك. (في مراحل لاحقة يُسجَّل الموقع فقط لحظة التقاط دليل عمل، والتطبيق مفتوح أمامك.)\n• بياناته مشفّرة داخل مساحة التطبيق الخاصة ومحميّة برمز PIN.\n• البلدية لا تستطيع مسح هاتفك أو إدارته. إذا أُلغي جهازك، يرفضه الخادم، ويحذف التطبيق بياناته هو فقط.\n• ما تسجّله من أعمال يُحفظ في سجل تدقيق يمكن التحقق منه.';

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

  @override
  String get fieldWorkTitle => 'العمل الميداني';

  @override
  String get takePhoto => 'التقاط صورة';

  @override
  String get photoUnreadable => 'تعذّرت قراءة الصورة. التقطها مجددًا.';

  @override
  String evidenceKindName(String kind) {
    String _temp0 = intl.Intl.selectLogic(kind, {
      'BEFORE': 'صورة قبل العمل',
      'AFTER': 'صورة بعد العمل',
      'TEST': 'صورة الاختبار',
      'other': '$kind',
    });
    return '$_temp0';
  }

  @override
  String get returnedTitle => 'أُعيد أمر العمل إليك. أضف المطلوب ثم أعد التقديم:';

  @override
  String returnReasonName(String reason) {
    String _temp0 = intl.Intl.selectLogic(reason, {
      'MISSING_EVIDENCE': 'صور ناقصة',
      'UNCLEAR_EVIDENCE': 'صور غير واضحة',
      'CHECKLIST_INCOMPLETE': 'قائمة الفحص ناقصة',
      'MATERIAL_QUESTION': 'استفسار عن المواد',
      'DIAGNOSIS_QUESTION': 'استفسار عن التشخيص',
      'other': 'سبب آخر',
    });
    return '$_temp0';
  }

  @override
  String commandRejected(String command, String error) {
    return 'رفض الخادم: $command — $error';
  }

  @override
  String commandName(String type) {
    String _temp0 = intl.Intl.selectLogic(type, {
      'ArriveOnSite': 'الوصول',
      'AddEvidence': 'صورة',
      'RecordDiagnosis': 'التشخيص',
      'StartWork': 'بدء العمل',
      'ConsumeMaterial': 'مادة',
      'RecordChecklist': 'قائمة الفحص',
      'SubmitWorkOrder': 'التقديم',
      'Resubmit': 'إعادة التقديم',
      'AcknowledgeStockIssue': 'تأكيد استلام مواد',
      'DisputeStockIssue': 'اعتراض على مواد',
      'DeclareStockReturn': 'إرجاع مواد',
      'DeclareCoreReturn': 'تصريح قطعة تالفة',
      'other': '$type',
    });
    return '$_temp0';
  }

  @override
  String photosWaiting(int count) {
    return '$count صورة بانتظار الرفع';
  }

  @override
  String get submittedWaitingReview => 'قُدّم العمل وهو بانتظار مراجعة المشرف.';

  @override
  String get stepArrive => 'الوصول وتأكيد الملصق';

  @override
  String get arriveAction => 'وصلت';

  @override
  String stepBefore(int count, int total) {
    return 'صور قبل العمل ($count/$total)';
  }

  @override
  String get stepDiagnosis => 'التشخيص';

  @override
  String get chooseDiagnosis => 'اختيار';

  @override
  String get stepStart => 'بدء العمل';

  @override
  String get startWorkAction => 'ابدأ';

  @override
  String get startWorkNeeds => 'يلزم صور قبل العمل والتشخيص أولًا.';

  @override
  String stepMaterial(int count) {
    return 'المواد المستخدمة ($count)';
  }

  @override
  String get addMaterial => 'إضافة مادة';

  @override
  String get stepChecklist => 'قائمة الفحص الوظيفي';

  @override
  String get openChecklist => 'فتح';

  @override
  String stepTestPhoto(int count, int total) {
    return 'صورة الاختبار ($count/$total)';
  }

  @override
  String stepAfter(int count, int total) {
    return 'صور بعد العمل ($count/$total)';
  }

  @override
  String get stepSubmit => 'التقديم للمراجعة';

  @override
  String get stepResubmit => 'إعادة التقديم';

  @override
  String get submitAction => 'تقديم';

  @override
  String get resubmitAction => 'إعادة التقديم';

  @override
  String get arriveWithCode => 'تأكيد بالرمز المكتوب';

  @override
  String arriveHint(String code) {
    return 'امسح ملصق QR على العمود $code.';
  }

  @override
  String get damagedTagHint => 'الملصق تالف أو غير مقروء؟ اكتب الرمز واذكر السبب.';

  @override
  String get manualReasonLabel => 'سبب الإدخال اليدوي';

  @override
  String get manualReasonRequired => 'اذكر سبب إدخال الرمز يدويًا.';

  @override
  String get noteOptional => 'ملاحظة (اختياري)';

  @override
  String get referenceMissing => 'لم تُحمَّل رموز التشخيص بعد. زامن ثم حاول مجددًا.';

  @override
  String get custodyHint => 'اختر من المواد الموجودة في عهدتك.';

  @override
  String get custodyEmpty => 'لا توجد مواد في عهدتك حسب آخر مزامنة.';

  @override
  String inCustody(String quantity, String unit) {
    return 'في العهدة: $quantity $unit';
  }

  @override
  String get chooseSerials => 'اختر الرقم التسلسلي للقطعة المركّبة:';

  @override
  String quantityLabel(String unit) {
    return 'الكمية ($unit)';
  }

  @override
  String get testMethodLabel => 'طريقة الاختبار';

  @override
  String get notTestedReason => 'سبب عدم الاختبار';

  @override
  String get yes => 'نعم';

  @override
  String get no => 'لا';

  @override
  String submitSummary(int before, int after, int test, int materials) {
    return 'قبل: $before · بعد: $after · اختبار: $test · مواد: $materials';
  }

  @override
  String get rootCauseLabel => 'السبب الجذري للعطل';

  @override
  String get noMaterialReasonLabel => 'لماذا لم تُستخدم أي مادة؟';

  @override
  String get recurrentFailureTitle => 'عطل متكرر';

  @override
  String get fullInspectionRecommended => 'توصية: يُنصح بفحص شامل للعمود';

  @override
  String recommendationRule(int count, int threshold, int days) {
    return '$count أعطال محسوبة؛ العتبة $threshold خلال $days يومًا';
  }

  @override
  String refusalReason(String code) {
    String _temp0 = intl.Intl.selectLogic(code, {
      'WORK_ORDER_CHANGED': 'تغيّر أمر العمل على الخادم بعد آخر تحديث للهاتف؛ حدّث المهمة وتحقق قبل إعادة الخطوة',
      'WORK_ORDER_REASSIGNED': 'أُعيد إسناد أمر العمل لفني آخر؛ صورك وموادك محفوظة لكن هذه الخطوة لم تُطبّق',
      'WORK_ORDER_CANCELLED': 'أُلغي أمر العمل؛ ينتظر العمل قرار المشرف',
      'WORK_ORDER_CLOSED': 'أُغلق أمر العمل؛ ينتظر العمل قرار المشرف',
      'BASE_VERSION_REQUIRED': 'حدّث المهمة ثم أعد المحاولة',
      'GRANT_REQUIRED': 'لا يوجد تصريح عمل دون اتصال',
      'GRANT_INVALID': 'تصريح العمل غير صالح لهذا الجهاز',
      'GRANT_EXPIRED': 'أُنجز بعد انتهاء تصريح العمل دون اتصال',
      'GRANT_REVOKED': 'أُنجز بعد إلغاء تصريح العمل',
      'GRANT_WINDOW_UNPROVEN': 'لا يمكن إثبات وقت إنجازه (أُعيد تشغيل الهاتف دون اتصال)؛ ينتظر قرار المشرف',
      'QUARANTINE_DISCARDED': 'رفضه المشرف بعد المراجعة',
      'NOT_ASSIGNEE': 'أمر العمل ليس مسندًا إليك',
      'INVALID_TRANSITION': 'لم تعد هذه الخطوة ممكنة في حالة أمر العمل الحالية',
      'CORE_RETURN_NOT_DECLARED': 'صرّح بالقطعة التالفة لكل قطعة استبدلتها قبل التقديم',
      'SERIALS_NOT_CONFIRMED': 'أكّد الأرقام التي استلمتها بالضبط، أو أبلغ عن مشكلة',
      'INSUFFICIENT_CUSTODY': 'الكمية أكبر مما في عهدتك',
      'SERIAL_NOT_IN_CUSTODY': 'هذا الرقم ليس في عهدتك',
      'NOT_RECEIVER': 'هذا الصرف ليس لك',
      'other': 'تعذّرت العملية ($code)',
    });
    return '$_temp0';
  }

  @override
  String commandHeld(String command, String reason) {
    return 'محجوز بانتظار قرار المشرف: $command — $reason';
  }

  @override
  String heldCount(int count) {
    return '$count بانتظار قرار المشرف';
  }

  @override
  String get grantExpiredTitle => 'اتصل بالإنترنت لمتابعة العمل';

  @override
  String get grantExpiredBody =>
      'انتهت مدة العمل دون اتصال المسموح بها لهذا الهاتف. اتصل بالإنترنت (وسجّل الدخول من جديد إن طُلب منك) لمتابعة العمل. لا يُسجَّل عمل جديد قبل ذلك.';

  @override
  String grantPendingItems(int count) {
    return '$count عنصر محفوظ على الهاتف وسيُرسل عند الاتصال';
  }

  @override
  String get tryNow => 'حاول الآن';

  @override
  String get updateRequiredTitle => 'يلزم تحديث التطبيق';

  @override
  String updateRequiredBody(String version) {
    return 'لم يعد الخادم يقبل هذا الإصدار من التطبيق. ثبّت الإصدار $version أو أحدث. ما حفظته على الهاتف يبقى محفوظًا.';
  }

  @override
  String get userDisabledBody =>
      'عُطّل حسابك في النظام، لذلك لم يعد الخادم يقبل شيئًا من هذا الهاتف. حذف التطبيق بياناته الخاصة المخزنة على الهاتف. لم يُمسّ أي شيء آخر على هاتفك.';

  @override
  String grantValidUntil(String time) {
    return 'العمل دون اتصال مسموح حتى $time';
  }

  @override
  String get grantNone => 'لا يوجد تصريح للعمل دون اتصال بعد: اتصل بالإنترنت';

  @override
  String get tabStock => 'المواد';

  @override
  String stepCoreReturn(int count) {
    return 'القطع التالفة: $count بحاجة إلى تصريح';
  }

  @override
  String get stepCoreReturnDone => 'القطع التالفة: صُرِّح بها';

  @override
  String get declareCoreAction => 'صرّح بالقطعة التالفة';

  @override
  String get coreReturnTitle => 'القطعة التالفة';

  @override
  String coreReturnHint(String part) {
    return 'استبدلت $part. أحضر القطعة التالفة إلى المستودع، أو اذكر لماذا لا يمكن إحضارها. الصورة مطلوبة في الحالتين.';
  }

  @override
  String coreExpectedSerial(String serial) {
    return 'الرقم المعروف على العمود: $serial';
  }

  @override
  String coreInstalledSerial(String serial) {
    return 'القطعة الجديدة المركّبة: $serial';
  }

  @override
  String get coreWithPart => 'القطعة التالفة معي';

  @override
  String get coreUnrecoverable => 'لا يمكن إحضارها';

  @override
  String get coreSerialLabel => 'الرقم التسلسلي على القطعة التالفة';

  @override
  String get coreReasonLabel => 'لماذا لا يمكن إحضارها؟';

  @override
  String get corePhotoAction => 'صوّر القطعة التالفة';

  @override
  String get corePhotoReady => 'الصورة جاهزة';

  @override
  String coreStateName(String state) {
    String _temp0 = intl.Intl.selectLogic(state, {
      'OPEN': 'بحاجة إلى تصريح',
      'DECLARED_WITH_PART': 'أحضرها إلى المستودع',
      'DECLARED_UNRECOVERABLE': 'غير قابلة للاسترداد: ينتظر قرار المشرف',
      'RECEIVED': 'استلمها المستودع',
      'WAIVED': 'أُعفيت',
      'other': '$state',
    });
    return '$_temp0';
  }

  @override
  String get issuesToConfirmTitle => 'مواد بانتظار تأكيد استلامك';

  @override
  String issueFrom(String code, String name) {
    return '$code · صرفها $name';
  }

  @override
  String get tickSerials => 'أشِّر على كل رقم تسلسلي استلمته فعلًا';

  @override
  String get confirmReceipt => 'تأكيد الاستلام';

  @override
  String get reportProblem => 'الإبلاغ عن مشكلة';

  @override
  String get disputeReasonLabel => 'ما المشكلة؟ (مثلًا: وصلت قطعة واحدة فقط)';

  @override
  String get send => 'إرسال';

  @override
  String get myCustodyTitle => 'عهدتي';

  @override
  String get returnUnused => 'إرجاع مواد غير مستخدمة';

  @override
  String get returnNoteLabel => 'ملاحظة (اختياري)';

  @override
  String get returnsWaitingTitle => 'مرتجعات بانتظار استلام المستودع';

  @override
  String get coresTitle => 'القطع التالفة';

  @override
  String get nothingWaiting => 'لا شيء بانتظارك.';

  @override
  String get stockHint => 'المواد المصروفة لك لا تدخل عهدتك إلا بعد تأكيد استلامها. الإرجاع يبقى «قيد التسليم» حتى يستلمه المستودع.';
}
