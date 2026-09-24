/** Mirrors Mfop.Assets.Domain.StreetLightAttributes (JSON, camelCase). */
export interface StreetLightAttributes {
  latitude: number;
  longitude: number;
  powerSource: 'GRID' | 'SOLAR' | 'HYBRID';
  responsibleParty: 'MUNICIPALITY' | 'ELECTRICITY_AUTHORITY' | 'OTHER_PUBLIC_ENTITY' | 'SHARED';
  installedOnPrecision: 'EXACT' | 'YEAR' | 'UNKNOWN';
  observedStatus: 'OPERATIONAL' | 'FAULTY' | 'UNKNOWN';
  addressText?: string | null;
  circuitRef?: string | null;
  poleMaterial?: string | null;
  poleHeightM?: number | null;
  luminaireMake?: string | null;
  luminaireModel?: string | null;
  luminaireWattageW?: number | null;
  luminaireSerial?: string | null;
  solarPanelW?: number | null;
  solarBatteryType?: string | null;
  solarBatteryAh?: number | null;
  solarControllerModel?: string | null;
  installedOn?: string | null;
  warrantyUntil?: string | null;
  supplier?: string | null;
  responsibleEntityName?: string | null;
}

export interface RegistrationRequest {
  reason: 'BASELINE_SURVEY' | 'NEW_INSTALLATION' | 'MISSED_IN_SURVEY';
  projectRef?: string | null;
  attributes: StreetLightAttributes;
}

export interface Registration {
  id: string;
  assetId: string;
  state: 'DRAFT' | 'SUBMITTED' | 'RETURNED' | 'APPROVED' | 'REJECTED' | 'DISCARDED';
  reason: RegistrationRequest['reason'];
  projectRef: string | null;
  captureChannel: string;
  registrarId: string;
  registrarName: string;
  createdAt: string;
  submittedAt: string | null;
  decidedBy: string | null;
  decidedByName: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
  returnCount: number;
  attributes: StreetLightAttributes;
  assetCode: string | null;
  allowedCommands: string[];
}

export interface Tag {
  code: string;
  checkCode: string;
  humanCode: string;
  qrPayload: string;
}

export interface AssetVersion {
  versionNo: number;
  snapshotHash: string;
  approvedBy: string;
  approvedByName: string;
  approvedAt: string;
  reason: string;
}

export interface Asset {
  id: string;
  assetCode: string | null;
  lifecycleState: string;
  operationalStatus: string | null;
  attributes: StreetLightAttributes;
  captureChannel: string;
  tag: Tag | null;
  currentVersionNo: number;
  versions: AssetVersion[];
  registrationId: string | null;
}

export interface AssetListItem {
  id: string;
  assetCode: string | null;
  lifecycleState: string;
  operationalStatus: string | null;
  powerSource: string;
  addressText: string | null;
  latitude: number;
  longitude: number;
}

export type ChangeValue = string | number | boolean | null | ChangeValue[];

export interface HistoryEntry {
  seq: number;
  occurredAt: string;
  actorUserId: string | null;
  actorName: string;
  actorRoles: string[];
  action: string;
  entityType: string;
  entityId: string;
  changes: Record<string, { old: ChangeValue; new: ChangeValue }>;
  facts: Record<string, ChangeValue> | null;
  reason: string | null;
  hash: string;
}
