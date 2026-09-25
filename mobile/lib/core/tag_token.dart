/// Turns what the scanner read (the QR holds `<base>/t/XXXXXXXX.CC`) or what the user typed (with or without the dot,
/// any case, spaces) into the token the server resolves. The check code is verified by the server.
String? tagTokenFrom(String raw) {
  var value = raw.trim();
  if (value.isEmpty) return null;
  final uri = Uri.tryParse(value);
  if (uri != null && uri.hasScheme && uri.pathSegments.isNotEmpty) {
    final i = uri.pathSegments.indexOf('t');
    value = i >= 0 && i + 1 < uri.pathSegments.length ? uri.pathSegments[i + 1] : uri.pathSegments.last;
  }

  value = value.replaceAll(RegExp(r'[\s-]'), '').toUpperCase();
  if (!RegExp(r'^[0-9A-Z]{8}\.?[0-9A-Z]{2}$').hasMatch(value)) return null;
  return value.contains('.') ? value : '${value.substring(0, 8)}.${value.substring(8)}';
}
