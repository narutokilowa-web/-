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
  String environmentLabel(String env) {
    return 'Environment: $env';
  }

  @override
  String get languageToggle => 'العربية';

  @override
  String get privacyTitle => 'Before you start: your privacy';

  @override
  String get privacyBody =>
      'This app runs on your personal phone.\n\n• It uses the camera only to scan QR codes on street lights. It does not access your gallery, files, contacts or other apps.\n• It does not track your location. (In later versions the location is recorded only at the moment you capture work evidence, with the app open.)\n• Its data is encrypted inside the app\'s private storage and protected by a PIN.\n• The municipality cannot wipe or manage your phone. If your device is revoked, the server rejects it and the app deletes only its own data.\n• The work you record is kept in a verifiable audit log.';

  @override
  String get privacyAccept => 'I understand and agree';

  @override
  String get signInTitle => 'Sign in';

  @override
  String get signInBody => 'The phone\'s browser will open to sign in with your municipality account.';

  @override
  String get signInButton => 'Sign in';

  @override
  String get offlineMessage => 'The server cannot be reached. Check the network and try again.';

  @override
  String errorCode(String code) {
    return 'The operation failed ($code).';
  }

  @override
  String get waitingTitle => 'Waiting for device approval';

  @override
  String get waitingBody =>
      'This phone is registered. A supervisor or administrator must approve it on the web console before it can be used.';

  @override
  String get checkAgain => 'Check now';

  @override
  String deviceIdLabel(String id) {
    return 'Device id: $id';
  }

  @override
  String get rejectedTitle => 'Device registration rejected';

  @override
  String get rejectedBody => 'This phone was not approved. Contact your supervisor, then send a new request.';

  @override
  String get registerAgain => 'Send a new request';

  @override
  String get revokedTitle => 'This device was revoked';

  @override
  String get revokedBody =>
      'The municipality revoked this phone, so the server no longer accepts anything from it. The app deleted its own data stored on the phone. Nothing else on your phone was touched.';

  @override
  String get startAgain => 'Start again';

  @override
  String get pinSetupTitle => 'Choose an app PIN';

  @override
  String pinSetupBody(int minutes) {
    return '4 to 8 digits. Asked when the app returns after $minutes minutes in the background.';
  }

  @override
  String get pinLabel => 'PIN';

  @override
  String get pinConfirmLabel => 'Repeat the PIN';

  @override
  String get pinMismatch => 'The PINs do not match.';

  @override
  String get pinFormat => 'Use 4 to 8 digits.';

  @override
  String get save => 'Save';

  @override
  String get lockTitle => 'App locked';

  @override
  String get unlock => 'Unlock';

  @override
  String pinWrong(int left) {
    return 'Wrong PIN. Attempts left: $left';
  }

  @override
  String get tabTasks => 'My tasks';

  @override
  String get tabScan => 'Scan';

  @override
  String get tabMe => 'Me';

  @override
  String get online => 'Online';

  @override
  String get offline => 'Offline';

  @override
  String waitingToSend(int count) {
    return '$count waiting to send';
  }

  @override
  String lastSync(String time) {
    return 'Last sync: $time';
  }

  @override
  String get neverSynced => 'Not synced yet';

  @override
  String get noTasks => 'No tasks assigned to you right now.';

  @override
  String get notTechnician => 'The task list is for technicians. You can scan pole codes in the Scan tab.';

  @override
  String taskTitle(String code) {
    return 'Work order $code';
  }

  @override
  String get faultLabel => 'Fault';

  @override
  String get noteLabel => 'Supervisor note';

  @override
  String assignedAt(String time) {
    return 'Assigned $time';
  }

  @override
  String dueAt(String time) {
    return 'Due: $time';
  }

  @override
  String get taskNextSteps => 'On-site execution (arrival, photos, diagnosis...) is added in the next milestone.';

  @override
  String get scanHint => 'Point the camera at the QR code on the pole.';

  @override
  String get manualCodeLabel => 'Or type the printed code (e.g. 7K3M92QX.4F)';

  @override
  String get resolve => 'Show';

  @override
  String get invalidCode => 'Invalid code.';

  @override
  String get tagNotFound => 'No active asset for this code.';

  @override
  String get cameraUnavailable => 'Camera unavailable. Type the code instead.';

  @override
  String get assetTypeStreetLight => 'Street light';

  @override
  String get statusLabel => 'Status';

  @override
  String get powerSourceLabel => 'Power source';

  @override
  String get responsibleLabel => 'Responsible party';

  @override
  String get addressLabel => 'Address';

  @override
  String openFaultsLabel(int count) {
    return 'Open faults: $count';
  }

  @override
  String get myTaskOnAsset => 'You have a task on this light';

  @override
  String get openTask => 'Open task';

  @override
  String get recentEvents => 'Recent events';

  @override
  String get meTitle => 'My account';

  @override
  String rolesLabel(String roles) {
    return 'Roles: $roles';
  }

  @override
  String get syncNow => 'Send and sync now';

  @override
  String get signOut => 'Sign out';

  @override
  String get outboxEmpty => 'Nothing waiting to send.';

  @override
  String opStatus(String status) {
    String _temp0 = intl.Intl.selectLogic(status, {
      'OPERATIONAL': 'Operational',
      'FAULTY': 'Faulty',
      'UNKNOWN': 'Unknown',
      'UNDER_MAINTENANCE': 'Under maintenance',
      'other': '$status',
    });
    return '$_temp0';
  }

  @override
  String powerSourceName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {'GRID': 'Grid', 'SOLAR': 'Solar', 'HYBRID': 'Hybrid', 'other': '$value'});
    return '$_temp0';
  }

  @override
  String responsibleName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'MUNICIPALITY': 'Municipality',
      'ELECTRICITY_AUTHORITY': 'Electricity authority',
      'OTHER_PUBLIC_ENTITY': 'Other public entity',
      'SHARED': 'Shared',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String faultCategoryName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'LIGHT_OFF': 'Light off',
      'FLICKERING': 'Flickering',
      'DAY_BURNING': 'Burning by day',
      'DAMAGED_POLE': 'Damaged pole',
      'EXPOSED_WIRES': 'Exposed wires',
      'MISSING_PARTS': 'Missing parts',
      'OTHER': 'Other',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String powerAtInspectionName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'CONFIRMED_AVAILABLE': 'Power available',
      'CONFIRMED_UNAVAILABLE': 'Power unavailable',
      'UNKNOWN': 'Power: unknown',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String priorityName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'P1': 'P1 critical',
      'P2': 'P2 high',
      'P3': 'P3 normal',
      'P4': 'P4 low',
      'other': '$value',
    });
    return '$_temp0';
  }

  @override
  String workOrderStateName(String value) {
    String _temp0 = intl.Intl.selectLogic(value, {
      'ASSIGNED': 'Assigned',
      'ON_SITE': 'On site',
      'IN_PROGRESS': 'In progress',
      'PENDING_REVIEW': 'Pending review',
      'RETURNED': 'Returned',
      'other': '$value',
    });
    return '$_temp0';
  }
}
