import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';
import '../me/me_tab.dart';
import '../scan/scan_screens.dart';
import '../stock/stock_tab.dart';
import '../tasks/tasks_screens.dart';

/// Bottom tabs by role (docs/lean-pilot/08 §8.2): My tasks (technician), Materials (technician, M5), Scan, Me.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key, required this.controller, required this.onToggleLocale, this.showCamera = true});

  final AppController controller;
  final VoidCallback onToggleLocale;
  final bool showCamera;

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _tab = 0;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final c = widget.controller;
    final technician = c.sync?.cachedRoles.contains('TECHNICIAN') ?? false;
    final pages = [
      MyTasksTab(controller: c, showCamera: widget.showCamera),
      if (technician) StockTab(controller: c),
      ScanTab(controller: c, showCamera: widget.showCamera),
      MeTab(controller: c, onToggleLocale: widget.onToggleLocale),
    ];
    final tab = _tab < pages.length ? _tab : 0;
    return Scaffold(
      appBar: AppBar(title: Text(l10n.appTitle)),
      body: Column(
        children: [
          SyncStrip(status: c.syncStatus ?? c.sync?.status),
          Expanded(child: pages[tab]),
        ],
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: tab,
        onDestinationSelected: (i) => setState(() => _tab = i),
        destinations: [
          NavigationDestination(key: const Key('tab-tasks'), icon: const Icon(Icons.assignment), label: l10n.tabTasks),
          if (technician) NavigationDestination(key: const Key('tab-stock'), icon: const Icon(Icons.inventory_2), label: l10n.tabStock),
          NavigationDestination(key: const Key('tab-scan'), icon: const Icon(Icons.qr_code_scanner), label: l10n.tabScan),
          NavigationDestination(key: const Key('tab-me'), icon: const Icon(Icons.person), label: l10n.tabMe),
        ],
      ),
    );
  }
}
