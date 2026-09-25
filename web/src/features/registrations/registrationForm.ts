import type { Registration, RegistrationRequest, StreetLightAttributes } from '../../shared/types';

/** Form model: every input is a string; converted to the API request on submit. */
export interface RegistrationFormValues {
  reason: RegistrationRequest['reason'];
  projectRef: string;
  latitude: string;
  longitude: string;
  addressText: string;
  circuitRef: string;
  powerSource: StreetLightAttributes['powerSource'];
  responsibleParty: StreetLightAttributes['responsibleParty'];
  responsibleEntityName: string;
  installedOnPrecision: StreetLightAttributes['installedOnPrecision'];
  installedOn: string;
  observedStatus: StreetLightAttributes['observedStatus'];
  poleMaterial: string;
  poleHeightM: string;
  luminaireMake: string;
  luminaireModel: string;
  luminaireWattageW: string;
  luminaireSerial: string;
  solarPanelW: string;
  solarBatteryType: string;
  solarBatteryAh: string;
  solarControllerModel: string;
  warrantyUntil: string;
  supplier: string;
}

export function emptyForm(): RegistrationFormValues {
  return {
    reason: 'BASELINE_SURVEY', projectRef: '', latitude: '', longitude: '', addressText: '', circuitRef: '',
    powerSource: 'GRID', responsibleParty: 'MUNICIPALITY', responsibleEntityName: '', installedOnPrecision: 'UNKNOWN',
    installedOn: '', observedStatus: 'UNKNOWN', poleMaterial: '', poleHeightM: '', luminaireMake: '', luminaireModel: '',
    luminaireWattageW: '', luminaireSerial: '', solarPanelW: '', solarBatteryType: '', solarBatteryAh: '', solarControllerModel: '',
    warrantyUntil: '', supplier: '',
  };
}

const str = (v: string) => (v.trim() === '' ? null : v.trim());
const num = (v: string) => (v.trim() === '' ? null : Number(v));
const text = (v: string | number | null | undefined) => (v === null || v === undefined ? '' : String(v));

/** Converts the form to the API request. For precision YEAR the date is January 1st of the given year. */
export function toRequest(f: RegistrationFormValues): RegistrationRequest {
  const solar = f.powerSource !== 'GRID';
  const installedOn =
    f.installedOnPrecision === 'UNKNOWN' ? null : f.installedOnPrecision === 'YEAR' ? (str(f.installedOn) ? `${f.installedOn.trim().slice(0, 4)}-01-01` : null) : str(f.installedOn);
  return {
    reason: f.reason,
    projectRef: str(f.projectRef),
    attributes: {
      latitude: Number(f.latitude),
      longitude: Number(f.longitude),
      powerSource: f.powerSource,
      responsibleParty: f.responsibleParty,
      responsibleEntityName: str(f.responsibleEntityName),
      installedOnPrecision: f.installedOnPrecision,
      installedOn,
      observedStatus: f.observedStatus,
      addressText: str(f.addressText),
      circuitRef: str(f.circuitRef),
      poleMaterial: str(f.poleMaterial),
      poleHeightM: num(f.poleHeightM),
      luminaireMake: str(f.luminaireMake),
      luminaireModel: str(f.luminaireModel),
      luminaireWattageW: num(f.luminaireWattageW),
      luminaireSerial: str(f.luminaireSerial),
      solarPanelW: solar ? num(f.solarPanelW) : null,
      solarBatteryType: solar ? str(f.solarBatteryType) : null,
      solarBatteryAh: solar ? num(f.solarBatteryAh) : null,
      solarControllerModel: solar ? str(f.solarControllerModel) : null,
      warrantyUntil: str(f.warrantyUntil),
      supplier: str(f.supplier),
    },
  };
}

export function fromRegistration(r: Registration): RegistrationFormValues {
  const a = r.attributes;
  return {
    reason: r.reason, projectRef: text(r.projectRef), latitude: text(a.latitude), longitude: text(a.longitude),
    addressText: text(a.addressText), circuitRef: text(a.circuitRef), powerSource: a.powerSource, responsibleParty: a.responsibleParty,
    responsibleEntityName: text(a.responsibleEntityName), installedOnPrecision: a.installedOnPrecision,
    installedOn: a.installedOnPrecision === 'YEAR' ? text(a.installedOn).slice(0, 4) : text(a.installedOn), observedStatus: a.observedStatus,
    poleMaterial: text(a.poleMaterial), poleHeightM: text(a.poleHeightM), luminaireMake: text(a.luminaireMake), luminaireModel: text(a.luminaireModel),
    luminaireWattageW: text(a.luminaireWattageW), luminaireSerial: text(a.luminaireSerial), solarPanelW: text(a.solarPanelW),
    solarBatteryType: text(a.solarBatteryType), solarBatteryAh: text(a.solarBatteryAh), solarControllerModel: text(a.solarControllerModel),
    warrantyUntil: text(a.warrantyUntil), supplier: text(a.supplier),
  };
}

/** Synthetic demo values (the pilot municipality is not decided; the server accepts any valid coordinates). */
export function syntheticForm(seed = Date.now()): RegistrationFormValues {
  const jitter = (seed % 1000) / 1_000_000;
  return {
    ...emptyForm(),
    latitude: (33.5138 + jitter).toFixed(6),
    longitude: (36.2765 + jitter).toFixed(6),
    addressText: `شارع تجريبي ${seed % 100}`,
    installedOnPrecision: 'YEAR',
    installedOn: '2019',
    observedStatus: 'OPERATIONAL',
    poleMaterial: 'STEEL',
    poleHeightM: '8',
    luminaireMake: 'Demo',
    luminaireWattageW: '60',
  };
}
