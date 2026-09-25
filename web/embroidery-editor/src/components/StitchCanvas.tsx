import { useCallback, useEffect, useRef, useState } from "react";
import { SegmentKind, type Simulation } from "../simulation";
import type { Design, EmbroideryObject, Vec2 } from "../types";
import {
  addRung,
  dragVertex,
  handles,
  removeRung,
  setEntryPoint,
  translateObject,
  type Handle,
} from "../editing";

export type Tool = "select" | "move" | "nodes" | "rungAdd" | "rungRemove" | "entry" | "split";

const TOOLS: { id: Tool; label: string; title: string; satinRailsOnly?: boolean }[] = [
  { id: "select", label: "Seç", title: "Tıkla: nesne seç, sürükle: kaydır" },
  { id: "move", label: "Taşı", title: "Seçili nesneyi sürükleyerek taşı" },
  { id: "nodes", label: "Düğüm", title: "Kolları sürükle; yakındaki noktalar yumuşak geçişle takip eder" },
  { id: "rungAdd", label: "Rung +", title: "Tıklanan yerden iki rayı birleştiren rung ekle", satinRailsOnly: true },
  { id: "rungRemove", label: "Rung −", title: "Tıklanan yere en yakın rung'u sil", satinRailsOnly: true },
  { id: "entry", label: "Giriş", title: "Nesnenin dikişe başlayacağı noktayı seç" },
  { id: "split", label: "Böl", title: "Nesneyi tıklanan noktadan ikiye böl" },
];

interface Props {
  sim: Simulation | null;
  colors: string[];
  design: Design | null;
  selectedId: string | null;
  /** Number of segments to draw (simulator position). */
  progress: number;
  showJumps: boolean;
  /** Fabric colour behind the stitches. */
  background: string;
  onSelect: (objectId: string | null) => void;
  /** Commits an edited object (move, node drag, rungs, entry point). */
  onEdit?: (item: EmbroideryObject) => void;
  /** Splits the object at a point (done on the server). */
  onSplit?: (objectId: string, at: Vec2) => void;
}

type Gesture =
  | { kind: "pan"; x: number; y: number; vx: number; vy: number; moved: boolean }
  | { kind: "move"; x: number; y: number; start: Vec2; original: EmbroideryObject; moved: boolean }
  | { kind: "node"; x: number; y: number; start: Vec2; handle: Handle; original: EmbroideryObject; moved: boolean };

interface View {
  scale: number; // px per mm
  x: number; // screen px of design x=0
  y: number;
}

const THREAD_WIDTH_MM = 0.32;

function shade(hex: string, factor: number): string {
  const n = parseInt(hex.slice(1), 16);
  const r = Math.round(((n >> 16) & 255) * factor);
  const g = Math.round(((n >> 8) & 255) * factor);
  const b = Math.round((n & 255) * factor);
  return `rgb(${r},${g},${b})`;
}

/** Handle spacing: about 14 px on screen, never below 0.5 mm. */
function handleSpacing(scale: number) {
  return Math.max(0.5, 14 / scale);
}

function objectOutlines(o: EmbroideryObject): Vec2[][] {
  switch (o.type) {
    case "run":
      return [o.path];
    case "satin":
      return o.source === "stroke" ? [o.centerline] : [o.railA, o.railB, ...o.rungs.map((r) => [r.a, r.b])];
    case "tatami":
      return o.region.rings.map((r) => [...r, r[0]]);
    case "rope":
      return [o.path];
  }
}

export function StitchCanvas({ sim, colors, design, selectedId, progress, showJumps, background, onSelect, onEdit, onSplit }: Props) {
  const [tool, setTool] = useState<Tool>("select");
  const [falloffMm, setFalloffMm] = useState(3);
  const [draft, setDraft] = useState<EmbroideryObject | null>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 800, h: 600 });
  const [view, setView] = useState<View>({ scale: 4, x: 40, y: 40 });
  const fittedFor = useRef<string | null>(null);
  const drag = useRef<Gesture | null>(null);

  // A committed edit comes back as a new design: drop the local draft then.
  useEffect(() => setDraft(null), [design]);

  useEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const ro = new ResizeObserver(([entry]) => {
      setSize({ w: Math.max(100, entry.contentRect.width), h: Math.max(100, entry.contentRect.height) });
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const fit = useCallback(() => {
    if (!sim?.bounds || !design) return;
    const b = sim.bounds;
    const cx = (b.minX + b.maxX) / 2;
    const cy = (b.minY + b.maxY) / 2;
    const w = Math.max(design.hoop.widthMm, b.maxX - b.minX) * 1.08;
    const h = Math.max(design.hoop.heightMm, b.maxY - b.minY) * 1.08;
    const scale = Math.min(size.w / w, size.h / h);
    setView({ scale, x: size.w / 2 - cx * scale, y: size.h / 2 - cy * scale });
  }, [sim, design, size]);

  // Fit once per opened design; later edits keep the user's zoom.
  useEffect(() => {
    if (design && sim?.bounds && fittedFor.current !== design.id) {
      fittedFor.current = design.id;
      fit();
    }
  }, [design, sim, fit]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(size.w * dpr);
    canvas.height = Math.round(size.h * dpr);
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = background;
    ctx.fillRect(0, 0, size.w, size.h);
    if (!sim || !design) return;

    const { scale, x: ox, y: oy } = view;
    const X = (mm: number) => ox + mm * scale;
    const Y = (mm: number) => oy + mm * scale;

    // Hoop, centred on the design like the machine does.
    if (sim.bounds) {
      const cx = (sim.bounds.minX + sim.bounds.maxX) / 2;
      const cy = (sim.bounds.minY + sim.bounds.maxY) / 2;
      const { widthMm, heightMm } = design.hoop;
      ctx.save();
      ctx.strokeStyle = "rgba(90,90,90,0.55)";
      ctx.setLineDash([6, 5]);
      ctx.lineWidth = 1;
      const r = Math.min(widthMm, heightMm) * 0.08 * scale;
      ctx.beginPath();
      ctx.roundRect(X(cx - widthMm / 2), Y(cy - heightMm / 2), widthMm * scale, heightMm * scale, r);
      ctx.stroke();
      ctx.restore();
    }

    const count = Math.min(progress, sim.count);
    const c = sim.coords;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    const threadPx = Math.max(0.6, THREAD_WIDTH_MM * scale);

    // Batch consecutive segments that share thread and kind into one path.
    const strokeRun = (from: number, to: number) => {
      const k = sim.kind[from];
      if (k === SegmentKind.Jump) {
        if (!showJumps) return;
        ctx.save();
        ctx.setLineDash([3, 4]);
        ctx.strokeStyle = "rgba(60,60,60,0.45)";
        ctx.lineWidth = 1;
      }
      ctx.beginPath();
      for (let i = from; i < to; i++) {
        const j = i * 4;
        if (i === from || c[j] !== c[j - 2] || c[j + 1] !== c[j - 1]) ctx.moveTo(X(c[j]), Y(c[j + 1]));
        ctx.lineTo(X(c[j + 2]), Y(c[j + 3]));
      }
      if (k === SegmentKind.Jump) {
        ctx.stroke();
        ctx.restore();
        return;
      }
      const color = colors[sim.thread[from]] ?? "#000000";
      const width = k === SegmentKind.Travel ? threadPx * 0.8 : threadPx;
      ctx.strokeStyle = shade(color, 0.55);
      ctx.lineWidth = width * 1.35;
      ctx.stroke();
      ctx.strokeStyle = color;
      ctx.lineWidth = width;
      ctx.stroke();
    };

    let start = 0;
    for (let i = 1; i <= count; i++) {
      if (i === count || sim.kind[i] !== sim.kind[start] || sim.thread[i] !== sim.thread[start]) {
        if (count > 0) strokeRun(start, i);
        start = i;
      }
    }

    // Selected object's source geometry (the draft while dragging).
    const selected = draft ?? design.objects.find((o) => o.id === selectedId);
    if (selected) {
      ctx.save();
      ctx.strokeStyle = "#e0457b";
      ctx.lineWidth = 1.5;
      ctx.setLineDash([]);
      for (const line of objectOutlines(selected)) {
        ctx.beginPath();
        line.forEach(([x, y], i) => (i === 0 ? ctx.moveTo(X(x), Y(y)) : ctx.lineTo(X(x), Y(y))));
        ctx.stroke();
      }
      if (tool === "nodes") {
        ctx.fillStyle = "#fff";
        for (const h of handles(selected, handleSpacing(view.scale))) {
          ctx.beginPath();
          ctx.rect(X(h.point[0]) - 4, Y(h.point[1]) - 4, 8, 8);
          ctx.fill();
          ctx.stroke();
        }
      }
      if (selected.entryPoint) {
        const [ex, ey] = selected.entryPoint;
        ctx.fillStyle = "#1f9d55";
        ctx.strokeStyle = "#fff";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(X(ex), Y(ey), 6, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
      }
      ctx.restore();
    }

    // Needle position while simulating.
    if (count > 0 && count < sim.count) {
      const j = (count - 1) * 4;
      ctx.save();
      ctx.fillStyle = "#e0457b";
      ctx.strokeStyle = "#fff";
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.arc(X(c[j + 2]), Y(c[j + 3]), 5, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();
      ctx.restore();
    }
  }, [sim, colors, design, selectedId, progress, showJumps, background, view, size, draft, tool]);

  const toMm = (e: { clientX: number; clientY: number }) => {
    const rect = canvasRef.current!.getBoundingClientRect();
    return [(e.clientX - rect.left - view.x) / view.scale, (e.clientY - rect.top - view.y) / view.scale] as const;
  };

  const pick = (mx: number, my: number) => {
    if (!sim) return null;
    const limit = 8 / view.scale;
    let best = limit * limit;
    let hit = -1;
    const c = sim.coords;
    for (let i = 0; i < Math.min(progress, sim.count); i++) {
      if (sim.kind[i] === SegmentKind.Jump) continue;
      const j = i * 4;
      const ax = c[j], ay = c[j + 1], bx = c[j + 2], by = c[j + 3];
      const dx = bx - ax, dy = by - ay;
      const len2 = dx * dx + dy * dy;
      const t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((mx - ax) * dx + (my - ay) * dy) / len2));
      const px = ax + dx * t - mx, py = ay + dy * t - my;
      const d2 = px * px + py * py;
      if (d2 <= best) {
        best = d2;
        hit = i; // later segments are drawn on top, so prefer them
      }
    }
    return hit < 0 ? null : sim.objectIds[sim.object[hit]];
  };

  const selectedObject = design?.objects.find((o) => o.id === selectedId) ?? null;
  const railsSatin = selectedObject?.type === "satin" && selectedObject.source === "rails";
  return (
    <div
      className="canvas-wrap"
      ref={wrapRef}
      data-tool={tool}
      data-design={design?.id}
      data-view={`${view.scale} ${view.x} ${view.y}`}
    >
      <div className="canvas-tools" role="toolbar" aria-label="Düzenleme araçları">
        {TOOLS.map((t) => (
          <button
            key={t.id}
            className={tool === t.id ? "active" : ""}
            title={t.title}
            aria-pressed={tool === t.id}
            disabled={t.id !== "select" && (!selectedObject || (t.satinRailsOnly && !railsSatin))}
            onClick={() => setTool(t.id)}
          >
            {t.label}
          </button>
        ))}
        {tool === "nodes" && (
          <label className="falloff" title="Sürüklenen noktanın çevresinde bu uzunluk boyunca noktalar yumuşak geçişle takip eder">
            Etki {falloffMm} mm
            <input type="range" min={0} max={15} step={0.5} value={falloffMm} onChange={(e) => setFalloffMm(Number(e.target.value))} />
          </label>
        )}
        {tool === "entry" && selectedObject?.entryPoint && (
          <button onClick={() => onEdit?.(setEntryPoint(selectedObject, null))} title="Giriş noktasını otomatiğe döndür">
            Girişi sıfırla
          </button>
        )}
      </div>
      <canvas
        ref={canvasRef}
        style={{ width: size.w, height: size.h }}
        onWheel={(e) => {
          const rect = canvasRef.current!.getBoundingClientRect();
          const sx = e.clientX - rect.left;
          const sy = e.clientY - rect.top;
          const factor = Math.exp(-e.deltaY * 0.0015);
          setView((v) => {
            const scale = Math.min(200, Math.max(0.2, v.scale * factor));
            const k = scale / v.scale;
            return { scale, x: sx - (sx - v.x) * k, y: sy - (sy - v.y) * k };
          });
        }}
        onPointerDown={(e) => {
          (e.target as Element).setPointerCapture(e.pointerId);
          const [mx, my] = toMm(e);
          const current = design?.objects.find((o) => o.id === selectedId);
          if (current && tool === "move") {
            drag.current = { kind: "move", x: e.clientX, y: e.clientY, start: [mx, my], original: current, moved: false };
            return;
          }
          if (current && tool === "nodes") {
            const grab = 10 / view.scale;
            let best: Handle | null = null;
            let bestD = grab;
            for (const h of handles(current, handleSpacing(view.scale))) {
              const d = Math.hypot(h.point[0] - mx, h.point[1] - my);
              if (d <= bestD) {
                bestD = d;
                best = h;
              }
            }
            if (best) {
              drag.current = { kind: "node", x: e.clientX, y: e.clientY, start: [mx, my], handle: best, original: current, moved: false };
              return;
            }
          }
          drag.current = { kind: "pan", x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, moved: false };
        }}
        onPointerMove={(e) => {
          const d = drag.current;
          if (!d) return;
          const dx = e.clientX - d.x, dy = e.clientY - d.y;
          if (Math.abs(dx) + Math.abs(dy) > 3) d.moved = true;
          if (!d.moved) return;
          if (d.kind === "pan") {
            setView((v) => ({ ...v, x: d.vx + dx, y: d.vy + dy }));
            return;
          }
          const [mx, my] = toMm(e);
          const ddx = mx - d.start[0], ddy = my - d.start[1];
          setDraft(
            d.kind === "move"
              ? translateObject(d.original, ddx, ddy)
              : dragVertex(d.original, d.handle.ref, d.handle.index, ddx, ddy, falloffMm),
          );
        }}
        onPointerUp={(e) => {
          const d = drag.current;
          drag.current = null;
          if (!d) return;
          if (d.moved) {
            if (d.kind !== "pan" && draft) onEdit?.(draft);
            return;
          }
          const [mx, my] = toMm(e);
          const p: Vec2 = [mx, my];
          const current = design?.objects.find((o) => o.id === selectedId);
          if (current && tool === "rungAdd" && current.type === "satin") onEdit?.(addRung(current, p));
          else if (current && tool === "rungRemove" && current.type === "satin") onEdit?.(removeRung(current, p, 10 / view.scale));
          else if (current && tool === "entry") onEdit?.(setEntryPoint(current, p));
          else if (current && tool === "split") onSplit?.(current.id, p);
          else onSelect(pick(mx, my));
        }}
      />
      <button className="fit-button" onClick={fit} title="Tasarımı sığdır">
        Sığdır
      </button>
    </div>
  );
}
