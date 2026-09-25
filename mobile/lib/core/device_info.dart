import 'package:flutter/services.dart';

class DeviceDescription {
  const DeviceDescription(this.model, this.osVersion);

  final String model;
  final String osVersion;
}

/// Model and Android version for the device registration request (shown to the approving supervisor).
abstract class DeviceInfoSource {
  Future<DeviceDescription> describe();
}

class PlatformDeviceInfo implements DeviceInfoSource {
  static const _channel = MethodChannel('mfop/device');

  @override
  Future<DeviceDescription> describe() async {
    final map = await _channel.invokeMapMethod<String, String>('describe') ?? const {};
    return DeviceDescription(map['model'] ?? 'Android', map['osVersion'] ?? 'unknown');
  }
}
