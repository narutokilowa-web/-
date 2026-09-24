import 'dart:async';

import 'package:flutter/widgets.dart';

import '../core/api_client.dart';
import '../core/auth.dart';
import '../core/config.dart';
import '../core/device_info.dart';
import '../core/local_db.dart';
import '../core/models.dart';
import '../core/outbox.dart';
import '../core/pin.dart';
import '../core/secure_store.dart';
import '../core/sync_engine.dart';
import '../core/tag_token.dart';

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
  }) : _openDb = openDb,
       _clock = clock ?? DateTime.now,
       pin = pin ?? PinService(store) {
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
