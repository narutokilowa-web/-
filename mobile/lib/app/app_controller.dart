import 'dart:async';

import 'package:flutter/widgets.dart';

import '../core/api_client.dart';
import '../core/auth.dart';
import '../core/config.dart';
import '../core/device_info.dart';
import '../core/field_work.dart';
import '../core/local_db.dart';
import '../core/models.dart';
import '../core/outbox.dart';
import '../core/photo_processing.dart';
import '../core/pin.dart';
import '../core/secure_store.dart';
import '../core/sync_engine.dart';
import '../core/tag_token.dart';
import '../features/work/photo_source.dart';

/// Screens of the app (docs/lean-pilot/08 §8.2 and §8.7).
enum AppStage { starting, privacy, signedOut, waitingApproval, deviceRejected, deviceRevoked, setPin, locked, ready }

typedef DbOpener = Future<LocalDb> Function(SecureStore store);

/// Drives the whole app: first run → login → device registration → approval → PIN → tasks.
/// Security decisions are the server's; this class only reacts to them.
class AppController extends ChangeNotifier with WidgetsBindingObserver {
  AppController({
    required this.config,
    required this.store,
    required this.auth,
    required this.deviceInfo,
    required DbOpener openDb,
    ApiClient Function(Future<String?> Function() deviceId)? apiFactory,
    PinService? pin,
    DateTime Function()? clock,
    PhotoSource? photoSource,
  }) : _openDb = openDb,
       _clock = clock ?? DateTime.now,
       pin = pin ?? PinService(store),
       photoSource = photoSource ?? const CameraPhotoSource() {
    api = (apiFactory ?? (d) => ApiClient(config, auth, d))(() => store.read(SecureKeys.deviceId));
  }

  final AppConfig config;
  final SecureStore store;
  final AuthService auth;
  final DeviceInfoSource deviceInfo;
  final PinService pin;
  final DbOpener _openDb;
  final DateTime Function() _clock;
  late final ApiClient api;

  /// In-app camera (never the gallery). Replaceable in tests.
  final PhotoSource photoSource;

  LocalDb? _db;
  Outbox? outbox;
  SyncEngine? sync;
  Timer? _syncTimer;
  Timer? _approvalTimer;
  DateTime? _backgroundedAt;

  AppStage stage = AppStage.starting;
  String? message;
  SyncStatus? syncStatus;
  bool busy = false;

  /// Periodic sync while the app is open (docs/lean-pilot/08 §8.6: every 15 min in the foreground).
  static const syncInterval = Duration(minutes: 15);
  static const approvalPollInterval = Duration(seconds: 10);

  List<WorkOrderTask> get tasks => sync?.cachedTasks ?? const [];
  List<String> get roles => sync?.cachedRoles ?? const [];
  String? get userName => sync?.cachedUserName;
  bool get isTechnician => roles.contains('TECHNICIAN');

  Future<void> start() async {
    _db ??= await _openDb(store);
    outbox = Outbox(_db!, clock: _clock);
    sync = SyncEngine(api, outbox!, _db!, clock: _clock);
    await _route();
  }

  Future<void> acceptPrivacy() async {
    await store.write(SecureKeys.privacyAccepted, _clock().toUtc().toIso8601String());
    await _route();
  }

  Future<void> signIn() => _guard(() async {
    try {
      await auth.signIn();
    } on SignInCancelled {
      return;
    }

    await _route();
  });

  Future<void> refreshDeviceStatus() => _guard(_route);

  Future<void> setPin(String value) => _guard(() async {
    await pin.set(value);
    stage = AppStage.ready;
    _startSyncTimer();
    await _syncQuietly();
  });

  Future<bool> unlock(String value) async {
    if (await pin.verify(value)) {
      stage = AppStage.ready;
      notifyListeners();
      _startSyncTimer();
      unawaited(_syncQuietly());
      return true;
    }

    if (pin.exhausted) {
      // Too many wrong PINs: forget the session; signing in again needs the password and the server.
      await auth.signOut();
      await pin.clear();
      await _route();
    }

    notifyListeners();
    return false;
  }

  Future<SyncStatus?> syncNow() async {
    await _guard(_syncQuietly);
    return syncStatus;
  }

  /// Scan tab: resolve a scanned QR payload or a typed code to the asset card (online).
  Future<AssetCard?> resolveTag(String raw) async {
    final token = tagTokenFrom(raw);
    if (token == null) return null;
    return _call(() => api.resolveTag(token));
  }

  Future<WorkOrderTask> openTask(String id) => _call(() => api.task(id));

  // ---- M3: field execution (docs/lean-pilot/08 §8.4). Every action is a command in the outbox; sync sends it. ----

  FieldReference get reference => sync?.cachedReference ?? FieldReference(const [], const []);

  List<CustodyItem> get custody => sync?.cachedCustody ?? const [];

  /// The cached task (last pull), if it is still assigned to me.
  WorkOrderTask? cachedTask(String id) => tasks.where((t) => t.id == id).firstOrNull;

  WorkView workView(WorkOrderTask task) =>
      WorkView.of(cachedTask(task.id) ?? task, reference, outbox?.all() ?? const [], sync?.evidence.forWorkOrder(task.id) ?? const []);

  /// W5: the scanned tag must be this light's; a typed code needs a reason (damaged tag).
  void arrive(WorkOrderTask task, {String? scanned, String? manualCode, String? manualReason}) => _enqueue('ArriveOnSite', {
    'workOrderId': task.id,
    'scannedToken': scanned,
    // A typed code is normalised like a scan (a pasted label URL, lower case, missing dot) → the tag token.
    'manualCode': manualCode == null ? null : (tagTokenFrom(manualCode) ?? manualCode.trim().toUpperCase()),
    'manualReason': manualReason?.trim(),
  });

  /// Announces the photo (hash first, docs/lean-pilot/06 §6.2) and keeps its bytes until the server has them.
  void addPhoto(WorkOrderTask task, String kind, ProcessedPhoto photo, DateTime capturedAt) {
    final evidenceId = Outbox.newId();
    final commandId = outbox!.enqueue('AddEvidence', {
      'workOrderId': task.id,
      'evidenceId': evidenceId,
      'kind': kind,
      'sha256': photo.sha256,
      'sizeBytes': photo.bytes.length,
      'mime': 'image/jpeg',
      'capturedAt': capturedAt.toUtc().toIso8601String(),
    });
    sync!.evidence.add(
      evidenceId: evidenceId,
      workOrderId: task.id,
      kind: kind,
      sha256: photo.sha256,
      bytes: photo.bytes,
      capturedAt: capturedAt,
      commandId: commandId,
    );
    _afterEnqueue();
  }

  void recordDiagnosis(WorkOrderTask task, String code, String? note) =>
      _enqueue('RecordDiagnosis', {'workOrderId': task.id, 'code': code, 'note': (note?.trim().isEmpty ?? true) ? null : note!.trim()});

  void startWork(WorkOrderTask task) => _enqueue('StartWork', {'workOrderId': task.id});

  void consume(WorkOrderTask task, CustodyItem item, double quantity, List<String> serials) => _enqueue('ConsumeMaterial', {
    'workOrderId': task.id,
    'movementId': Outbox.newId(),
    'itemId': item.itemId,
    'quantity': quantity,
    'serials': serials.isEmpty ? null : serials,
  });

  void recordChecklist(WorkOrderTask task, String templateId, String testMethod, Map<String, String> answers, String? notTestedReason) =>
      _enqueue('RecordChecklist', {
        'workOrderId': task.id,
        'checklistId': Outbox.newId(),
        'templateId': templateId,
        'testMethod': testMethod,
        'notTestedReason': notTestedReason,
        'answers': answers,
      });

  /// W9 (first submission) or W12 (after a return: additions only).
  void submit(WorkOrderTask task, {required String rootCause, String? noMaterialReason, required bool resubmit}) =>
      _enqueue(resubmit ? 'Resubmit' : 'SubmitWorkOrder', {
        'workOrderId': task.id,
        'rootCauseNote': rootCause.trim(),
        'noMaterialReason': (noMaterialReason?.trim().isEmpty ?? true) ? null : noMaterialReason!.trim(),
      });

  void _enqueue(String type, Map<String, dynamic> payload) {
    outbox!.enqueue(type, payload);
    _afterEnqueue();
  }

  /// Queued: show it at once, then try to send (offline: it waits in the outbox).
  void _afterEnqueue() {
    syncStatus = sync?.status;
    notifyListeners();
    unawaited(_syncQuietly());
  }

  Future<void> signOut() async {
    _stopTimers();
    await auth.signOut();
    await pin.clear();
    await _route();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) {
      _backgroundedAt ??= _clock();
    } else if (state == AppLifecycleState.resumed) {
      final away = _backgroundedAt == null ? Duration.zero : _clock().difference(_backgroundedAt!);
      _backgroundedAt = null;
      if (stage == AppStage.ready && away >= Duration(minutes: config.appLockMinutes)) {
        lock();
      } else if (stage == AppStage.ready) {
        unawaited(_syncQuietly());
      } else if (stage == AppStage.waitingApproval) {
        unawaited(refreshDeviceStatus());
      }
    }
  }

  void lock() {
    _stopTimers();
    stage = AppStage.locked;
    notifyListeners();
  }

  /// Decides the screen from local state and the server's view of this device.
  Future<void> _route() async {
    message = null;
    _approvalTimer?.cancel();
    if (await store.read(SecureKeys.privacyAccepted) == null) {
      return _go(AppStage.privacy);
    }

    if (!await auth.hasSession()) {
      return _go(AppStage.signedOut);
    }

    try {
      var deviceId = await store.read(SecureKeys.deviceId);
      if (deviceId == null) {
        final d = await deviceInfo.describe();
        final registered = await api.registerDevice(model: d.model, osVersion: d.osVersion);
        deviceId = registered['deviceId'] as String;
        await store.write(SecureKeys.deviceId, deviceId);
      }

      final status = await api.deviceStatus();
      switch (status) {
        case 'PENDING_APPROVAL':
          _approvalTimer = Timer.periodic(approvalPollInterval, (_) => unawaited(refreshDeviceStatus()));
          return _go(AppStage.waitingApproval);
        case 'REJECTED':
          await store.delete(SecureKeys.deviceId);
          return _go(AppStage.deviceRejected);
        case 'REVOKED':
          return _wipeOwnData();
      }
    } on OfflineException {
      // Offline: continue with what is already on the phone (offline-first); the server re-checks on the next sync.
      if (await store.read(SecureKeys.deviceId) == null) {
        message = 'offline';
        return _go(AppStage.signedOut);
      }
    } on NotSignedInException {
      return _go(AppStage.signedOut);
    } on DeviceBlockedException {
      return _wipeOwnData();
    }

    if (!await pin.isSet()) return _go(AppStage.setPin);
    if (stage == AppStage.ready) return notifyListeners();
    return _go(AppStage.locked);
  }

  Future<void> _syncQuietly() async {
    if (sync == null) return;
    try {
      syncStatus = await sync!.syncNow();
    } on DeviceBlockedException {
      await _wipeOwnData();
    } on NotSignedInException {
      _stopTimers();
      await _route();
    } on ApiException catch (e) {
      message = e.code;
    }

    notifyListeners();
  }

  Future<T> _call<T>(Future<T> Function() action) async {
    try {
      return await action();
    } on DeviceBlockedException {
      await _wipeOwnData();
      rethrow;
    } on NotSignedInException {
      await _route();
      rethrow;
    }
  }

  /// Revoked device (docs/lean-pilot/05 §5.6, best-effort hygiene): the app discards ITS OWN data only —
  /// its database file, its database key, tokens, device id and PIN. Nothing else on the phone is read or touched.
  Future<void> _wipeOwnData() async {
    _stopTimers();
    _db?.destroy();
    _db = null;
    await auth.signOut();
    await store.deleteAll();
    _db = await _openDb(store); // fresh, empty database with a new key, for a possible new start
    outbox = Outbox(_db!, clock: _clock);
    sync = SyncEngine(api, outbox!, _db!, clock: _clock);
    syncStatus = null;
    _go(AppStage.deviceRevoked);
  }

  Future<void> _guard(Future<void> Function() action) async {
    if (busy) return;
    busy = true;
    notifyListeners();
    try {
      await action();
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  void _go(AppStage next) {
    stage = next;
    notifyListeners();
  }

  void _startSyncTimer() {
    _syncTimer?.cancel();
    _syncTimer = Timer.periodic(syncInterval, (_) => unawaited(_syncQuietly()));
  }

  void _stopTimers() {
    _syncTimer?.cancel();
    _approvalTimer?.cancel();
  }

  @override
  void dispose() {
    _stopTimers();
    super.dispose();
  }
}
