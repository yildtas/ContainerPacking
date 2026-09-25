import { useState } from "react";
import type { StitchProfile } from "../types";
import { NumberField } from "./Fields";

interface Props {
  base: StitchProfile;
  onSave: (profile: StitchProfile) => Promise<void>;
}

const FIELDS: { key: keyof StitchProfile; label: string; step: number }[] = [
  { key: "satinSpacingMm", label: "Satin sıklığı", step: 0.01 },
  { key: "satinPullMm", label: "Satin çekme payı", step: 0.05 },
  { key: "tatamiRowSpacingMm", label: "Tatami sıra aralığı", step: 0.01 },
  { key: "tatamiStitchLengthMm", label: "Tatami dikiş boyu", step: 0.1 },
  { key: "tatamiPullMm", label: "Tatami çekme payı", step: 0.05 },
  { key: "runStitchLengthMm", label: "Run dikiş boyu", step: 0.1 },
  { key: "ropeSpacingMm", label: "Halat sıklığı", step: 0.01 },
];

/**
 * Saves the values chosen from the calibration sew-out as a named profile (profiles/<id>.json on
 * the server), starting from the profile currently applied.
 */
export function ProfileForm({ base, onSave }: Props) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<StitchProfile>(base);
  const [saving, setSaving] = useState(false);
  const idValid = /^[a-z0-9][a-z0-9-]{1,39}$/.test(draft.id);

  if (!open) {
    return (
      <button
        onClick={() => {
          setDraft({ ...base, id: "", name: "", description: "Kalibrasyon dikiminden seçilen değerler." });
          setOpen(true);
        }}
        title="Kalibrasyon sayfasından seçtiğiniz değerleri yeni bir profil olarak kaydedin"
      >
        Yeni profil…
      </button>
    );
  }

  return (
    <div className="profile-form">
      <label className="field">
        <span>Kimlik</span>
        <input value={draft.id} placeholder="ornek-kumas" onChange={(e) => setDraft({ ...draft, id: e.target.value.toLowerCase() })} />
      </label>
      <label className="field">
        <span>Ad</span>
        <input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
      </label>
      {FIELDS.map((f) => (
        <NumberField
          key={f.key}
          label={f.label}
          value={draft[f.key] as number}
          step={f.step}
          min={0.05}
          max={12}
          unit="mm"
          onCommit={(v) => setDraft({ ...draft, [f.key]: v })}
        />
      ))}
      {!idValid && draft.id && <p className="hint">Kimlik: küçük harf, rakam ve tire (2–40).</p>}
      <div className="button-row">
        <button
          className="primary"
          disabled={!idValid || !draft.name.trim() || saving}
          onClick={async () => {
            setSaving(true);
            try {
              await onSave(draft);
              setOpen(false);
            } finally {
              setSaving(false);
            }
          }}
        >
          Kaydet
        </button>
        <button onClick={() => setOpen(false)}>Vazgeç</button>
      </div>
    </div>
  );
}
