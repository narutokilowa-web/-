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
  /** The phone that sent the command (M3: field commands). */
  deviceId?: string | null;
  commandId?: string | null;
}

/** Mirrors Mfop.Access.Contracts.DeviceDto. */
export interface Device {
  id: string;
  userId: string;
  userDisplayName: string;
  username: string;
  platform: string;
  model: string;
  osVersion: string;
  appVersion: string;
  status: 'PENDING_APPROVAL' | 'ACTIVE' | 'REJECTED' | 'REVOKED';
  requestedAt: string;
  decidedByName: string | null;
  decidedAt: string | null;
  decisionReason: string | null;
  revokedByName: string | null;
  revokedAt: string | null;
  revokeReason: string | null;
  lastPullAt: string | null;
  lastPushAt: string | null;
  lastReportedPendingCommands: number | null;
}

export const FAULT_CATEGORIES = ['LIGHT_OFF', 'FLICKERING', 'DAY_BURNING', 'DAMAGED_POLE', 'EXPOSED_WIRES', 'MISSING_PARTS', 'OTHER'] as const;
export const POWER_AT_INSPECTION = ['CONFIRMED_AVAILABLE', 'CONFIRMED_UNAVAILABLE', 'UNKNOWN'] as const;
export const POWER_INFO_SOURCES = ['OBSERVED_ON_SITE', 'ELECTRICITY_AUTHORITY', 'SCHEDULE', 'RESIDENT', 'OTHER'] as const;
export const DISMISS_REASONS = ['DUPLICATE', 'NOT_A_FAULT', 'POWER_UNAVAILABLE_AT_REPORT', 'OUT_OF_MUNICIPAL_RESPONSIBILITY', 'OTHER'] as const;
export const PRIORITIES = ['P1', 'P2', 'P3', 'P4'] as const;

export interface ReportFaultRequest {
  assetId: string;
  category: (typeof FAULT_CATEGORIES)[number];
  powerAtInspection: (typeof POWER_AT_INSPECTION)[number];
  powerInfoSource: (typeof POWER_INFO_SOURCES)[number] | null;
  powerNote: string | null;
  description: string | null;
}

export interface Fault {
  id: string;
  assetId: string;
  assetCode: string | null;
  assetAddress: string | null;
  category: string;
  source: string;
  reportedBy: string;
  reportedByName: string;
  reportedAt: string;
  powerAtInspection: string;
  powerInfoSource: string | null;
  powerNote: string | null;
  reportedWithoutPower: boolean;
  responsibleParty: string;
  description: string | null;
  state: 'OPEN' | 'IN_WORK_ORDER' | 'RESOLVED' | 'DISMISSED';
  workOrderId: string | null;
  dismissReason: string | null;
  dismissNote: string | null;
  dismissedByName: string | null;
  dismissedAt: string | null;
  confirmationCount: number;
  confirmations: { id: string; confirmedByName: string; at: string; powerAtInspection: string; powerInfoSource: string | null; description: string | null }[];
}

export interface AssetSummary {
  id: string;
  assetCode: string | null;
  lifecycleState: string;
  operationalStatus: string | null;
  powerSource: string;
  responsibleParty: string;
  responsibleEntityName: string | null;
  addressText: string | null;
  latitude: number;
  longitude: number;
}

export interface WorkOrderListItem {
  id: string;
  code: string;
  state: string;
  priority: string;
  assetId: string;
  assetCode: string | null;
  assetAddress: string | null;
  faultCategory: string;
  assigneeId: string | null;
  assigneeName: string | null;
  createdAt: string;
  slaDueAt: string | null;
}

export interface WorkOrder {
  id: string;
  code: string;
  state: string;
  priority: string;
  slaDueAt: string | null;
  note: string | null;
  faultId: string;
  faultCategory: string;
  faultState: string;
  asset: AssetSummary | null;
  assigneeId: string | null;
  assigneeName: string | null;
  createdByName: string;
  createdAt: string;
  cancelReason: string | null;
  cancelledAt: string | null;
  assignments: { id: string; assigneeId: string; assigneeName: string; state: string; startedAt: string; startedByName: string; endedAt: string | null; endReason: string | null }[];
  allowedCommands: string[];
}

export interface Technician {
  userId: string;
  displayName: string;
  username: string;
  hasActiveDevice: boolean;
}

// ---- M3: field execution, review, closure, dashboard, audit, stock, rules ----

/** Mirrors Mfop.Inventory.Contracts.MoneyDto: an amount always carries its ISO currency and the date it is valid for. */
export interface Money {
  amount: number;
  currency: string;
  asOf: string;
}

export interface DiagnosisCode {
  code: string;
  nameAr: string;
  nameEn: string;
  countsAsFailure: boolean;
  requiresMaterial: boolean;
  expectedItems: string[];
  powerSources: string[];
  path: string;
}

export interface ChecklistItem {
  code: string;
  textAr: string;
  textEn: string;
  expected: 'YES' | 'NO';
  blocking: boolean;
  testItem: boolean;
}

export interface ChecklistTemplate {
  id: string;
  code: string;
  version: number;
  powerSources: string[];
  items: ChecklistItem[];
  testMethods: { code: string; textAr: string; textEn: string }[];
  evidenceRequirements: { beforeFirstArrival: number; beforeLaterArrival: number; after: number; test: number };
}

export interface ReviewEvidence {
  id: string;
  kind: 'BEFORE' | 'AFTER' | 'TEST';
  state: 'ANNOUNCED' | 'UPLOADED' | 'VERIFIED' | 'HASH_MISMATCH' | 'DUPLICATE';
  sha256Device: string;
  sha256Server: string | null;
  capturedAt: string | null;
  announcedAt: string;
  verifiedAt: string | null;
  hasPreview: boolean;
  duplicateOf: string | null;
  supersededBy: string | null;
  supersedeReason: string | null;
  capturedByName: string;
}

export interface Movement {
  id: string;
  type: string;
  itemId: string;
  sku: string;
  itemNameAr: string;
  itemNameEn: string;
  unit: string;
  quantity: number;
  serials: string[];
  workOrderId: string | null;
  assetId: string | null;
  actorId: string;
  occurredAt: string;
  recordedAt: string;
  exceedsCustody: boolean;
  serialNotInCustody: boolean;
  unitCost: Money | null;
  totalCost: Money | null;
}

export interface Arrival {
  id: string;
  arrivalNo: number;
  userName: string;
  tagScanned: boolean;
  scannedCode: string | null;
  manualCode: string | null;
  manualReason: string | null;
  deviceTime: string | null;
  recordedAt: string;
}

export interface ChecklistResult {
  id: string;
  templateId: string;
  testMethod: string;
  notTestedReason: string | null;
  testResult: 'PASSED' | 'FAILED' | 'NOT_TESTED';
  blocked: boolean;
  answers: Record<string, string>;
  recordedByName: string;
  recordedAt: string;
}

export interface Submission {
  id: string;
  submissionNo: number;
  submittedByName: string;
  submittedAt: string;
  payloadHash: string;
  evidenceIds: string[];
  materialIds: string[];
  checklistId: string | null;
  rootCauseNote: string | null;
  noMaterialReason: string | null;
  addedEvidenceIds: string[];
  addedMaterialIds: string[];
}

export interface ReviewDecision {
  id: string;
  submissionNo: number;
  reviewerName: string;
  decision: 'APPROVED' | 'RETURNED';
  reasons: string[];
  note: string | null;
  decidedAt: string;
}

export interface ReviewFlag {
  id: string;
  ruleCode: string;
  severity: 'CRITICAL' | 'WARNING' | 'INFO';
  state: string;
  explanation: Record<string, unknown>;
  createdAt: string;
}

/** Mirrors Mfop.WorkOrders.Contracts.WorkOrderReviewDto. */
export interface WorkOrderReview {
  workOrderId: string;
  code: string;
  state: string;
  diagnosis: DiagnosisCode | null;
  diagnosisNote: string | null;
  rootCauseNote: string | null;
  noMaterialReason: string | null;
  testMethod: string | null;
  testResult: string | null;
  firstArrivedAt: string | null;
  workStartedAt: string | null;
  submittedAt: string | null;
  workMinutes: number | null;
  arrivals: Arrival[];
  evidence: ReviewEvidence[];
  materials: Movement[];
  expectedItemsMissing: string[];
  template: ChecklistTemplate | null;
  checklist: ChecklistResult | null;
  submissions: Submission[];
  reviews: ReviewDecision[];
  flags: ReviewFlag[];
  pendingEvidence: number;
  blockedEvidence: number;
  canApprove: boolean;
  approveBlockedReason: string | null;
  executors: string[];
  sealHash: string | null;
  closedAt: string | null;
  closedByName: string | null;
}

export const RETURN_REASONS = ['MISSING_EVIDENCE', 'UNCLEAR_EVIDENCE', 'CHECKLIST_INCOMPLETE', 'MATERIAL_QUESTION', 'DIAGNOSIS_QUESTION', 'OTHER'] as const;

export interface CountByKey {
  key: string;
  count: number;
}

export interface CompletedWorkOrder {
  id: string;
  code: string;
  assetId: string;
  assetCode: string | null;
  assetAddress: string | null;
  diagnosisCode: string | null;
  closedAt: string;
  closedByName: string;
  sealHash: string | null;
}

export interface DashboardSummary {
  lightsByOperationalStatus: CountByKey[];
  openFaults: number;
  openFaultsReportedWithoutPower: number;
  openWorkOrdersByState: CountByKey[];
  pendingReview: number;
  recentlyCompleted: CompletedWorkOrder[];
}

export interface AuditSearchEntry {
  seq: number;
  occurredAt: string;
  actorUserId: string | null;
  actorName: string;
  actorRoles: string[];
  deviceId: string | null;
  action: string;
  entityType: string;
  entityId: string;
  entityVersion: number | null;
  changes: Record<string, { old: ChangeValue; new: ChangeValue }>;
  facts: Record<string, ChangeValue> | null;
  reason: string | null;
  commandId: string;
  prevHash: string;
  hash: string;
}

export interface Balance {
  locationId: string;
  locationKind: 'WAREHOUSE' | 'CUSTODY' | 'SUPPLIER' | 'CONSUMED';
  locationName: string;
  custodianUserId: string | null;
  custodianName: string | null;
  itemId: string;
  sku: string;
  itemNameAr: string;
  itemNameEn: string;
  unit: string;
  quantity: number;
}

export interface CountedFailure {
  faultId: string;
  workOrderId: string;
  workOrderCode: string;
  diagnosisCode: string;
  resolvedAt: string;
}

export interface Recommendation {
  id: string;
  assetId: string;
  code: 'FULL_INSPECTION';
  ruleCode: string;
  state: string;
  count: number;
  threshold: number;
  windowDays: number;
  countedFailures: CountedFailure[];
  createdAt: string;
  updatedAt: string;
}

export interface RuleSetting {
  ruleCode: string;
  enabled: boolean;
  params: { threshold?: number; window_days?: number };
  changedBy: string | null;
  changedAt: string;
  version: number;
}
