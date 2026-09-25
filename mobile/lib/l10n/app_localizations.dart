import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_ar.dart';
import 'app_localizations_en.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale) : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate = _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates = <LocalizationsDelegate<dynamic>>[
    delegate,
    GlobalMaterialLocalizations.delegate,
    GlobalCupertinoLocalizations.delegate,
    GlobalWidgetsLocalizations.delegate,
  ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[Locale('ar'), Locale('en')];

  /// No description provided for @appTitle.
  ///
  /// In ar, this message translates to:
  /// **'تطبيق العمليات الميدانية'**
  String get appTitle;

  /// No description provided for @environmentLabel.
  ///
  /// In ar, this message translates to:
  /// **'البيئة: {env}'**
  String environmentLabel(String env);

  /// No description provided for @languageToggle.
  ///
  /// In ar, this message translates to:
  /// **'English'**
  String get languageToggle;

  /// No description provided for @privacyTitle.
  ///
  /// In ar, this message translates to:
  /// **'قبل البدء: خصوصيتك'**
  String get privacyTitle;

  /// No description provided for @privacyBody.
  ///
  /// In ar, this message translates to:
  /// **'هذا التطبيق يعمل على هاتفك الشخصي.\n\n• يستخدم الكاميرا داخل التطبيق فقط: لمسح رمز QR على أعمدة الإنارة ولالتقاط صور العمل. لا يحفظ الصور في معرض الهاتف، ولا يصل إلى معرض الصور أو الملفات أو جهات الاتصال أو أي تطبيق آخر. تُحذف صور العمل من الهاتف بعد رفعها.\n• لا يتتبع موقعك. (في مراحل لاحقة يُسجَّل الموقع فقط لحظة التقاط دليل عمل، والتطبيق مفتوح أمامك.)\n• بياناته مشفّرة داخل مساحة التطبيق الخاصة ومحميّة برمز PIN.\n• البلدية لا تستطيع مسح هاتفك أو إدارته. إذا أُلغي جهازك، يرفضه الخادم، ويحذف التطبيق بياناته هو فقط.\n• ما تسجّله من أعمال يُحفظ في سجل تدقيق يمكن التحقق منه.'**
  String get privacyBody;

  /// No description provided for @privacyAccept.
  ///
  /// In ar, this message translates to:
  /// **'فهمت وأوافق'**
  String get privacyAccept;

  /// No description provided for @signInTitle.
  ///
  /// In ar, this message translates to:
  /// **'تسجيل الدخول'**
  String get signInTitle;

  /// No description provided for @signInBody.
  ///
  /// In ar, this message translates to:
  /// **'سيُفتح متصفح الهاتف لتسجيل الدخول بحسابك في البلدية.'**
  String get signInBody;

  /// No description provided for @signInButton.
  ///
  /// In ar, this message translates to:
  /// **'تسجيل الدخول'**
  String get signInButton;

  /// No description provided for @offlineMessage.
  ///
  /// In ar, this message translates to:
  /// **'لا يوجد اتصال بالخادم. تحقق من الشبكة ثم حاول مجددًا.'**
  String get offlineMessage;

  /// No description provided for @errorCode.
  ///
  /// In ar, this message translates to:
  /// **'تعذّر إكمال العملية ({code}).'**
  String errorCode(String code);

  /// No description provided for @waitingTitle.
  ///
  /// In ar, this message translates to:
  /// **'بانتظار الموافقة على الجهاز'**
  String get waitingTitle;

  /// No description provided for @waitingBody.
  ///
  /// In ar, this message translates to:
  /// **'سُجّل هذا الهاتف. يجب أن يوافق عليه مشرف أو مدير النظام من لوحة الويب قبل استخدامه.'**
  String get waitingBody;

  /// No description provided for @checkAgain.
  ///
  /// In ar, this message translates to:
  /// **'تحقق الآن'**
  String get checkAgain;

  /// No description provided for @deviceIdLabel.
  ///
  /// In ar, this message translates to:
  /// **'رقم الجهاز: {id}'**
  String deviceIdLabel(String id);

  /// No description provided for @rejectedTitle.
  ///
  /// In ar, this message translates to:
  /// **'رُفض طلب الجهاز'**
  String get rejectedTitle;

  /// No description provided for @rejectedBody.
  ///
  /// In ar, this message translates to:
  /// **'لم تتم الموافقة على هذا الهاتف. تواصل مع مشرفك، ثم أرسل طلبًا جديدًا.'**
  String get rejectedBody;

  /// No description provided for @registerAgain.
  ///
  /// In ar, this message translates to:
  /// **'إرسال طلب جديد'**
  String get registerAgain;

  /// No description provided for @revokedTitle.
  ///
  /// In ar, this message translates to:
  /// **'أُلغي هذا الجهاز'**
  String get revokedTitle;

  /// No description provided for @revokedBody.
  ///
  /// In ar, this message translates to:
  /// **'ألغت البلدية صلاحية هذا الهاتف، فلم يعد الخادم يقبل منه أي عملية. حذف التطبيق بياناته الخاصة المخزّنة على الهاتف. لم يُلمس أي شيء آخر على هاتفك.'**
  String get revokedBody;

  /// No description provided for @startAgain.
  ///
  /// In ar, this message translates to:
  /// **'البدء من جديد'**
  String get startAgain;

  /// No description provided for @pinSetupTitle.
  ///
  /// In ar, this message translates to:
  /// **'اختر رمز PIN للتطبيق'**
  String get pinSetupTitle;

  /// No description provided for @pinSetupBody.
  ///
  /// In ar, this message translates to:
  /// **'من 4 إلى 8 أرقام. يُطلب عند فتح التطبيق بعد {minutes} دقائق في الخلفية.'**
  String pinSetupBody(int minutes);

  /// No description provided for @pinLabel.
  ///
  /// In ar, this message translates to:
  /// **'رمز PIN'**
  String get pinLabel;

  /// No description provided for @pinConfirmLabel.
  ///
  /// In ar, this message translates to:
  /// **'أعد إدخال الرمز'**
  String get pinConfirmLabel;

  /// No description provided for @pinMismatch.
  ///
  /// In ar, this message translates to:
  /// **'الرمزان غير متطابقين.'**
  String get pinMismatch;

  /// No description provided for @pinFormat.
  ///
  /// In ar, this message translates to:
  /// **'استخدم من 4 إلى 8 أرقام.'**
  String get pinFormat;

  /// No description provided for @save.
  ///
  /// In ar, this message translates to:
  /// **'حفظ'**
  String get save;

  /// No description provided for @lockTitle.
  ///
  /// In ar, this message translates to:
  /// **'التطبيق مقفل'**
  String get lockTitle;

  /// No description provided for @unlock.
  ///
  /// In ar, this message translates to:
  /// **'فتح'**
  String get unlock;

  /// No description provided for @pinWrong.
  ///
  /// In ar, this message translates to:
  /// **'رمز غير صحيح. المحاولات المتبقية: {left}'**
  String pinWrong(int left);

  /// No description provided for @tabTasks.
  ///
  /// In ar, this message translates to:
  /// **'مهامي'**
  String get tabTasks;

  /// No description provided for @tabScan.
  ///
  /// In ar, this message translates to:
  /// **'مسح'**
  String get tabScan;

  /// No description provided for @tabMe.
  ///
  /// In ar, this message translates to:
  /// **'أنا'**
  String get tabMe;

  /// No description provided for @online.
  ///
  /// In ar, this message translates to:
  /// **'متصل'**
  String get online;

  /// No description provided for @offline.
  ///
  /// In ar, this message translates to:
  /// **'غير متصل'**
  String get offline;

  /// No description provided for @waitingToSend.
  ///
  /// In ar, this message translates to:
  /// **'{count} بانتظار الإرسال'**
  String waitingToSend(int count);

  /// No description provided for @lastSync.
  ///
  /// In ar, this message translates to:
  /// **'آخر مزامنة: {time}'**
  String lastSync(String time);

  /// No description provided for @neverSynced.
  ///
  /// In ar, this message translates to:
  /// **'لم تتم المزامنة بعد'**
  String get neverSynced;

  /// No description provided for @noTasks.
  ///
  /// In ar, this message translates to:
  /// **'لا توجد مهام مُسندة إليك الآن.'**
  String get noTasks;

  /// No description provided for @notTechnician.
  ///
  /// In ar, this message translates to:
  /// **'قائمة المهام للفنيين. يمكنك مسح رموز الأعمدة من تبويب «مسح».'**
  String get notTechnician;

  /// No description provided for @taskTitle.
  ///
  /// In ar, this message translates to:
  /// **'أمر العمل {code}'**
  String taskTitle(String code);

  /// No description provided for @faultLabel.
  ///
  /// In ar, this message translates to:
  /// **'العطل'**
  String get faultLabel;

  /// No description provided for @noteLabel.
  ///
  /// In ar, this message translates to:
  /// **'ملاحظة المشرف'**
  String get noteLabel;

  /// No description provided for @assignedAt.
  ///
  /// In ar, this message translates to:
  /// **'أُسند في {time}'**
  String assignedAt(String time);

  /// No description provided for @dueAt.
  ///
  /// In ar, this message translates to:
  /// **'الموعد: {time}'**
  String dueAt(String time);

  /// No description provided for @taskNextSteps.
  ///
  /// In ar, this message translates to:
  /// **'تنفيذ العمل في الموقع (الوصول، الصور، التشخيص...) يُضاف في المرحلة التالية.'**
  String get taskNextSteps;

  /// No description provided for @scanHint.
  ///
  /// In ar, this message translates to:
  /// **'وجّه الكاميرا إلى رمز QR على العمود.'**
  String get scanHint;

  /// No description provided for @manualCodeLabel.
  ///
  /// In ar, this message translates to:
  /// **'أو اكتب الرمز المطبوع (مثال: 7K3M92QX.4F)'**
  String get manualCodeLabel;

  /// No description provided for @resolve.
  ///
  /// In ar, this message translates to:
  /// **'عرض'**
  String get resolve;

  /// No description provided for @invalidCode.
  ///
  /// In ar, this message translates to:
  /// **'رمز غير صالح.'**
  String get invalidCode;

  /// No description provided for @tagNotFound.
  ///
  /// In ar, this message translates to:
  /// **'لا يوجد أصل فعّال بهذا الرمز.'**
  String get tagNotFound;

  /// No description provided for @cameraUnavailable.
  ///
  /// In ar, this message translates to:
  /// **'الكاميرا غير متاحة. استخدم إدخال الرمز يدويًا.'**
  String get cameraUnavailable;

  /// No description provided for @assetTypeStreetLight.
  ///
  /// In ar, this message translates to:
  /// **'نقطة إنارة'**
  String get assetTypeStreetLight;

  /// No description provided for @statusLabel.
  ///
  /// In ar, this message translates to:
  /// **'الحالة'**
  String get statusLabel;

  /// No description provided for @powerSourceLabel.
  ///
  /// In ar, this message translates to:
  /// **'مصدر الطاقة'**
  String get powerSourceLabel;

  /// No description provided for @responsibleLabel.
  ///
  /// In ar, this message translates to:
  /// **'المسؤولية'**
  String get responsibleLabel;

  /// No description provided for @addressLabel.
  ///
  /// In ar, this message translates to:
  /// **'العنوان'**
  String get addressLabel;

  /// No description provided for @openFaultsLabel.
  ///
  /// In ar, this message translates to:
  /// **'أعطال مفتوحة: {count}'**
  String openFaultsLabel(int count);

  /// No description provided for @myTaskOnAsset.
  ///
  /// In ar, this message translates to:
  /// **'لديك مهمة على هذه النقطة'**
  String get myTaskOnAsset;

  /// No description provided for @openTask.
  ///
  /// In ar, this message translates to:
  /// **'فتح المهمة'**
  String get openTask;

  /// No description provided for @recentEvents.
  ///
  /// In ar, this message translates to:
  /// **'آخر الأحداث'**
  String get recentEvents;

  /// No description provided for @meTitle.
  ///
  /// In ar, this message translates to:
  /// **'حسابي'**
  String get meTitle;

  /// No description provided for @rolesLabel.
  ///
  /// In ar, this message translates to:
  /// **'الأدوار: {roles}'**
  String rolesLabel(String roles);

  /// No description provided for @syncNow.
  ///
  /// In ar, this message translates to:
  /// **'إرسال ومزامنة الآن'**
  String get syncNow;

  /// No description provided for @signOut.
  ///
  /// In ar, this message translates to:
  /// **'تسجيل الخروج'**
  String get signOut;

  /// No description provided for @outboxEmpty.
  ///
  /// In ar, this message translates to:
  /// **'لا يوجد ما ينتظر الإرسال.'**
  String get outboxEmpty;

  /// No description provided for @opStatus.
  ///
  /// In ar, this message translates to:
  /// **'{status, select, OPERATIONAL{يعمل} FAULTY{معطّل} UNKNOWN{غير معروف} UNDER_MAINTENANCE{قيد الصيانة} other{{status}}}'**
  String opStatus(String status);

  /// No description provided for @powerSourceName.
  ///
  /// In ar, this message translates to:
  /// **'{value, select, GRID{الشبكة} SOLAR{شمسية} HYBRID{هجينة} other{{value}}}'**
  String powerSourceName(String value);

  /// No description provided for @responsibleName.
  ///
  /// In ar, this message translates to:
  /// **'{value, select, MUNICIPALITY{البلدية} ELECTRICITY_AUTHORITY{جهة الكهرباء} OTHER_PUBLIC_ENTITY{جهة عامة أخرى} SHARED{مشتركة} other{{value}}}'**
  String responsibleName(String value);

  /// No description provided for @faultCategoryName.
  ///
  /// In ar, this message translates to:
  /// **'{value, select, LIGHT_OFF{لا تضيء} FLICKERING{وميض متقطع} DAY_BURNING{تضيء نهارًا} DAMAGED_POLE{عمود متضرر} EXPOSED_WIRES{أسلاك مكشوفة} MISSING_PARTS{قطع مفقودة} OTHER{أخرى} other{{value}}}'**
  String faultCategoryName(String value);

  /// No description provided for @powerAtInspectionName.
  ///
  /// In ar, this message translates to:
  /// **'{value, select, CONFIRMED_AVAILABLE{الكهرباء متوفرة} CONFIRMED_UNAVAILABLE{الكهرباء مقطوعة} UNKNOWN{الكهرباء: غير معروف} other{{value}}}'**
  String powerAtInspectionName(String value);

  /// No description provided for @priorityName.
  ///
  /// In ar, this message translates to:
  /// **'{value, select, P1{P1 عاجل جدًا} P2{P2 عاجل} P3{P3 عادي} P4{P4 منخفض} other{{value}}}'**
  String priorityName(String value);

  /// No description provided for @workOrderStateName.
  ///
  /// In ar, this message translates to:
  /// **'{value, select, ASSIGNED{مُسند} ON_SITE{في الموقع} IN_PROGRESS{قيد التنفيذ} PENDING_REVIEW{بانتظار المراجعة} RETURNED{مُعاد} other{{value}}}'**
  String workOrderStateName(String value);

  /// No description provided for @fieldWorkTitle.
  ///
  /// In ar, this message translates to:
  /// **'العمل الميداني'**
  String get fieldWorkTitle;

  /// No description provided for @takePhoto.
  ///
  /// In ar, this message translates to:
  /// **'التقاط صورة'**
  String get takePhoto;

  /// No description provided for @photoUnreadable.
  ///
  /// In ar, this message translates to:
  /// **'تعذّرت قراءة الصورة. التقطها مجددًا.'**
  String get photoUnreadable;

  /// No description provided for @evidenceKindName.
  ///
  /// In ar, this message translates to:
  /// **'{kind, select, BEFORE{صورة قبل العمل} AFTER{صورة بعد العمل} TEST{صورة الاختبار} other{{kind}}}'**
  String evidenceKindName(String kind);

  /// No description provided for @returnedTitle.
  ///
  /// In ar, this message translates to:
  /// **'أُعيد أمر العمل إليك. أضف المطلوب ثم أعد التقديم:'**
  String get returnedTitle;

  /// No description provided for @returnReasonName.
  ///
  /// In ar, this message translates to:
  /// **'{reason, select, MISSING_EVIDENCE{صور ناقصة} UNCLEAR_EVIDENCE{صور غير واضحة} CHECKLIST_INCOMPLETE{قائمة الفحص ناقصة} MATERIAL_QUESTION{استفسار عن المواد} DIAGNOSIS_QUESTION{استفسار عن التشخيص} other{سبب آخر}}'**
  String returnReasonName(String reason);

  /// No description provided for @commandRejected.
  ///
  /// In ar, this message translates to:
  /// **'رفض الخادم: {command} — {error}'**
  String commandRejected(String command, String error);

  /// No description provided for @commandName.
  ///
  /// In ar, this message translates to:
  /// **'{type, select, ArriveOnSite{الوصول} AddEvidence{صورة} RecordDiagnosis{التشخيص} StartWork{بدء العمل} ConsumeMaterial{مادة} RecordChecklist{قائمة الفحص} SubmitWorkOrder{التقديم} Resubmit{إعادة التقديم} other{{type}}}'**
  String commandName(String type);

  /// No description provided for @photosWaiting.
  ///
  /// In ar, this message translates to:
  /// **'{count} صورة بانتظار الرفع'**
  String photosWaiting(int count);

  /// No description provided for @submittedWaitingReview.
  ///
  /// In ar, this message translates to:
  /// **'قُدّم العمل وهو بانتظار مراجعة المشرف.'**
  String get submittedWaitingReview;

  /// No description provided for @stepArrive.
  ///
  /// In ar, this message translates to:
  /// **'الوصول وتأكيد الملصق'**
  String get stepArrive;

  /// No description provided for @arriveAction.
  ///
  /// In ar, this message translates to:
  /// **'وصلت'**
  String get arriveAction;

  /// No description provided for @stepBefore.
  ///
  /// In ar, this message translates to:
  /// **'صور قبل العمل ({count}/{total})'**
  String stepBefore(int count, int total);

  /// No description provided for @stepDiagnosis.
  ///
  /// In ar, this message translates to:
  /// **'التشخيص'**
  String get stepDiagnosis;

  /// No description provided for @chooseDiagnosis.
  ///
  /// In ar, this message translates to:
  /// **'اختيار'**
  String get chooseDiagnosis;

  /// No description provided for @stepStart.
  ///
  /// In ar, this message translates to:
  /// **'بدء العمل'**
  String get stepStart;

  /// No description provided for @startWorkAction.
  ///
  /// In ar, this message translates to:
  /// **'ابدأ'**
  String get startWorkAction;

  /// No description provided for @startWorkNeeds.
  ///
  /// In ar, this message translates to:
  /// **'يلزم صور قبل العمل والتشخيص أولًا.'**
  String get startWorkNeeds;

  /// No description provided for @stepMaterial.
  ///
  /// In ar, this message translates to:
  /// **'المواد المستخدمة ({count})'**
  String stepMaterial(int count);

  /// No description provided for @addMaterial.
  ///
  /// In ar, this message translates to:
  /// **'إضافة مادة'**
  String get addMaterial;

  /// No description provided for @stepChecklist.
  ///
  /// In ar, this message translates to:
  /// **'قائمة الفحص الوظيفي'**
  String get stepChecklist;

  /// No description provided for @openChecklist.
  ///
  /// In ar, this message translates to:
  /// **'فتح'**
  String get openChecklist;

  /// No description provided for @stepTestPhoto.
  ///
  /// In ar, this message translates to:
  /// **'صورة الاختبار ({count}/{total})'**
  String stepTestPhoto(int count, int total);

  /// No description provided for @stepAfter.
  ///
  /// In ar, this message translates to:
  /// **'صور بعد العمل ({count}/{total})'**
  String stepAfter(int count, int total);

  /// No description provided for @stepSubmit.
  ///
  /// In ar, this message translates to:
  /// **'التقديم للمراجعة'**
  String get stepSubmit;

  /// No description provided for @stepResubmit.
  ///
  /// In ar, this message translates to:
  /// **'إعادة التقديم'**
  String get stepResubmit;

  /// No description provided for @submitAction.
  ///
  /// In ar, this message translates to:
  /// **'تقديم'**
  String get submitAction;

  /// No description provided for @resubmitAction.
  ///
  /// In ar, this message translates to:
  /// **'إعادة التقديم'**
  String get resubmitAction;

  /// No description provided for @arriveWithCode.
  ///
  /// In ar, this message translates to:
  /// **'تأكيد بالرمز المكتوب'**
  String get arriveWithCode;

  /// No description provided for @arriveHint.
  ///
  /// In ar, this message translates to:
  /// **'امسح ملصق QR على العمود {code}.'**
  String arriveHint(String code);

  /// No description provided for @damagedTagHint.
  ///
  /// In ar, this message translates to:
  /// **'الملصق تالف أو غير مقروء؟ اكتب الرمز واذكر السبب.'**
  String get damagedTagHint;

  /// No description provided for @manualReasonLabel.
  ///
  /// In ar, this message translates to:
  /// **'سبب الإدخال اليدوي'**
  String get manualReasonLabel;

  /// No description provided for @manualReasonRequired.
  ///
  /// In ar, this message translates to:
  /// **'اذكر سبب إدخال الرمز يدويًا.'**
  String get manualReasonRequired;

  /// No description provided for @noteOptional.
  ///
  /// In ar, this message translates to:
  /// **'ملاحظة (اختياري)'**
  String get noteOptional;

  /// No description provided for @referenceMissing.
  ///
  /// In ar, this message translates to:
  /// **'لم تُحمَّل رموز التشخيص بعد. زامن ثم حاول مجددًا.'**
  String get referenceMissing;

  /// No description provided for @custodyHint.
  ///
  /// In ar, this message translates to:
  /// **'اختر من المواد الموجودة في عهدتك.'**
  String get custodyHint;

  /// No description provided for @custodyEmpty.
  ///
  /// In ar, this message translates to:
  /// **'لا توجد مواد في عهدتك حسب آخر مزامنة.'**
  String get custodyEmpty;

  /// No description provided for @inCustody.
  ///
  /// In ar, this message translates to:
  /// **'في العهدة: {quantity} {unit}'**
  String inCustody(String quantity, String unit);

  /// No description provided for @chooseSerials.
  ///
  /// In ar, this message translates to:
  /// **'اختر الرقم التسلسلي للقطعة المركّبة:'**
  String get chooseSerials;

  /// No description provided for @quantityLabel.
  ///
  /// In ar, this message translates to:
  /// **'الكمية ({unit})'**
  String quantityLabel(String unit);

  /// No description provided for @testMethodLabel.
  ///
  /// In ar, this message translates to:
  /// **'طريقة الاختبار'**
  String get testMethodLabel;

  /// No description provided for @notTestedReason.
  ///
  /// In ar, this message translates to:
  /// **'سبب عدم الاختبار'**
  String get notTestedReason;

  /// No description provided for @yes.
  ///
  /// In ar, this message translates to:
  /// **'نعم'**
  String get yes;

  /// No description provided for @no.
  ///
  /// In ar, this message translates to:
  /// **'لا'**
  String get no;

  /// No description provided for @submitSummary.
  ///
  /// In ar, this message translates to:
  /// **'قبل: {before} · بعد: {after} · اختبار: {test} · مواد: {materials}'**
  String submitSummary(int before, int after, int test, int materials);

  /// No description provided for @rootCauseLabel.
  ///
  /// In ar, this message translates to:
  /// **'السبب الجذري للعطل'**
  String get rootCauseLabel;

  /// No description provided for @noMaterialReasonLabel.
  ///
  /// In ar, this message translates to:
  /// **'لماذا لم تُستخدم أي مادة؟'**
  String get noMaterialReasonLabel;

  /// No description provided for @recurrentFailureTitle.
  ///
  /// In ar, this message translates to:
  /// **'عطل متكرر'**
  String get recurrentFailureTitle;

  /// No description provided for @fullInspectionRecommended.
  ///
  /// In ar, this message translates to:
  /// **'توصية: يُنصح بفحص شامل للعمود'**
  String get fullInspectionRecommended;

  /// No description provided for @recommendationRule.
  ///
  /// In ar, this message translates to:
  /// **'{count} أعطال محسوبة؛ العتبة {threshold} خلال {days} يومًا'**
  String recommendationRule(int count, int threshold, int days);

  /// No description provided for @refusalReason.
  ///
  /// In ar, this message translates to:
  /// **'{code, select, WORK_ORDER_CHANGED{تغيّر أمر العمل على الخادم بعد آخر تحديث للهاتف؛ حدّث المهمة وتحقق قبل إعادة الخطوة} WORK_ORDER_REASSIGNED{أُعيد إسناد أمر العمل لفني آخر؛ صورك وموادك محفوظة لكن هذه الخطوة لم تُطبّق} WORK_ORDER_CANCELLED{أُلغي أمر العمل؛ ينتظر العمل قرار المشرف} WORK_ORDER_CLOSED{أُغلق أمر العمل؛ ينتظر العمل قرار المشرف} BASE_VERSION_REQUIRED{حدّث المهمة ثم أعد المحاولة} GRANT_REQUIRED{لا يوجد تصريح عمل دون اتصال} GRANT_INVALID{تصريح العمل غير صالح لهذا الجهاز} GRANT_EXPIRED{أُنجز بعد انتهاء تصريح العمل دون اتصال} GRANT_REVOKED{أُنجز بعد إلغاء تصريح العمل} GRANT_WINDOW_UNPROVEN{لا يمكن إثبات وقت إنجازه (أُعيد تشغيل الهاتف دون اتصال)؛ ينتظر قرار المشرف} QUARANTINE_DISCARDED{رفضه المشرف بعد المراجعة} NOT_ASSIGNEE{أمر العمل ليس مسندًا إليك} INVALID_TRANSITION{لم تعد هذه الخطوة ممكنة في حالة أمر العمل الحالية} other{تعذّرت العملية ({code})}}'**
  String refusalReason(String code);

  /// No description provided for @commandHeld.
  ///
  /// In ar, this message translates to:
  /// **'محجوز بانتظار قرار المشرف: {command} — {reason}'**
  String commandHeld(String command, String reason);

  /// No description provided for @heldCount.
  ///
  /// In ar, this message translates to:
  /// **'{count} بانتظار قرار المشرف'**
  String heldCount(int count);

  /// No description provided for @grantExpiredTitle.
  ///
  /// In ar, this message translates to:
  /// **'اتصل بالإنترنت لمتابعة العمل'**
  String get grantExpiredTitle;

  /// No description provided for @grantExpiredBody.
  ///
  /// In ar, this message translates to:
  /// **'انتهت مدة العمل دون اتصال المسموح بها لهذا الهاتف. اتصل بالإنترنت (وسجّل الدخول من جديد إن طُلب منك) لمتابعة العمل. لا يُسجَّل عمل جديد قبل ذلك.'**
  String get grantExpiredBody;

  /// No description provided for @grantPendingItems.
  ///
  /// In ar, this message translates to:
  /// **'{count} عنصر محفوظ على الهاتف وسيُرسل عند الاتصال'**
  String grantPendingItems(int count);

  /// No description provided for @tryNow.
  ///
  /// In ar, this message translates to:
  /// **'حاول الآن'**
  String get tryNow;

  /// No description provided for @updateRequiredTitle.
  ///
  /// In ar, this message translates to:
  /// **'يلزم تحديث التطبيق'**
  String get updateRequiredTitle;

  /// No description provided for @updateRequiredBody.
  ///
  /// In ar, this message translates to:
  /// **'لم يعد الخادم يقبل هذا الإصدار من التطبيق. ثبّت الإصدار {version} أو أحدث. ما حفظته على الهاتف يبقى محفوظًا.'**
  String updateRequiredBody(String version);

  /// No description provided for @userDisabledBody.
  ///
  /// In ar, this message translates to:
  /// **'عُطّل حسابك في النظام، لذلك لم يعد الخادم يقبل شيئًا من هذا الهاتف. حذف التطبيق بياناته الخاصة المخزنة على الهاتف. لم يُمسّ أي شيء آخر على هاتفك.'**
  String get userDisabledBody;

  /// No description provided for @grantValidUntil.
  ///
  /// In ar, this message translates to:
  /// **'العمل دون اتصال مسموح حتى {time}'**
  String grantValidUntil(String time);

  /// No description provided for @grantNone.
  ///
  /// In ar, this message translates to:
  /// **'لا يوجد تصريح للعمل دون اتصال بعد: اتصل بالإنترنت'**
  String get grantNone;
}

class _AppLocalizationsDelegate extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) => <String>['ar', 'en'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'ar':
      return AppLocalizationsAr();
    case 'en':
      return AppLocalizationsEn();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
