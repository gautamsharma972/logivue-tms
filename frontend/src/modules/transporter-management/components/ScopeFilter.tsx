import { Field } from '../../../shared/ui';
import type { RankingScopeParams } from '../api';

/** Period and scope controls shared by ranking and benchmarking. Empty numeric fields mean "not set". */
export function ScopeFilter({
  value,
  onChange,
  showTransporterCategory = true,
}: {
  value: RankingScopeParams;
  onChange: (next: RankingScopeParams) => void;
  showTransporterCategory?: boolean;
}) {
  const num = (raw: string) => (raw.trim() === '' ? undefined : Number(raw));
  const text = (raw: string) => (raw.trim() === '' ? undefined : raw.trim());

  return (
    <div className="filter-grid">
      <Field label="From">
        <input type="date" value={value.from} onChange={(e) => onChange({ ...value, from: e.target.value })} />
      </Field>
      <Field label="To">
        <input type="date" value={value.to} onChange={(e) => onChange({ ...value, to: e.target.value })} />
      </Field>
      <Field label="Origin location ref">
        <input inputMode="numeric" value={value.originLocationReference ?? ''} onChange={(e) => onChange({ ...value, originLocationReference: num(e.target.value) })} />
      </Field>
      <Field label="Destination location ref">
        <input inputMode="numeric" value={value.destinationLocationReference ?? ''} onChange={(e) => onChange({ ...value, destinationLocationReference: num(e.target.value) })} />
      </Field>
      <Field label="Service type (lane)">
        <input value={value.serviceType ?? ''} placeholder="FTL" onChange={(e) => onChange({ ...value, serviceType: text(e.target.value) })} />
      </Field>
      <Field label="Vehicle type ref">
        <input inputMode="numeric" value={value.vehicleTypeReference ?? ''} onChange={(e) => onChange({ ...value, vehicleTypeReference: num(e.target.value) })} />
      </Field>
      <Field label="Region (state)">
        <input value={value.region ?? ''} onChange={(e) => onChange({ ...value, region: text(e.target.value) })} />
      </Field>
      {showTransporterCategory && (
        <Field label="Transporter category ID">
          <input inputMode="numeric" value={value.transporterTypeId ?? ''} onChange={(e) => onChange({ ...value, transporterTypeId: num(e.target.value) })} />
        </Field>
      )}
    </div>
  );
}
