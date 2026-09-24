import 'package:flutter/material.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

import 'app/app_controller.dart';
import 'app/mfop_app.dart';
import 'core/auth.dart';
import 'core/config.dart';
import 'core/device_info.dart';
import 'core/local_db.dart';
import 'core/secure_store.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final config = AppConfig.fromEnvironment();
  final store = KeystoreSecureStore();
  final controller = AppController(
    config: config,
    store: store,
    auth: AppAuthService(config, store),
    deviceInfo: PlatformDeviceInfo(),
    openDb: openAppDatabase,
  );
  runApp(MfopApp(controller: controller));
  await controller.start();
}

/// The encrypted database lives in the app's private support directory (never shared storage).
Future<LocalDb> openAppDatabase(SecureStore store) async {
  final dir = await getApplicationSupportDirectory();
  return LocalDb.open(store, path: p.join(dir.path, 'mfop_field.db'));
}
