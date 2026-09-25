import 'package:flutter/services.dart';

/// FLAG_SECURE while evidence is on screen (no screenshots; blank in recent apps). Android only; a no-op elsewhere.
class SecureWindow {
  static const _channel = MethodChannel('mfop/secure');

  static Future<void> set(bool secure) async {
    try {
      await _channel.invokeMethod<void>('set', secure);
    } on MissingPluginException {
      // widget tests / non-Android hosts
    } on PlatformException {
      // best effort
    }
  }
}
