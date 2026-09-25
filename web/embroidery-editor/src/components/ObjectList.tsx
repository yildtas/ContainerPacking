import type { Design } from "../types";

interface Props {
  design: Design;
  selectedId: string | null;
  onSelect: (id: string) => void;
  onMove: (id: string, delta: -1 | 1) => void;
  onToggle: (id: string) => void;
  onDelete: (id: string) => void;
}

const typeShort = { run: "R", satin: "S", tatami: "T" } as const;

/** Objects in sew order; the order here is the order the machine stitches. */
export function ObjectList({ design, selectedId, onSelect, onMove, onToggle, onDelete }: Props) {
  if (design.objects.length === 0) return <p className="empty">Nesne yok.</p>;
  return (
    <ol className="object-list">
      {design.objects.map((o, i) => (
        <li key={o.id} className={o.id === selectedId ? "selected" : undefined} onClick={() => onSelect(o.id)}>
          <span className="swatch" style={{ background: design.threads[o.threadIndex]?.colorHex }} />
          <span className={`type-badge type-${o.type}`} title={o.type}>
            {typeShort[o.type]}
          </span>
          <span className={o.visible ? "name" : "name muted"} title={o.name}>
            {o.name}
          </span>
          <span className="row-actions" onClick={(e) => e.stopPropagation()}>
            <button title="Yukarı" disabled={i === 0} onClick={() => onMove(o.id, -1)}>
              ↑
            </button>
            <button title="Aşağı" disabled={i === design.objects.length - 1} onClick={() => onMove(o.id, 1)}>
              ↓
            </button>
            <button title={o.visible ? "Gizle" : "Göster"} onClick={() => onToggle(o.id)}>
              {o.visible ? "◉" : "○"}
            </button>
            <button title="Sil" onClick={() => onDelete(o.id)}>
              ✕
            </button>
          </span>
        </li>
      ))}
    </ol>
  );
}
