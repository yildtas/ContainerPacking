import { useEffect, useState } from "react";

interface NumberFieldProps {
  label: string;
  value: number;
  step?: number;
  min?: number;
  max?: number;
  unit?: string;
  onCommit: (value: number) => void;
}

/** Edits a number locally and commits on Enter or blur, so each keystroke is not a server edit. */
export function NumberField({ label, value, step = 0.1, min, max, unit, onCommit }: NumberFieldProps) {
  const [draft, setDraft] = useState(format(value));
  useEffect(() => setDraft(format(value)), [value]);

  const commit = () => {
    const parsed = Number(draft.replace(",", "."));
    if (!Number.isFinite(parsed) || (min !== undefined && parsed < min) || (max !== undefined && parsed > max)) {
      setDraft(format(value));
      return;
    }
    if (parsed !== value) onCommit(parsed);
  };

  return (
    <label className="field">
      <span>{label}</span>
      <span className="field-input">
        <input
          type="number"
          inputMode="decimal"
          value={draft}
          step={step}
          min={min}
          max={max}
          onChange={(e) => setDraft(e.target.value)}
          onBlur={commit}
          onKeyDown={(e) => {
            if (e.key === "Enter") (e.target as HTMLInputElement).blur();
            if (e.key === "Escape") setDraft(format(value));
          }}
        />
        {unit && <small>{unit}</small>}
      </span>
    </label>
  );
}

function format(v: number) {
  return String(Math.round(v * 1000) / 1000);
}

export function CheckField({ label, value, onCommit }: { label: string; value: boolean; onCommit: (v: boolean) => void }) {
  return (
    <label className="field field-check">
      <input type="checkbox" checked={value} onChange={(e) => onCommit(e.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

export function SelectField<T extends string | number>({
  label,
  value,
  options,
  onCommit,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onCommit: (v: T) => void;
}) {
  return (
    <label className="field">
      <span>{label}</span>
      <select
        value={String(value)}
        onChange={(e) => {
          const opt = options.find((o) => String(o.value) === e.target.value);
          if (opt) onCommit(opt.value);
        }}
      >
        {options.map((o) => (
          <option key={String(o.value)} value={String(o.value)}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  );
}
