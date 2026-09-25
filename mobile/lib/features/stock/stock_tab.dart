import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../core/models.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';

bool _arabic(BuildContext context) => Localizations.localeOf(context).languageCode == 'ar';

String _fmt(double q) => q == q.roundToDouble() ? q.toStringAsFixed(0) : q.toString();

/// M5 (docs/lean-pilot/04 §4.9–4.11, 07): the technician's material. Issued material waits here until they confirm every
/// serial (or report a problem); unused material is returned from custody; failed parts show what the warehouse expects.
/// Every action is a command in the outbox, sent when the phone is online.
class StockTab extends StatelessWidget {
  const StockTab({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListenableBuilder(
      listenable: controller,
      builder: (context, _) {
        final issues = controller.issuesToAnswer;
        final stock = controller.stock;
        final custody = controller.custody;
        final returns = stock.returns;
        final cores = stock.coreReturns;
        final ar = _arabic(context);
        return RefreshIndicator(
          onRefresh: () async => controller.syncNow(),
          child: ListView(
            padding: const EdgeInsets.all(12),
            children: [
              Text(l10n.stockHint, style: Theme.of(context).textTheme.bodySmall),
              const SizedBox(height: 8),
              Text(l10n.issuesToConfirmTitle, style: Theme.of(context).textTheme.titleMedium),
              if (issues.isEmpty) Text(l10n.nothingWaiting, key: const Key('issues-empty')),
              for (final issue in issues) _IssueCard(key: Key('issue-${issue.code}'), controller: controller, issue: issue),
              const Divider(height: 24),
              Text(l10n.myCustodyTitle, style: Theme.of(context).textTheme.titleMedium),
              OutlinedButton(
                key: const Key('return-unused'),
                onPressed: custody.isEmpty
                    ? null
                    : () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ReturnScreen(controller: controller))),
                child: Text(l10n.returnUnused),
              ),
              if (custody.isEmpty) Text(l10n.custodyEmpty),
              for (final c in custody)
                ListTile(
                  key: Key('stock-custody-${c.sku}'),
                  dense: true,
                  title: Text(ar ? c.nameAr : c.nameEn),
                  subtitle: Text([c.sku, l10n.inCustody(_fmt(c.quantity), c.unit), if (c.serials.isNotEmpty) c.serials.join(', ')].join(' · ')),
                ),
              if (returns.isNotEmpty) ...[
                const Divider(height: 24),
                Text(l10n.returnsWaitingTitle, style: Theme.of(context).textTheme.titleMedium),
                for (final r in returns)
                  ListTile(
                    key: Key('return-${r.code}'),
                    dense: true,
                    title: Text(r.code),
                    subtitle: Text(r.lines.map((l) => '${l.sku} × ${_fmt(l.quantity)}').join(', ')),
                  ),
              ],
              if (cores.isNotEmpty) ...[
                const Divider(height: 24),
                Text(l10n.coresTitle, style: Theme.of(context).textTheme.titleMedium),
                for (final core in cores)
                  ListTile(
                    key: Key('core-${core.code}'),
                    dense: true,
                    title: Text('${core.sku} — ${core.workOrderCode}'),
                    subtitle: Text(l10n.coreStateName(core.state)),
                  ),
              ],
            ],
          ),
        );
      },
    );
  }
}

class _IssueCard extends StatefulWidget {
  const _IssueCard({super.key, required this.controller, required this.issue});

  final AppController controller;
  final StockIssueView issue;

  @override
  State<_IssueCard> createState() => _IssueCardState();
}

class _IssueCardState extends State<_IssueCard> {
  final Set<String> _ticked = {};

  Future<void> _dispute() async {
    final l10n = AppLocalizations.of(context);
    final reason = TextEditingController();
    final send = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.reportProblem),
        content: TextField(key: const Key('dispute-reason'), controller: reason, decoration: InputDecoration(labelText: l10n.disputeReasonLabel)),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(false), child: Text(MaterialLocalizations.of(context).cancelButtonLabel)),
          FilledButton(key: const Key('dispute-send'), onPressed: () => Navigator.of(context).pop(true), child: Text(l10n.send)),
        ],
      ),
    );
    if (send == true && reason.text.trim().length >= 3) await widget.controller.disputeIssue(widget.issue, reason.text);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final issue = widget.issue;
    final serials = issue.serials;
    final complete = serials.every(_ticked.contains);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(l10n.issueFrom(issue.code, issue.issuedByName), style: Theme.of(context).textTheme.titleSmall),
            for (final line in issue.lines) Text('${line.sku} — ${_arabic(context) ? line.nameAr : line.nameEn} × ${_fmt(line.quantity)} ${line.unit}'),
            if (serials.isNotEmpty) ...[
              const SizedBox(height: 6),
              Text(l10n.tickSerials, style: Theme.of(context).textTheme.bodySmall),
              Wrap(
                spacing: 6,
                children: [
                  for (final s in serials)
                    FilterChip(
                      key: Key('issue-serial-$s'),
                      label: Text(s),
                      selected: _ticked.contains(s),
                      onSelected: (on) => setState(() => on ? _ticked.add(s) : _ticked.remove(s)),
                    ),
                ],
              ),
            ],
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  child: FilledButton(
                    key: const Key('issue-confirm'),
                    onPressed: complete ? () => widget.controller.acknowledgeIssue(issue) : null,
                    child: Text(l10n.confirmReceipt),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(child: OutlinedButton(key: const Key('issue-dispute'), onPressed: _dispute, child: Text(l10n.reportProblem))),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Unused material back to the warehouse: one item from my custody, its quantity or serials, an optional note.
class ReturnScreen extends StatefulWidget {
  const ReturnScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<ReturnScreen> createState() => _ReturnScreenState();
}

class _ReturnScreenState extends State<ReturnScreen> {
  CustodyItem? _item;
  final Set<String> _serials = {};
  final _quantity = TextEditingController(text: '1');
  final _note = TextEditingController();

  double? get _qty => _item == null ? null : (_item!.serialTracked ? _serials.length.toDouble() : double.tryParse(_quantity.text.trim()));

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final item = _item;
    final qty = _qty;
    return ActionScaffold(
      title: l10n.returnUnused,
      primary: FilledButton(
        key: const Key('return-save'),
        onPressed: item == null || qty == null || qty <= 0 || qty > item.quantity
            ? null
            : () {
                widget.controller.returnUnused(item, qty, _serials.toList(), _note.text);
                Navigator.of(context).pop();
              },
        child: Text(l10n.send),
      ),
      children: [
        for (final c in widget.controller.custody)
          ListTile(
            key: Key('return-item-${c.sku}'),
            selected: identical(c, item),
            leading: Icon(identical(c, item) ? Icons.check_box : Icons.check_box_outline_blank),
            title: Text(_arabic(context) ? c.nameAr : c.nameEn),
            subtitle: Text('${c.sku} · ${l10n.inCustody(_fmt(c.quantity), c.unit)}'),
            onTap: () => setState(() {
              _item = c;
              _serials.clear();
            }),
          ),
        if (item != null && item.serialTracked)
          Wrap(
            spacing: 6,
            children: [
              for (final s in item.serials)
                FilterChip(
                  key: Key('return-serial-$s'),
                  label: Text(s),
                  selected: _serials.contains(s),
                  onSelected: (on) => setState(() => on ? _serials.add(s) : _serials.remove(s)),
                ),
            ],
          ),
        if (item != null && !item.serialTracked)
          TextField(
            key: const Key('return-quantity'),
            controller: _quantity,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: InputDecoration(labelText: l10n.quantityLabel(item.unit)),
            onChanged: (_) => setState(() {}),
          ),
        TextField(key: const Key('return-note'), controller: _note, decoration: InputDecoration(labelText: l10n.returnNoteLabel)),
      ],
    );
  }
}
