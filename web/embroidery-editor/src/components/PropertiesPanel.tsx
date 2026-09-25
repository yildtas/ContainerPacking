import { useEffect, useState } from "react";
import type { EmbroideryObject, EmbroideryThread, StitchType } from "../types";
import { CheckField, NumberField, SelectField } from "./Fields";

interface Props {
  item: EmbroideryObject;
  threads: EmbroideryThread[];
  onChange: (item: EmbroideryObject) => void;
  onConvert: (type: StitchType) => void;
}

export const typeLabels: Record<StitchType, string> = { run: "Run (düz dikiş)", satin: "Satin", tatami: "Tatami (dolgu)", rope: "Halat (burgu)" };

export function PropertiesPanel({ item, threads, onChange, onConvert }: Props) {
  const [name, setName] = useState(item.name);
  useEffect(() => setName(item.name), [item.name]);

  return (
    <div className="panel-body">
      <label className="field">
        <span>Ad</span>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          onBlur={() => name.trim() && name !== item.name && onChange({ ...item, name: name.trim() })}
          onKeyDown={(e) => e.key === "Enter" && (e.target as HTMLInputElement).blur()}
        />
      </label>
      <SelectField
        label="Tür"
        value={item.type}
        options={(Object.keys(typeLabels) as StitchType[]).map((t) => ({ value: t, label: typeLabels[t] }))}
        onCommit={(t) => t !== item.type && onConvert(t)}
      />
      <SelectField
        label="İplik"
        value={item.threadIndex}
        options={threads.map((t, i) => ({ value: i, label: `${i + 1}. ${t.name} (${t.colorHex})` }))}
        onCommit={(threadIndex) => onChange({ ...item, threadIndex })}
      />
      <CheckField label="Dikilsin" value={item.visible} onCommit={(visible) => onChange({ ...item, visible })} />
      <ParameterFields item={item} onChange={onChange} />
    </div>
  );
}

function ParameterFields({ item, onChange }: { item: EmbroideryObject; onChange: (item: EmbroideryObject) => void }) {
  switch (item.type) {
    case "run": {
      const p = item.parameters;
      const set = (patch: Partial<typeof p>) => onChange({ ...item, parameters: { ...p, ...patch } });
      return (
        <fieldset>
          <legend>Run</legend>
          <NumberField label="Dikiş boyu" unit="mm" value={p.stitchLengthMm} min={0.5} max={12} onCommit={(v) => set({ stitchLengthMm: v })} />
          <NumberField label="Köşe açısı" unit="°" step={5} value={p.cornerAngleDeg} min={0} max={180} onCommit={(v) => set({ cornerAngleDeg: v })} />
          <SelectField
            label="Tekrar"
            value={p.repeats}
            options={[{ value: 1, label: "Tek" }, { value: 3, label: "Üçlü (bean)" }, { value: 5, label: "Beşli" }]}
            onCommit={(v) => set({ repeats: v })}
          />
        </fieldset>
      );
    }
    case "satin": {
      const p = item.parameters;
      const set = (patch: Partial<typeof p>) => onChange({ ...item, parameters: { ...p, ...patch } });
      const setU = (patch: Partial<typeof p.underlay>) => set({ underlay: { ...p.underlay, ...patch } });
      return (
        <>
          <fieldset>
            <legend>Kolon</legend>
            <p className="hint">
              {item.source === "stroke"
                ? "Orta çizgi + kalınlık"
                : `İki kenar (rail)${item.rungs.length ? `, ${item.rungs.length} rung` : ""}`}
            </p>
            {item.source === "stroke" && (
              <>
                <NumberField label="Kalınlık" unit="mm" step={0.1} value={item.widthMm} min={0.3} max={20} onCommit={(v) => onChange({ ...item, widthMm: v })} />
                <NumberField label="Baş incelmesi" unit="mm" step={0.5} value={item.startTaperMm} min={0} max={500} onCommit={(v) => onChange({ ...item, startTaperMm: v })} />
                <NumberField label="Son incelmesi" unit="mm" step={0.5} value={item.endTaperMm} min={0} max={500} onCommit={(v) => onChange({ ...item, endTaperMm: v })} />
              </>
            )}
          </fieldset>
          <fieldset>
            <legend>Satin</legend>
            <NumberField label="Sıklık" unit="mm" step={0.05} value={p.spacingMm} min={0.15} max={5} onCommit={(v) => set({ spacingMm: v })} />
            <NumberField label="Pull comp." unit="mm" step={0.05} value={p.pullCompensationMm} min={-1} max={3} onCommit={(v) => set({ pullCompensationMm: v })} />
            <NumberField label="Push comp." unit="mm" step={0.05} value={p.pushCompensationMm} min={0} max={5} onCommit={(v) => set({ pushCompensationMm: v })} />
            <NumberField label="Split genişliği" unit="mm" step={0.5} value={p.maxWidthMm} min={1} max={20} onCommit={(v) => set({ maxWidthMm: v })} />
            <SelectField
              label="Kısa dikiş"
              value={p.shortStitch}
              options={[{ value: "none", label: "Kapalı" }, { value: "innerOnly", label: "İç kenarda" }]}
              onCommit={(v) => set({ shortStitch: v })}
            />
            <NumberField label="Köşe bölme açısı" unit="°" step={5} value={p.cornerSplitAngleDeg} min={20} max={180} onCommit={(v) => set({ cornerSplitAngleDeg: v })} />
          </fieldset>
          <fieldset>
            <legend>Underlay</legend>
            <CheckField label="Merkez yürüyüş" value={p.underlay.centerWalk} onCommit={(v) => setU({ centerWalk: v })} />
            <CheckField label="Kenar yürüyüş" value={p.underlay.edgeWalk} onCommit={(v) => setU({ edgeWalk: v })} />
            <CheckField label="Zikzak" value={p.underlay.zigZag} onCommit={(v) => setU({ zigZag: v })} />
            <NumberField label="Kenar içeriği" unit="mm" value={p.underlay.edgeInsetMm} min={0} max={5} onCommit={(v) => setU({ edgeInsetMm: v })} />
            <NumberField label="Zikzak aralığı" unit="mm" value={p.underlay.zigZagSpacingMm} min={0.5} max={10} onCommit={(v) => setU({ zigZagSpacingMm: v })} />
          </fieldset>
        </>
      );
    }
    case "rope": {
      const p = item.parameters;
      const set = (patch: Partial<typeof p>) => onChange({ ...item, parameters: { ...p, ...patch } });
      return (
        <fieldset>
          <legend>Halat</legend>
          <NumberField label="Bant genişliği" unit="mm" step={0.5} value={item.widthMm} min={1} max={20} onCommit={(v) => onChange({ ...item, widthMm: v })} />
          <NumberField label="Adım" unit="mm" step={0.5} value={p.pitchMm} min={0.8} max={20} onCommit={(v) => set({ pitchMm: v })} />
          <NumberField label="Tel eğimi (boy)" unit="mm" step={0.5} value={p.strandLengthMm} min={1} max={40} onCommit={(v) => set({ strandLengthMm: v })} />
          <NumberField label="Sıklık" unit="mm" step={0.05} value={p.spacingMm} min={0.15} max={5} onCommit={(v) => set({ spacingMm: v })} />
          <NumberField label="Bindirme" step={0.05} value={p.overlapFactor} min={0.5} max={2} onCommit={(v) => set({ overlapFactor: v })} />
          <NumberField label="Pull comp." unit="mm" step={0.05} value={p.pullCompensationMm} min={-1} max={3} onCommit={(v) => set({ pullCompensationMm: v })} />
          <SelectField
            label="Burgu"
            value={p.twist}
            options={[{ value: "s", label: "S" }, { value: "z", label: "Z" }]}
            onCommit={(v) => set({ twist: v })}
          />
          <CheckField label="Orta underlay" value={p.centerUnderlay} onCommit={(v) => set({ centerUnderlay: v })} />
        </fieldset>
      );
    }
    case "tatami": {
      const p = item.parameters;
      const set = (patch: Partial<typeof p>) => onChange({ ...item, parameters: { ...p, ...patch } });
      const setU = (patch: Partial<typeof p.underlay>) => set({ underlay: { ...p.underlay, ...patch } });
      return (
        <>
          <fieldset>
            <legend>Tatami</legend>
            <NumberField label="Açı" unit="°" step={5} value={p.angleDeg} min={-360} max={360} onCommit={(v) => set({ angleDeg: v })} />
            <NumberField label="Satır aralığı" unit="mm" step={0.05} value={p.rowSpacingMm} min={0.15} max={5} onCommit={(v) => set({ rowSpacingMm: v })} />
            <NumberField label="Dikiş boyu" unit="mm" value={p.stitchLengthMm} min={0.5} max={12} onCommit={(v) => set({ stitchLengthMm: v })} />
            <NumberField label="Kaydırma (stagger)" step={0.05} value={p.staggerFraction} min={0} max={1} onCommit={(v) => set({ staggerFraction: v })} />
            <NumberField label="Pull comp." unit="mm" step={0.05} value={p.pullCompensationMm} min={-1} max={3} onCommit={(v) => set({ pullCompensationMm: v })} />
            <NumberField label="Kenar içeriği" unit="mm" step={0.05} value={p.edgeInsetMm} min={0} max={5} onCommit={(v) => set({ edgeInsetMm: v })} />
          </fieldset>
          <fieldset>
            <legend>Underlay</legend>
            <CheckField label="Kenar run" value={p.underlay.edgeRun} onCommit={(v) => setU({ edgeRun: v })} />
            <CheckField label="Dolgu underlay" value={p.underlay.fill} onCommit={(v) => setU({ fill: v })} />
            <NumberField label="İçeri çekme" unit="mm" value={p.underlay.insetMm} min={0} max={10} onCommit={(v) => setU({ insetMm: v })} />
            <NumberField label="Satır aralığı" unit="mm" value={p.underlay.rowSpacingMm} min={0.5} max={10} onCommit={(v) => setU({ rowSpacingMm: v })} />
          </fieldset>
        </>
      );
    }
  }
}
