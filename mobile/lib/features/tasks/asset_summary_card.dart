import 'package:flutter/material.dart';

import '../../core/models.dart';
import '../../l10n/app_localizations.dart';
import '../../shared/widgets.dart';

/// Compact asset facts: code, type, power source, operational status, responsible party (docs/lean-pilot/08 §8.3).
class AssetSummaryCard extends StatelessWidget {
  const AssetSummaryCard({super.key, required this.asset});

  final AssetSummary asset;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(asset.assetCode ?? '—', key: const Key('asset-code'), style: theme.textTheme.headlineSmall),
            Text('${l10n.assetTypeStreetLight} · ${l10n.powerSourceName(asset.powerSource)}'),
            const SizedBox(height: 8),
            Row(
              children: [
                Text('${l10n.statusLabel}: '),
                Text(
                  l10n.opStatus(asset.operationalStatus ?? 'UNKNOWN'),
                  key: const Key('asset-status'),
                  style: TextStyle(color: statusColor(asset.operationalStatus), fontWeight: FontWeight.bold),
                ),
              ],
            ),
            Text('${l10n.responsibleLabel}: ${l10n.responsibleName(asset.responsibleParty)}'),
            if (asset.addressText != null) Text('${l10n.addressLabel}: ${asset.addressText}', key: const Key('asset-address')),
          ],
        ),
      ),
    );
  }
}
