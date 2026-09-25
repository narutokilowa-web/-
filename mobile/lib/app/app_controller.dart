import 'dart:async';

import 'package:flutter/widgets.dart';

import '../core/api_client.dart';
import '../core/auth.dart';
import '../core/config.dart';
import '../core/device_info.dart';
import '../core/field_work.dart';
import '../core/device_trust.dart';
import '../core/local_db.dart';
import '../core/models.dart';
import '../core/offline_grant.dart';
import '../core/outbox.dart';
import '../core/photo_processing.dart';
import '../core/pin.dart';
import '../core/secure_store.dart';
import '../core/sync_engine.dart';
import '../core/tag_token.dart';
import '../features/work/photo_source.dart';

/// Screens of the app (docs/lean-pilot/08 §8.2 and §8.7).
/// M4: grantExpired — the offline grant ran out (or none yet): no new work until the phone reconnects (05 §5.5);
/// updateRequired — this app version is below the server's minimum.
enum AppStage { starting, privacy, signedOut, waitingApproval, deviceRejected, deviceRevoked, setPin, locked, ready, grantExpired, updateRequired }

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
  GrantStore? grants;
  Timer? _syncTimer;
  Timer? _approvalTimer;
  Timer? _retryTimer;
  Timer? _grantTimer;
  Duration? _retryDelay;
  DateTime? _backgroundedAt;
  Future<void> _serial = Future.value();

  /// Set with [AppStage.updateRequired]: the oldest app version the server accepts.
  String? minAppVersion;

  /// Why the phone was blocked (DEVICE_REVOKED or USER_DISABLED), for the revoked screen.
  String? blockedCode;

  DeviceTrust get trust => api.trust;

  AppStage stage = AppStage.starting;
  String? message;
  SyncStatus? syncStatus;
  bool busy = false;

  /// Periodic sync while the app is open (docs/lean-pilot/08 §8.6: every 15 min in the foreground).
  static const syncInterval = Duration(minutes: 15);
  static const approvalPollInterval = Duration(seconds: 10);

  /// Reconnect back-off after a sync that found no network: 15 s, 30 s, 1 min … at most the normal interval.
  static const firstRetry = Duration(seconds: 15);

  /// How often the app re-checks, on its monotonic clock, whether the offline grant has run out while it is open.
  static const grantCheckInterval = Duration(minutes: 1);

  /// App lock after this long in the background: the server policy once known, the build default before that.
  int get appLockMinutes => grants?.policy?.appLockMinutes ?? config.appLockMinutes;

  List<WorkOrderTask> get tasks => sync?.cachedTasks ?? const [];
  List<String> get roles => sync?.cachedRoles ?? const [];
  String? get userName => sync?.cachedUserName;
  bool get isTechnician => roles.contains('TECHNICIAN');

  Future<void> start() async {
    _db ??= await _openDb(store);
    _openStores();
    await _route();
  }

  void _openStores() {
    outbox = Outbox(_db!, clock: _clock);
    grants = GrantStore(_db!);
    api.onServerTime = (serverTime, at) => grants?.observeServerTime(serverTime, at);
    sync = SyncEngine(api, outbox!, _db!, clock: _clock, grants: grants!, deviceId: () => store.read(SecureKeys.deviceId));
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

  /// Locked screens (grant expired / update required): try again now.
  Future<void> retryConnection() => _guard(() async {
    if (stage == AppStage.updateRequired) {
      await _route();
      return;
    }

    await _syncQuietly();
  });

  /// True while the offline grant is valid on the phone's monotonic clock (the server re-checks every command).
  Future<bool> grantValid() async => !(grants?.expired(await trust.now(), _clock()) ?? true);

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
  Future<void> arrive(WorkOrderTask task, {String? scanned, String? manualCode, String? manualReason}) => _enqueue(task, 'ArriveOnSite', {
    'workOrderId': task.id,
    'scannedToken': scanned,
    // A typed code is normalised like a scan (a pasted label URL, lower case, missing dot) → the tag token.
    'manualCode': manualCode == null ? null : (tagTokenFrom(manualCode) ?? manualCode.trim().toUpperCase()),
    'manualReason': manualReason?.trim(),
  });

  /// Announces the photo (hash first, docs/lean-pilot/06 §6.2) and keeps its bytes until the server has them.
  Future<void> addPhoto(WorkOrderTask task, String kind, ProcessedPhoto photo, DateTime capturedAt) {
    final evidenceId = Outbox.newId();
    return _enqueue(
      task,
      'AddEvidence',
      {
        'workOrderId': task.id,
        'evidenceId': evidenceId,
        'kind': kind,
        'sha256': photo.sha256,
        'sizeBytes': photo.bytes.length,
        'mime': 'image/jpeg',
        'capturedAt': capturedAt.toUtc().toIso8601String(),
      },
      after: (commandId) => sync!.evidence.add(
        evidenceId: evidenceId,
        workOrderId: task.id,
        kind: kind,
        sha256: photo.sha256,
        bytes: photo.bytes,
        capturedAt: capturedAt,
        commandId: commandId,
      ),
    );
  }

  Future<void> recordDiagnosis(WorkOrderTask task, String code, String? note) =>
      _enqueue(task, 'RecordDiagnosis', {'workOrderId': task.id, 'code': code, 'note': (note?.trim().isEmpty ?? true) ? null : note!.trim()});

  Future<void> startWork(WorkOrderTask task) => _enqueue(task, 'StartWork', {'workOrderId': task.id});

  Future<void> consume(WorkOrderTask task, CustodyItem item, double quantity, List<String> serials) => _enqueue(task, 'ConsumeMaterial', {
    'workOrderId': task.id,
    'movementId': Outbox.newId(),
    'itemId': item.itemId,
    'quantity': quantity,
    'serials': serials.isEmpty ? null : serials,
  });

  Future<void> recordChecklist(WorkOrderTask task, String templateId, String testMethod, Map<String, String> answers, String? notTestedReason) =>
      _enqueue(task, 'RecordChecklist', {
        'workOrderId': task.id,
        'checklistId': Outbox.newId(),
        'templateId': templateId,
        'testMethod': testMethod,
        'notTestedReason': notTestedReason,
        'answers': answers,
      });

  /// W9 (first submission) or W12 (after a return: additions only).
  Future<void> submit(WorkOrderTask task, {required String rootCause, String? noMaterialReason, required bool resubmit}) =>
      _enqueue(task, resubmit ? 'Resubmit' : 'SubmitWorkOrder', {
        'workOrderId': task.id,
        'rootCauseNote': rootCause.trim(),
        'noMaterialReason': (noMaterialReason?.trim().isEmpty ?? true) ? null : noMaterialReason!.trim(),
      });

  /// Records a work action in the outbox with its context: the offline grant it is made under, the monotonic clock and boot
  /// (trusted time), and the work-order version the technician is looking at. Without a valid grant nothing is recorded and
  /// the app locks (docs/lean-pilot/05 §5.5). Actions are recorded one after the other, in the order they were made.
  Future<void> _enqueue(WorkOrderTask task, String type, Map<String, dynamic> payload, {void Function(String commandId)? after}) {
    final run = _serial.then((_) async {
      final now = await trust.now();
      final grant = grants?.current;
      if (grant == null || grants!.expired(now, _clock())) {
        message = 'GRANT_EXPIRED';
        _lockForGrant();
        return;
      }

      final cached = cachedTask(task.id);
      final seen = cached == null || task.eventSeq > cached.eventSeq ? task.eventSeq : cached.eventSeq;
      final id = outbox!.enqueue(type, payload, CommandContext(grantId: grant.grantId, elapsedMs: now.elapsedMs, bootId: now.bootId, baseVersion: seen));
      after?.call(id);
      _afterEnqueue();
    });
    _serial = run.catchError((Object _) {});
    return run;
  }

  void _lockForGrant() {
    if (stage != AppStage.ready) return;
    _stopTimers(keepRetry: true);
    _go(AppStage.grantExpired);
  }

  /// While open, the app watches the grant on its monotonic clock and locks new work when it runs out.
  Future<void> _checkGrant() async {
    if (stage == AppStage.ready && !await grantValid()) _lockForGrant();
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
      if ((stage == AppStage.ready || stage == AppStage.grantExpired) && away >= Duration(minutes: appLockMinutes)) {
        lock();
      } else if (stage == AppStage.ready || stage == AppStage.grantExpired) {
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
    minAppVersion = null;
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
          return _wipeOwnData('DEVICE_REVOKED');
      }
    } on OfflineException {
      // Offline: continue with what is already on the phone (offline-first); the server re-checks on the next sync.
      if (await store.read(SecureKeys.deviceId) == null) {
        message = 'offline';
        return _go(AppStage.signedOut);
      }
    } on NotSignedInException {
      return _go(AppStage.signedOut);
    } on UpgradeRequiredException catch (e) {
      minAppVersion = e.minAppVersion;
      return _go(AppStage.updateRequired);
    } on DeviceBlockedException catch (e) {
      return _wipeOwnData(e.code);
    }

    if (!await pin.isSet()) return _go(AppStage.setPin);
    if (stage == AppStage.ready) return notifyListeners();
    return _go(AppStage.locked);
  }

  Future<void> _syncQuietly() async {
    if (sync == null) return;
    try {
      syncStatus = await sync!.syncNow();
      if (syncStatus!.online) {
        _retryDelay = null;
        _retryTimer?.cancel();
        if (stage == AppStage.grantExpired && await grantValid()) {
          message = null;
          stage = AppStage.ready;
          _startSyncTimer();
        }
      } else {
        _scheduleRetry();
      }
    } on DeviceBlockedException catch (e) {
      await _wipeOwnData(e.code);
    } on NotSignedInException {
      _stopTimers();
      await _route();
    } on UpgradeRequiredException catch (e) {
      _stopTimers();
      minAppVersion = e.minAppVersion;
      stage = AppStage.updateRequired;
    } on GrantRejectedException catch (e) {
      message = 'GRANT_${e.reason.name.toUpperCase()}';
    } on ApiException catch (e) {
      message = e.code;
    }

    await _checkGrant();
    notifyListeners();
  }

  /// The current reconnect delay (null while online).
  Duration? get retryDelay => _retryDelay;

  /// Offline: try again with a growing delay (the network may come back at any time), never faster than 15 s.
  void _scheduleRetry() {
    final next = _retryDelay == null ? firstRetry : _retryDelay! * 2;
    _retryDelay = next > syncInterval ? syncInterval : next;
    _retryTimer?.cancel();
    _retryTimer = Timer(_retryDelay!, () => unawaited(_syncQuietly()));
  }

  Future<T> _call<T>(Future<T> Function() action) async {
    try {
      return await action();
    } on DeviceBlockedException catch (e) {
      await _wipeOwnData(e.code);
      rethrow;
    } on NotSignedInException {
      await _route();
      rethrow;
    }
  }

  /// Revoked device or disabled employee (docs/lean-pilot/05 §5.6, best-effort hygiene): the app discards ITS OWN data
  /// only — its database file, its database key, its device signing key, tokens, device id and PIN. Nothing else on the
  /// phone is read or touched.
  Future<void> _wipeOwnData([String? code]) async {
    _stopTimers();
    _db?.destroy();
    _db = null;
    await auth.signOut();
    await store.deleteAll();
    try {
      await trust.deleteKey();
    } on Exception {
      // best effort: the server no longer accepts this key anyway
    }

    _db = await _openDb(store); // fresh, empty database with a new key, for a possible new start
    _openStores();
    syncStatus = null;
    blockedCode = code;
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
    _grantTimer?.cancel();
    _grantTimer = Timer.periodic(grantCheckInterval, (_) => unawaited(_checkGrant().then((_) => notifyListeners())));
  }

  void _stopTimers({bool keepRetry = false}) {
    _syncTimer?.cancel();
    _approvalTimer?.cancel();
    _grantTimer?.cancel();
    if (!keepRetry) _retryTimer?.cancel();
  }

  @override
  void dispose() {
    _stopTimers();
    super.dispose();
  }
}
