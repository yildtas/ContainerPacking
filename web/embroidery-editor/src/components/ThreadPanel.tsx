import { useEffect, useRef, useState } from "react";
import type { EmbroideryThread } from "../types";

interface Props {
  threads: EmbroideryThread[];
  onChange: (threads: EmbroideryThread[]) => void;
}

/**
 * Thread palette. The colour picker fires continuously while dragging, so edits are
 * kept locally and committed once the user pauses.
 */
export function ThreadPanel({ threads, onChange }: Props) {
  const [draft, setDraft] = useState(threads);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => setDraft(threads), [threads]);
  useEffect(() => () => window.clearTimeout(timer.current), []);

  const update = (next: EmbroideryThread[], immediate = false) => {
    setDraft(next);
    window.clearTimeout(timer.current);
    if (immediate) onChange(next);
    else timer.current = window.setTimeout(() => onChange(next), 400);
  };

  return (
    <div className="thread-list">
      {draft.map((t, i) => (
        <label key={i} className="thread-row">
          <input
            type="color"
            value={t.colorHex.toLowerCase()}
            onChange={(e) => {
              const next = draft.slice();
              next[i] = { ...t, colorHex: e.target.value.toUpperCase() };
              update(next);
            }}
          />
          <span>
            {i + 1}. {t.name}
          </span>
          <code>{t.colorHex}</code>
        </label>
      ))}
      <button
        className="link-button"
        onClick={() => update([...draft, { name: `İplik ${draft.length + 1}`, colorHex: "#000000" }], true)}
      >
        + İplik ekle
      </button>
    </div>
  );
}
