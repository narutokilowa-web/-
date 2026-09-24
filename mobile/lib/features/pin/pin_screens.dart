import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../app/app_controller.dart';
import '../../core/pin.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';

TextField _pinField(TextEditingController c, String label, Key key) => TextField(
  key: key,
  controller: c,
  obscureText: true,
  keyboardType: TextInputType.number,
  maxLength: 8,
  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
  decoration: InputDecoration(labelText: label),
);

class PinSetupScreen extends StatefulWidget {
  const PinSetupScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<PinSetupScreen> createState() => _PinSetupScreenState();
}

class _PinSetupScreenState extends State<PinSetupScreen> {
  final _pin = TextEditingController();
  final _confirm = TextEditingController();
  String? _error;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.pinSetupTitle,
      primary: FilledButton(
        key: const Key('pin-save'),
        onPressed: () async {
          if (!PinService.isValidFormat(_pin.text)) return setState(() => _error = l10n.pinFormat);
          if (_pin.text != _confirm.text) return setState(() => _error = l10n.pinMismatch);
          await widget.controller.setPin(_pin.text);
        },
        child: Text(l10n.save),
      ),
      children: [
        Text(l10n.pinSetupBody(widget.controller.config.appLockMinutes)),
        _pinField(_pin, l10n.pinLabel, const Key('pin-new')),
        _pinField(_confirm, l10n.pinConfirmLabel, const Key('pin-confirm')),
        if (_error != null)
          Text(
            _error!,
            key: const Key('pin-error'),
            style: const TextStyle(color: Color(0xFFB42318)),
          ),
      ],
    );
  }
}

class PinLockScreen extends StatefulWidget {
  const PinLockScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<PinLockScreen> createState() => _PinLockScreenState();
}

class _PinLockScreenState extends State<PinLockScreen> {
  final _pin = TextEditingController();
  bool _wrong = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.lockTitle,
      primary: FilledButton(
        key: const Key('pin-unlock'),
        onPressed: () async {
          final ok = await widget.controller.unlock(_pin.text);
          if (!ok && mounted) setState(() => _wrong = true);
          _pin.clear();
        },
        child: Text(l10n.unlock),
      ),
      children: [
        const Icon(Icons.lock, size: 64),
        _pinField(_pin, l10n.pinLabel, const Key('pin-entry')),
        if (_wrong)
          Text(
            l10n.pinWrong(PinService.maxAttempts - widget.controller.pin.failures),
            key: const Key('pin-wrong'),
            style: const TextStyle(color: Color(0xFFB42318)),
          ),
      ],
    );
  }
}
