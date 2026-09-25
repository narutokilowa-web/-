import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../app/app_controller.dart';
import '../../core/field_work.dart';
import '../../core/models.dart';
import '../../core/photo_processing.dart';
import '../../core/tag_token.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';

bool _arabic(BuildContext context) => Localizations.localeOf(context).languageCode == 'ar';

/// ASCII stamp burned into each photo (bitmap font): work order, kind and UTC capture time.
String photoStamp(String workOrderCode, String kind, DateTime at) {
  final u = at.toUtc();
  String two(int v) => v.toString().padLeft(2, '0');
  return '$workOrderCode $kind ${u.year}-${two(u.month)}-${two(u.day)} ${two(u.hour)}:${two(u.minute)}:${two(u.second)}Z';
}

/// The technician's steps on one work order (docs/lean-pilot/08 §8.4): arrive → before photos → diagnosis → start →
/// material → checklist + test photo → after photos → submit. Each step is a queued command; the server re-checks all.
class WorkSteps extends StatefulWidget {
  const WorkSteps({super.key, required this.controller, required this.task, this.showCamera = true});

  final AppController controller;
  final WorkOrderTask task;
  final bool showCamera;

  @override
  State<WorkSteps> createState() => _WorkStepsState();
}

class _WorkStepsState extends State<WorkSteps> {
  bool _processing = false;

  AppController get c => widget.controller;

  Future<void> _photo(String kind) async {
    final l10n = AppLocalizations.of(context);
    final raw = await c.photoSource.capture(context, l10n.evidenceKindName(kind));
    if (raw == null || !mounted) return;
    setState(() => _processing = true);
    try {
      final now = DateTime.now();
      final photo = await processPhotoInBackground(PhotoRequest(raw, photoStamp(widget.task.code, kind, now)));
      c.addPhoto(widget.task, kind, photo, now);
    } on FormatException {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.photoUnreadable)));
    } finally {
      if (mounted) setState(() => _processing = false);
    }
  }

  String _diagnosisName(String code) {
    final d = c.reference.diagnosisCodes.where((x) => x.code == code).firstOrNull;
    if (d == null) return code;
    return _arabic(context) ? d.nameAr : d.nameEn;
  }

  Future<void> _open(Widget screen) => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => screen));

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListenableBuilder(
      listenable: c,
      builder: (context, _) {
        final v = c.workView(widget.task);
        final r = v.requirements;
        final task = v.task;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (v.isReturned)
              Card(
                key: const Key('returned-banner'),
                color: const Color(0xFFFDF1D8),
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(l10n.returnedTitle, style: const TextStyle(fontWeight: FontWeight.bold)),
                      for (final reason in task.progress?.lastReturnReasons ?? const <String>[]) Text('• ${l10n.returnReasonName(reason)}'),
                      if (task.progress?.lastReturnNote != null) Text(task.progress!.lastReturnNote!),
                    ],
                  ),
                ),
              ),
            for (final rej in v.rejections.reversed.take(3))
              Text(
                l10n.commandRejected(l10n.commandName(rej.commandType), l10n.errorCode(rej.code)),
                key: Key('rejected-${rej.commandType}'),
                style: const TextStyle(color: Color(0xFFB42318)),
              ),
            if (v.photosWaiting > 0) Text(l10n.photosWaiting(v.photosWaiting), key: const Key('photos-waiting')),
            if (_processing) const LinearProgressIndicator(),
            if (v.waitingForReview)
              Card(
                key: const Key('submitted-banner'),
                color: const Color(0xFFE3F3E6),
                child: Padding(padding: const EdgeInsets.all(12), child: Text(l10n.submittedWaitingReview)),
              ),
            _Step(
              key: const Key('step-arrive'),
              done: !v.canArrive || (task.progress?.arrivals ?? 0) > 0,
              title: l10n.stepArrive,
              action: v.canArrive ? l10n.arriveAction : null,
              onAction: () => _open(ArriveScreen(controller: c, task: task, showCamera: widget.showCamera)),
            ),
            _Step(
              key: const Key('step-before'),
              done: v.beforeDone,
              title: l10n.stepBefore(v.beforePhotos, r.beforeFirstArrival),
              action: v.canTakeBefore ? l10n.takePhoto : null,
              onAction: _processing ? null : () => _photo('BEFORE'),
            ),
            _Step(
              key: const Key('step-diagnosis'),
              done: v.diagnosisCode != null,
              title: v.diagnosisCode == null ? l10n.stepDiagnosis : '${l10n.stepDiagnosis}: ${_diagnosisName(v.diagnosisCode!)}',
              action: v.canDiagnose ? l10n.chooseDiagnosis : null,
              onAction: () => _open(DiagnosisScreen(controller: c, task: task)),
            ),
            _Step(
              key: const Key('step-start'),
              done: v.inProgress || v.isReturned || v.waitingForReview,
              title: l10n.stepStart,
              action: v.canStart ? l10n.startWorkAction : null,
              hint: v.onSite && !v.canStart ? l10n.startWorkNeeds : null,
              onAction: () => c.startWork(task),
            ),
            _Step(
              key: const Key('step-material'),
              done: v.materials > 0,
              title: l10n.stepMaterial(v.materials),
              action: v.canRecordWork ? l10n.addMaterial : null,
              onAction: () => _open(MaterialScreen(controller: c, task: task)),
            ),
            _Step(
              key: const Key('step-checklist'),
              done: v.checklistRecorded,
              title: l10n.stepChecklist,
              action: v.canRecordWork && v.template != null ? l10n.openChecklist : null,
              onAction: () => _open(ChecklistScreen(controller: c, task: task, template: v.template!)),
            ),
            _Step(
              key: const Key('step-test-photo'),
              done: v.testDone,
              title: l10n.stepTestPhoto(v.testPhotos, r.test),
              action: v.canRecordWork ? l10n.takePhoto : null,
              onAction: _processing ? null : () => _photo('TEST'),
            ),
            _Step(
              key: const Key('step-after'),
              done: v.afterDone,
              title: l10n.stepAfter(v.afterPhotos, r.after),
              action: v.canRecordWork ? l10n.takePhoto : null,
              onAction: _processing ? null : () => _photo('AFTER'),
            ),
            _Step(
              key: const Key('step-submit'),
              done: v.waitingForReview,
              title: v.isReturned ? l10n.stepResubmit : l10n.stepSubmit,
              action: v.readyToSubmit ? (v.isReturned ? l10n.resubmitAction : l10n.submitAction) : null,
              onAction: () => _open(SubmitScreen(controller: c, task: task, view: v)),
            ),
          ],
        );
      },
    );
  }
}

class _Step extends StatelessWidget {
  const _Step({super.key, required this.done, required this.title, this.action, this.onAction, this.hint});

  final bool done;
  final String title;
  final String? action;
  final VoidCallback? onAction;
  final String? hint;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(done ? Icons.check_circle : Icons.radio_button_unchecked, color: done ? const Color(0xFF1B5E20) : null),
              const SizedBox(width: 12),
              Expanded(child: Text(title, style: Theme.of(context).textTheme.titleSmall)),
            ],
          ),
          if (hint != null) Padding(padding: const EdgeInsets.only(top: 4), child: Text(hint!, style: Theme.of(context).textTheme.bodySmall)),
          if (action != null) ...[
            const SizedBox(height: 8),
            FilledButton(key: Key('${(key! as ValueKey<String>).value}-action'), onPressed: onAction, child: Text(action!)),
          ],
        ],
      ),
    ),
  );
}

/// W5: scan the tag on the pole (or type the code and say why).
class ArriveScreen extends StatefulWidget {
  const ArriveScreen({super.key, required this.controller, required this.task, this.showCamera = true});

  final AppController controller;
  final WorkOrderTask task;
  final bool showCamera;

  @override
  State<ArriveScreen> createState() => _ArriveScreenState();
}

class _ArriveScreenState extends State<ArriveScreen> {
  final _code = TextEditingController();
  final _reason = TextEditingController();
  final _scanner = MobileScannerController(formats: const [BarcodeFormat.qrCode], detectionSpeed: DetectionSpeed.noDuplicates);
  String? _error;
  bool _done = false;

  @override
  void dispose() {
    _scanner.dispose();
    super.dispose();
  }

  void _scanned(String raw) {
    if (_done || tagTokenFrom(raw) == null) return;
    _done = true;
    widget.controller.arrive(widget.task, scanned: raw);
    Navigator.of(context).pop();
  }

  void _manual() {
    final l10n = AppLocalizations.of(context);
    if (tagTokenFrom(_code.text) == null) return setState(() => _error = l10n.invalidCode);
    if (_reason.text.trim().isEmpty) return setState(() => _error = l10n.manualReasonRequired);
    widget.controller.arrive(widget.task, manualCode: _code.text, manualReason: _reason.text);
    Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ActionScaffold(
      title: l10n.stepArrive,
      primary: FilledButton(key: const Key('arrive-manual'), onPressed: _manual, child: Text(l10n.arriveWithCode)),
      children: [
        Text(l10n.arriveHint(widget.task.asset.assetCode ?? '—')),
        const SizedBox(height: 8),
        if (widget.showCamera)
          SizedBox(
            height: 260,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: MobileScanner(
                controller: _scanner,
                onDetect: (capture) {
                  final value = capture.barcodes.firstOrNull?.rawValue;
                  if (value != null) _scanned(value);
                },
                errorBuilder: (context, error) => Center(child: Text(l10n.cameraUnavailable)),
              ),
            ),
          ),
        const SizedBox(height: 12),
        Text(l10n.damagedTagHint, style: Theme.of(context).textTheme.bodySmall),
        TextField(
          key: const Key('arrive-code'),
          controller: _code,
          textCapitalization: TextCapitalization.characters,
          decoration: InputDecoration(labelText: l10n.manualCodeLabel),
        ),
        TextField(
          key: const Key('arrive-reason'),
          controller: _reason,
          decoration: InputDecoration(labelText: l10n.manualReasonLabel),
        ),
        if (_error != null) Text(_error!, key: const Key('arrive-error'), style: const TextStyle(color: Color(0xFFB42318))),
      ],
    );
  }
}

/// Diagnosis from the seeded codes, filtered to this light's power source (REPAIR path only in DEMO 1).
class DiagnosisScreen extends StatefulWidget {
  const DiagnosisScreen({super.key, required this.controller, required this.task});

  final AppController controller;
  final WorkOrderTask task;

  @override
  State<DiagnosisScreen> createState() => _DiagnosisScreenState();
}

class _DiagnosisScreenState extends State<DiagnosisScreen> {
  String? _code;
  final _note = TextEditingController();

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final codes = widget.controller.reference.repairCodesFor(widget.task.asset.powerSource);
    return ActionScaffold(
      title: l10n.stepDiagnosis,
      primary: FilledButton(
        key: const Key('diagnosis-save'),
        onPressed: _code == null
            ? null
            : () {
                widget.controller.recordDiagnosis(widget.task, _code!, _note.text);
                Navigator.of(context).pop();
              },
        child: Text(l10n.save),
      ),
      children: [
        if (codes.isEmpty) Text(l10n.referenceMissing),
        RadioGroup<String>(
          groupValue: _code,
          onChanged: (v) => setState(() => _code = v),
          child: Column(
            children: [
              for (final d in codes)
                RadioListTile<String>(key: Key('diagnosis-${d.code}'), value: d.code, title: Text(_arabic(context) ? d.nameAr : d.nameEn)),
            ],
          ),
        ),
        TextField(key: const Key('diagnosis-note'), controller: _note, decoration: InputDecoration(labelText: l10n.noteOptional)),
      ],
    );
  }
}

/// One material from the technician's own custody; serials are chosen from what the server says is in custody.
class MaterialScreen extends StatefulWidget {
  const MaterialScreen({super.key, required this.controller, required this.task});

  final AppController controller;
  final WorkOrderTask task;

  @override
  State<MaterialScreen> createState() => _MaterialScreenState();
}

class _MaterialScreenState extends State<MaterialScreen> {
  CustodyItem? _item;
  final Set<String> _serials = {};
  final _quantity = TextEditingController(text: '1');

  double? get _qty => _item == null ? null : (_item!.serialTracked ? _serials.length.toDouble() : double.tryParse(_quantity.text.trim()));

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final custody = widget.controller.custody;
    final item = _item;
    final qty = _qty;
    return ActionScaffold(
      title: l10n.addMaterial,
      primary: FilledButton(
        key: const Key('material-save'),
        onPressed: item == null || qty == null || qty <= 0
            ? null
            : () {
                widget.controller.consume(widget.task, item, qty, _serials.toList());
                Navigator.of(context).pop();
              },
        child: Text(l10n.save),
      ),
      children: [
        Text(l10n.custodyHint),
        if (custody.isEmpty) Text(l10n.custodyEmpty, key: const Key('custody-empty')),
        for (final c in custody)
          ListTile(
            key: Key('custody-${c.sku}'),
            selected: identical(c, item),
            leading: Icon(identical(c, item) ? Icons.check_box : Icons.check_box_outline_blank),
            title: Text(_arabic(context) ? c.nameAr : c.nameEn),
            subtitle: Text('${c.sku} · ${l10n.inCustody(_fmt(c.quantity), c.unit)}'),
            onTap: () => setState(() {
              _item = c;
              _serials.clear();
            }),
          ),
        if (item != null && item.serialTracked) ...[
          Text(l10n.chooseSerials),
          Wrap(
            spacing: 6,
            children: [
              for (final s in item.serials)
                FilterChip(
                  key: Key('serial-$s'),
                  label: Text(s),
                  selected: _serials.contains(s),
                  onSelected: (on) => setState(() => on ? _serials.add(s) : _serials.remove(s)),
                ),
            ],
          ),
        ],
        if (item != null && !item.serialTracked)
          TextField(
            key: const Key('material-quantity'),
            controller: _quantity,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: InputDecoration(labelText: l10n.quantityLabel(item.unit)),
            onChanged: (_) => setState(() {}),
          ),
      ],
    );
  }
}

String _fmt(double q) => q == q.roundToDouble() ? q.toStringAsFixed(0) : q.toString();

/// The functional-test checklist of the template pinned to this work order: test method, every item YES/NO.
class ChecklistScreen extends StatefulWidget {
  const ChecklistScreen({super.key, required this.controller, required this.task, required this.template});

  final AppController controller;
  final WorkOrderTask task;
  final ChecklistTemplate template;

  @override
  State<ChecklistScreen> createState() => _ChecklistScreenState();
}

class _ChecklistScreenState extends State<ChecklistScreen> {
  String? _method;
  final Map<String, String> _answers = {};
  final _reason = TextEditingController();

  bool get _notTested => _method == 'NOT_TESTED';

  List<ChecklistItem> get _items => widget.template.items.where((i) => !_notTested || !i.testItem).toList();

  bool get _complete =>
      _method != null && _items.every((i) => _answers.containsKey(i.code)) && (!_notTested || _reason.text.trim().isNotEmpty);

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final ar = _arabic(context);
    return ActionScaffold(
      title: l10n.stepChecklist,
      primary: FilledButton(
        key: const Key('checklist-save'),
        onPressed: !_complete
            ? null
            : () {
                final answers = {for (final i in _items) i.code: _answers[i.code]!};
                widget.controller.recordChecklist(widget.task, widget.template.id, _method!, answers, _notTested ? _reason.text.trim() : null);
                Navigator.of(context).pop();
              },
        child: Text(l10n.save),
      ),
      children: [
        Text(l10n.testMethodLabel, style: Theme.of(context).textTheme.titleSmall),
        RadioGroup<String>(
          groupValue: _method,
          onChanged: (v) => setState(() => _method = v),
          child: Column(
            children: [
              for (final m in widget.template.testMethods)
                RadioListTile<String>(key: Key('method-${m.code}'), value: m.code, title: Text(ar ? m.textAr : m.textEn)),
            ],
          ),
        ),
        if (_notTested)
          TextField(
            key: const Key('not-tested-reason'),
            controller: _reason,
            decoration: InputDecoration(labelText: l10n.notTestedReason),
            onChanged: (_) => setState(() {}),
          ),
        const Divider(),
        for (final i in _items)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 6),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(ar ? i.textAr : i.textEn),
                const SizedBox(height: 4),
                SegmentedButton<String>(
                  key: Key('item-${i.code}'),
                  emptySelectionAllowed: true,
                  segments: [
                    ButtonSegment(value: 'YES', label: Text(l10n.yes, key: Key('item-${i.code}-YES'))),
                    ButtonSegment(value: 'NO', label: Text(l10n.no, key: Key('item-${i.code}-NO'))),
                  ],
                  selected: {?_answers[i.code]},
                  onSelectionChanged: (s) => setState(() => s.isEmpty ? _answers.remove(i.code) : _answers[i.code] = s.first),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

/// W9 / W12: root cause (and why no material, if none was recorded), then submit for supervisor review.
class SubmitScreen extends StatefulWidget {
  const SubmitScreen({super.key, required this.controller, required this.task, required this.view});

  final AppController controller;
  final WorkOrderTask task;
  final WorkView view;

  @override
  State<SubmitScreen> createState() => _SubmitScreenState();
}

class _SubmitScreenState extends State<SubmitScreen> {
  final _rootCause = TextEditingController();
  final _noMaterial = TextEditingController();

  bool get _needsNoMaterialReason => widget.view.materials == 0;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final ok = _rootCause.text.trim().isNotEmpty && (!_needsNoMaterialReason || _noMaterial.text.trim().isNotEmpty);
    return ActionScaffold(
      title: widget.view.isReturned ? l10n.stepResubmit : l10n.stepSubmit,
      primary: FilledButton(
        key: const Key('submit-confirm'),
        onPressed: !ok
            ? null
            : () {
                widget.controller.submit(
                  widget.task,
                  rootCause: _rootCause.text,
                  noMaterialReason: _needsNoMaterialReason ? _noMaterial.text : null,
                  resubmit: widget.view.isReturned,
                );
                Navigator.of(context).pop();
              },
        child: Text(widget.view.isReturned ? l10n.resubmitAction : l10n.submitAction),
      ),
      children: [
        Text(l10n.submitSummary(widget.view.beforePhotos, widget.view.afterPhotos, widget.view.testPhotos, widget.view.materials)),
        TextField(
          key: const Key('root-cause'),
          controller: _rootCause,
          maxLines: 3,
          decoration: InputDecoration(labelText: l10n.rootCauseLabel),
          onChanged: (_) => setState(() {}),
        ),
        if (_needsNoMaterialReason)
          TextField(
            key: const Key('no-material-reason'),
            controller: _noMaterial,
            decoration: InputDecoration(labelText: l10n.noMaterialReasonLabel),
            onChanged: (_) => setState(() {}),
          ),
      ],
    );
  }
}
