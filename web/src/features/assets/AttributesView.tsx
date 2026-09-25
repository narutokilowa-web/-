import { useTranslation } from 'react-i18next';
import type { StreetLightAttributes } from '../../shared/types';
import { display } from '../../shared/format';

const ORDER: (keyof StreetLightAttributes)[] = [
  'latitude', 'longitude', 'addressText', 'circuitRef', 'powerSource', 'observedStatus', 'responsibleParty', 'responsibleEntityName',
  'poleMaterial', 'poleHeightM', 'luminaireMake', 'luminaireModel', 'luminaireWattageW', 'luminaireSerial', 'solarPanelW',
  'solarBatteryType', 'solarBatteryAh', 'solarControllerModel', 'installedOnPrecision', 'installedOn', 'warrantyUntil', 'supplier',
];

const ENUM_GROUP: Partial<Record<keyof StreetLightAttributes, string>> = {
  powerSource: 'powerSource', observedStatus: 'status', responsibleParty: 'responsibleParty', installedOnPrecision: 'precision',
};

export function AttributesView({ attributes }: { attributes: StreetLightAttributes }) {
  const { t } = useTranslation();
  return (
    <dl className="facts attributes" data-testid="attributes">
      {ORDER.filter((k) => attributes[k] !== null && attributes[k] !== undefined && attributes[k] !== '').map((k) => (
        <div key={k} className="fact">
          <dt>{t(`field.${k}`)}</dt>
          <dd data-field={k}>{ENUM_GROUP[k] ? t(`${ENUM_GROUP[k]}.${String(attributes[k])}`) : display(attributes[k])}</dd>
        </div>
      ))}
    </dl>
  );
}
