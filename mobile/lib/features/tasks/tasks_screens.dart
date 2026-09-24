import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../core/api_client.dart';
import '../../core/models.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';
import 'asset_summary_card.dart';

/// "My tasks": work orders currently assigned to me, sorted by priority, then due time (docs/lean-pilot/08 §8.4 step 0).
class MyTasksTab extends StatelessWidget {
  const MyTasksTab({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tasks = controller.tasks;
    return RefreshIndicator(
      onRefresh: controller.syncNow,
      child: tasks.isEmpty
          ? ListView(
              children: [
                Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(controller.isTechnician ? l10n.noTasks : l10n.notTechnician, key: const Key('no-tasks')),
                ),
              ],
            )
          : ListView.separated(
              key: const Key('task-list'),
              itemCount: tasks.length,
              separatorBuilder: (_, _) => const Divider(height: 1),
              itemBuilder: (context, i) {
                final t = tasks[i];
                return ListTile(
                  key: Key('task-${t.code}'),
                  minTileHeight: 72,
                  leading: CircleAvatar(child: Text(t.priority)),
                  title: Text('${t.asset.assetCode ?? '—'} · ${l10n.faultCategoryName(t.faultCategory)}'),
                  subtitle: Text(
                    [t.code, if (t.asset.addressText != null) t.asset.addressText!, l10n.workOrderStateName(t.state)].join(' · '),
                  ),
                  onTap: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => TaskDetailScreen(controller: controller, cached: t),
                    ),
                  ),
                );
              },
            ),
    );
  }
}

/// One task, refreshed from the server when online (the server decides whether it is still mine).
class TaskDetailScreen extends StatefulWidget {
  const TaskDetailScreen({super.key, required this.controller, required this.cached});

  final AppController controller;
  final WorkOrderTask cached;

  @override
  State<TaskDetailScreen> createState() => _TaskDetailScreenState();
}

class _TaskDetailScreenState extends State<TaskDetailScreen> {
  late WorkOrderTask _task = widget.cached;
  String? _error;

  @override
  void initState() {
    super.initState();
    widget.controller
        .openTask(widget.cached.id)
        .then((t) {
          if (mounted) setState(() => _task = t);
        })
        .catchError((Object e) {
          if (mounted && e is ApiException) setState(() => _error = e.code);
        });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final t = _task;
    return ActionScaffold(
      title: l10n.taskTitle(t.code),
      children: [
        if (_error != null)
          Text(
            l10n.errorCode(_error!),
            key: const Key('task-error'),
            style: const TextStyle(color: Color(0xFFB42318)),
          ),
        Wrap(
          spacing: 8,
          children: [
            Chip(label: Text(l10n.priorityName(t.priority))),
            Chip(key: const Key('task-state'), label: Text(l10n.workOrderStateName(t.state))),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          '${l10n.faultLabel}: ${l10n.faultCategoryName(t.faultCategory)} · ${l10n.powerAtInspectionName(t.faultPowerAtInspection)}',
          key: const Key('task-fault'),
        ),
        if (t.faultDescription != null) Text(t.faultDescription!),
        if (t.note != null) Text('${l10n.noteLabel}: ${t.note}'),
        Text(l10n.assignedAt(formatTime(context, t.assignedAt))),
        if (t.slaDueAt != null) Text(l10n.dueAt(formatTime(context, t.slaDueAt!))),
        const SizedBox(height: 16),
        AssetSummaryCard(asset: t.asset),
        const SizedBox(height: 16),
        Text(l10n.taskNextSteps, style: Theme.of(context).textTheme.bodySmall),
      ],
    );
  }
}
