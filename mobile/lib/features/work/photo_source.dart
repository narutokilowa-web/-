import 'dart:io';
import 'dart:typed_data';

import 'package:camera/camera.dart';
import 'package:flutter/material.dart';

import '../../core/secure_window.dart';
import '../../l10n/app_localizations.dart';

/// Where evidence photos come from. Production: the in-app camera only (never the gallery, docs/lean-pilot/06 §6.2).
abstract class PhotoSource {
  /// Raw image bytes, or null if the technician cancelled.
  Future<Uint8List?> capture(BuildContext context, String title);
}

class CameraPhotoSource implements PhotoSource {
  const CameraPhotoSource();

  @override
  Future<Uint8List?> capture(BuildContext context, String title) =>
      Navigator.of(context).push<Uint8List>(MaterialPageRoute(builder: (_) => CameraScreen(title: title)));
}

/// Full-screen in-app camera. The temporary file written by the camera plugin (in the app's own cache) is read and
/// deleted immediately; nothing is written to the gallery or shared storage.
class CameraScreen extends StatefulWidget {
  const CameraScreen({super.key, required this.title});

  final String title;

  @override
  State<CameraScreen> createState() => _CameraScreenState();
}

class _CameraScreenState extends State<CameraScreen> {
  CameraController? _camera;
  String? _error;
  bool _taking = false;

  @override
  void initState() {
    super.initState();
    SecureWindow.set(true);
    _open();
  }

  Future<void> _open() async {
    try {
      final cameras = await availableCameras();
      if (cameras.isEmpty) throw CameraException('NO_CAMERA', null);
      final back = cameras.firstWhere((c) => c.lensDirection == CameraLensDirection.back, orElse: () => cameras.first);
      final controller = CameraController(back, ResolutionPreset.high, enableAudio: false, imageFormatGroup: ImageFormatGroup.jpeg);
      await controller.initialize();
      if (!mounted) return controller.dispose();
      setState(() => _camera = controller);
    } on CameraException catch (e) {
      if (mounted) setState(() => _error = e.code);
    }
  }

  Future<void> _take() async {
    final camera = _camera;
    if (camera == null || _taking) return;
    setState(() => _taking = true);
    try {
      final shot = await camera.takePicture();
      final bytes = await shot.readAsBytes();
      try {
        await File(shot.path).delete();
      } on FileSystemException {
        // already gone
      }

      if (mounted) Navigator.of(context).pop(bytes);
    } on CameraException catch (e) {
      if (mounted) setState(() => _error = e.code);
    } finally {
      if (mounted) setState(() => _taking = false);
    }
  }

  @override
  void dispose() {
    _camera?.dispose();
    SecureWindow.set(false);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final camera = _camera;
    return Scaffold(
      appBar: AppBar(title: Text(widget.title)),
      backgroundColor: Colors.black,
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: Center(
                child: _error != null
                    ? Text(l10n.cameraUnavailable, style: const TextStyle(color: Colors.white))
                    : camera == null
                    ? const CircularProgressIndicator()
                    : CameraPreview(camera),
              ),
            ),
            Padding(
              padding: const EdgeInsets.all(16),
              child: FilledButton.icon(
                key: const Key('camera-shutter'),
                onPressed: camera == null || _taking ? null : _take,
                icon: const Icon(Icons.camera_alt),
                label: Text(l10n.takePhoto),
                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
