
import 'package:crypto/crypto.dart';
import 'package:flutter/foundation.dart';
import 'package:image/image.dart' as img;

/// A photo ready to be announced: re-encoded JPEG bytes and their SHA-256 (docs/lean-pilot/06 §6.2).
class ProcessedPhoto {
  ProcessedPhoto(this.bytes, this.sha256, this.width, this.height);

  final Uint8List bytes;
  final String sha256;
  final int width;
  final int height;
}

class PhotoRequest {
  PhotoRequest(this.raw, this.stamp, {this.maxSide = 2048, this.quality = 80});

  final Uint8List raw;

  /// Burned into the image (work-order code, kind, capture time): makes every photo unique and readable in review.
  final String stamp;
  final int maxSide;
  final int quality;
}

/// On-device processing of a camera photo: orientation applied, longest side ≤ [PhotoRequest.maxSide], a text stamp,
/// and a fresh JPEG with **no EXIF** (no GPS, no device serials). The hash is computed on the final bytes.
ProcessedPhoto processPhoto(PhotoRequest request) {
  final decoded = img.decodeImage(request.raw);
  if (decoded == null) throw const FormatException('not an image');
  var image = img.bakeOrientation(decoded);
  final longest = image.width > image.height ? image.width : image.height;
  if (longest > request.maxSide) {
    image = image.width >= image.height ? img.copyResize(image, width: request.maxSide) : img.copyResize(image, height: request.maxSide);
  }

  final font = image.width >= 1200 ? img.arial48 : (image.width >= 600 ? img.arial24 : img.arial14);
  final bandHeight = font.lineHeight + 12;
  img.fillRect(image, x1: 0, y1: image.height - bandHeight, x2: image.width, y2: image.height, color: img.ColorRgba8(0, 0, 0, 160));
  img.drawString(image, request.stamp, font: font, x: 8, y: image.height - bandHeight + 6, color: img.ColorRgb8(255, 255, 255));

  image.exif = img.ExifData();
  final bytes = img.encodeJpg(image, quality: request.quality);
  return ProcessedPhoto(bytes, sha256.convert(bytes).toString(), image.width, image.height);
}

/// Runs [processPhoto] off the UI thread.
Future<ProcessedPhoto> processPhotoInBackground(PhotoRequest request) => compute(processPhoto, request);
