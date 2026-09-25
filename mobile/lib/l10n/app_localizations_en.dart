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
      'This app runs on your personal phone.\n\n• It uses the camera only inside the app: to scan QR codes on street lights and to take work photos. Photos are not saved to your gallery, and the app does not access your gallery, files, contacts or other apps. Work photos are deleted from the phone once uploaded.\n• It does not track your location. (In later versions the location is recorded only at the moment you capture work evidence, with the app open.)\n• Its data is encrypted inside the app\'s private storage and protected by a PIN.\n• The municipality cannot wipe or manage your phone. If your device is revoked, the server rejects it and the app deletes only its own data.\n• The work you record is kept in a verifiable audit log.';

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

  @override
  String get fieldWorkTitle => 'Field work';

  @override
  String get takePhoto => 'Take photo';

  @override
  String get photoUnreadable => 'The photo could not be read. Take it again.';

  @override
  String evidenceKindName(String kind) {
    String _temp0 = intl.Intl.selectLogic(kind, {'BEFORE': 'Before photo', 'AFTER': 'After photo', 'TEST': 'Test photo', 'other': '$kind'});
    return '$_temp0';
  }

  @override
  String get returnedTitle => 'The work order was returned to you. Add what is needed, then resubmit:';

  @override
  String returnReasonName(String reason) {
    String _temp0 = intl.Intl.selectLogic(reason, {
      'MISSING_EVIDENCE': 'Missing photos',
      'UNCLEAR_EVIDENCE': 'Unclear photos',
      'CHECKLIST_INCOMPLETE': 'Checklist incomplete',
      'MATERIAL_QUESTION': 'Question about materials',
      'DIAGNOSIS_QUESTION': 'Question about the diagnosis',
      'other': 'Other',
    });
    return '$_temp0';
  }

  @override
  String commandRejected(String command, String error) {
    return 'Refused by the server: $command — $error';
  }

  @override
  String commandName(String type) {
    String _temp0 = intl.Intl.selectLogic(type, {
      'ArriveOnSite': 'Arrival',
      'AddEvidence': 'Photo',
      'RecordDiagnosis': 'Diagnosis',
      'StartWork': 'Start work',
      'ConsumeMaterial': 'Material',
      'RecordChecklist': 'Checklist',
      'SubmitWorkOrder': 'Submission',
      'Resubmit': 'Resubmission',
      'AcknowledgeStockIssue': 'Material receipt',
      'DisputeStockIssue': 'Material dispute',
      'DeclareStockReturn': 'Material return',
      'DeclareCoreReturn': 'Failed part',
      'other': '$type',
    });
    return '$_temp0';
  }

  @override
  String photosWaiting(int count) {
    return '$count photos waiting to upload';
  }

  @override
  String get submittedWaitingReview => 'Submitted; waiting for the supervisor\'s review.';

  @override
  String get stepArrive => 'Arrive and confirm the tag';

  @override
  String get arriveAction => 'I have arrived';

  @override
  String stepBefore(int count, int total) {
    return 'Before photos ($count/$total)';
  }

  @override
  String get stepDiagnosis => 'Diagnosis';

  @override
  String get chooseDiagnosis => 'Choose';

  @override
  String get stepStart => 'Start work';

  @override
  String get startWorkAction => 'Start';

  @override
  String get startWorkNeeds => 'Before photos and a diagnosis are needed first.';

  @override
  String stepMaterial(int count) {
    return 'Materials used ($count)';
  }

  @override
  String get addMaterial => 'Add material';

  @override
  String get stepChecklist => 'Functional-test checklist';

  @override
  String get openChecklist => 'Open';

  @override
  String stepTestPhoto(int count, int total) {
    return 'Test photo ($count/$total)';
  }

  @override
  String stepAfter(int count, int total) {
    return 'After photos ($count/$total)';
  }

  @override
  String get stepSubmit => 'Submit for review';

  @override
  String get stepResubmit => 'Resubmit';

  @override
  String get submitAction => 'Submit';

  @override
  String get resubmitAction => 'Resubmit';

  @override
  String get arriveWithCode => 'Confirm with the typed code';

  @override
  String arriveHint(String code) {
    return 'Scan the QR tag on light $code.';
  }

  @override
  String get damagedTagHint => 'Tag damaged or unreadable? Type the code and give the reason.';

  @override
  String get manualReasonLabel => 'Reason for typing the code';

  @override
  String get manualReasonRequired => 'Give the reason for typing the code.';

  @override
  String get noteOptional => 'Note (optional)';

  @override
  String get referenceMissing => 'Diagnosis codes are not loaded yet. Sync and try again.';

  @override
  String get custodyHint => 'Choose from the materials in your custody.';

  @override
  String get custodyEmpty => 'No materials in your custody as of the last sync.';

  @override
  String inCustody(String quantity, String unit) {
    return 'In custody: $quantity $unit';
  }

  @override
  String get chooseSerials => 'Choose the serial of the installed part:';

  @override
  String quantityLabel(String unit) {
    return 'Quantity ($unit)';
  }

  @override
  String get testMethodLabel => 'Test method';

  @override
  String get notTestedReason => 'Why it was not tested';

  @override
  String get yes => 'Yes';

  @override
  String get no => 'No';

  @override
  String submitSummary(int before, int after, int test, int materials) {
    return 'Before: $before · After: $after · Test: $test · Materials: $materials';
  }

  @override
  String get rootCauseLabel => 'Root cause of the fault';

  @override
  String get noMaterialReasonLabel => 'Why was no material used?';

  @override
  String get recurrentFailureTitle => 'Recurrent Failure';

  @override
  String get fullInspectionRecommended => 'Recommendation: Full inspection recommended';

  @override
  String recommendationRule(int count, int threshold, int days) {
    return '$count counted failures; threshold $threshold within $days days';
  }

  @override
  String refusalReason(String code) {
    String _temp0 = intl.Intl.selectLogic(code, {
      'WORK_ORDER_CHANGED':
          'The work order changed on the server after your last refresh; refresh the task and check before repeating this step',
      'WORK_ORDER_REASSIGNED':
          'The work order was reassigned to another technician; your photos and materials are kept, but this step was not applied',
      'WORK_ORDER_CANCELLED': 'The work order was cancelled; the work waits for the supervisor\'s decision',
      'WORK_ORDER_CLOSED': 'The work order was closed; the work waits for the supervisor\'s decision',
      'BASE_VERSION_REQUIRED': 'Refresh the task and try again',
      'GRANT_REQUIRED': 'There is no offline permission',
      'GRANT_INVALID': 'The offline permission is not valid for this phone',
      'GRANT_EXPIRED': 'Done after the offline permission ran out',
      'GRANT_REVOKED': 'Done after the offline permission was revoked',
      'GRANT_WINDOW_UNPROVEN': 'Its time cannot be proven (the phone restarted while offline); it waits for the supervisor\'s decision',
      'QUARANTINE_DISCARDED': 'Discarded by the supervisor after review',
      'NOT_ASSIGNEE': 'This work order is not assigned to you',
      'INVALID_TRANSITION': 'This step is no longer possible in the work order\'s current state',
      'CORE_RETURN_NOT_DECLARED': 'Declare the failed part of every part you replaced before submitting',
      'SERIALS_NOT_CONFIRMED': 'Confirm exactly the serials you received, or report a problem',
      'INSUFFICIENT_CUSTODY': 'More than your custody holds',
      'SERIAL_NOT_IN_CUSTODY': 'This serial is not in your custody',
      'NOT_RECEIVER': 'This issue is not addressed to you',
      'other': 'The operation failed ($code)',
    });
    return '$_temp0';
  }

  @override
  String commandHeld(String command, String reason) {
    return 'Held for the supervisor\'s decision: $command — $reason';
  }

  @override
  String heldCount(int count) {
    return '$count held for the supervisor\'s decision';
  }

  @override
  String get grantExpiredTitle => 'Connect to continue working';

  @override
  String get grantExpiredBody =>
      'This phone has reached the time it may work offline. Connect to the internet (and sign in again if asked) to continue. No new work is recorded until then.';

  @override
  String grantPendingItems(int count) {
    return '$count items are kept on the phone and will be sent when connected';
  }

  @override
  String get tryNow => 'Try now';

  @override
  String get updateRequiredTitle => 'App update required';

  @override
  String updateRequiredBody(String version) {
    return 'The server no longer accepts this app version. Install version $version or newer. What you saved on the phone stays saved.';
  }

  @override
  String get userDisabledBody =>
      'Your account was disabled, so the server no longer accepts anything from this phone. The app deleted its own data stored on the phone. Nothing else on your phone was touched.';

  @override
  String grantValidUntil(String time) {
    return 'Offline work allowed until $time';
  }

  @override
  String get grantNone => 'No offline permission yet: connect to the internet';

  @override
  String get tabStock => 'Materials';

  @override
  String stepCoreReturn(int count) {
    return 'Failed parts: $count to declare';
  }

  @override
  String get stepCoreReturnDone => 'Failed parts: declared';

  @override
  String get declareCoreAction => 'Declare failed part';

  @override
  String get coreReturnTitle => 'Failed part';

  @override
  String coreReturnHint(String part) {
    return 'You replaced $part. Bring the failed part to the warehouse, or say why it cannot be brought back. A photo is required either way.';
  }

  @override
  String coreExpectedSerial(String serial) {
    return 'Serial known on the light: $serial';
  }

  @override
  String coreInstalledSerial(String serial) {
    return 'New part installed: $serial';
  }

  @override
  String get coreWithPart => 'I have the failed part';

  @override
  String get coreUnrecoverable => 'It cannot be brought back';

  @override
  String get coreSerialLabel => 'Serial on the failed part';

  @override
  String get coreReasonLabel => 'Why can it not be brought back?';

  @override
  String get corePhotoAction => 'Photograph the failed part';

  @override
  String get corePhotoReady => 'Photo ready';

  @override
  String coreStateName(String state) {
    String _temp0 = intl.Intl.selectLogic(state, {
      'OPEN': 'To declare',
      'DECLARED_WITH_PART': 'Bring it to the warehouse',
      'DECLARED_UNRECOVERABLE': 'Unrecoverable: waits for the supervisor',
      'RECEIVED': 'Received by the warehouse',
      'WAIVED': 'Waived',
      'other': '$state',
    });
    return '$_temp0';
  }

  @override
  String get issuesToConfirmTitle => 'Material waiting for your confirmation';

  @override
  String issueFrom(String code, String name) {
    return '$code · issued by $name';
  }

  @override
  String get tickSerials => 'Tick every serial you actually received';

  @override
  String get confirmReceipt => 'Confirm receipt';

  @override
  String get reportProblem => 'Report a problem';

  @override
  String get disputeReasonLabel => 'What is wrong? (e.g. only one arrived)';

  @override
  String get send => 'Send';

  @override
  String get myCustodyTitle => 'My custody';

  @override
  String get returnUnused => 'Return unused material';

  @override
  String get returnNoteLabel => 'Note (optional)';

  @override
  String get returnsWaitingTitle => 'Returns waiting for the warehouse';

  @override
  String get coresTitle => 'Failed parts';

  @override
  String get nothingWaiting => 'Nothing waiting for you.';

  @override
  String get stockHint =>
      'Material issued to you is in your custody only after you confirm it. A return stays in transit until the warehouse receives it.';
}
