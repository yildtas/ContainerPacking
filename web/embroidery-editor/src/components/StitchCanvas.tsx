import { useCallback, useEffect, useRef, useState } from "react";
import { SegmentKind, type Simulation } from "../simulation";
import type { Design, EmbroideryObject, Vec2 } from "../types";

interface Props {
  sim: Simulation | null;
  colors: string[];
  design: Design | null;
  selectedId: string | null;
  /** Number of segments to draw (simulator position). */
  progress: number;
  showJumps: boolean;
  onSelect: (objectId: string | null) => void;
}

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

function objectOutlines(o: EmbroideryObject): Vec2[][] {
  switch (o.type) {
    case "run":
      return [o.path];
    case "satin":
      return [o.railA, o.railB];
    case "tatami":
      return o.region.rings.map((r) => [...r, r[0]]);
  }
}

export function StitchCanvas({ sim, colors, design, selectedId, progress, showJumps, onSelect }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 800, h: 600 });
  const [view, setView] = useState<View>({ scale: 4, x: 40, y: 40 });
  const fittedFor = useRef<string | null>(null);
  const drag = useRef<{ x: number; y: number; vx: number; vy: number; moved: boolean } | null>(null);

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
    ctx.fillStyle = getComputedStyle(canvas).getPropertyValue("--canvas-bg").trim() || "#f4f1ea";
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

    // Selected object's source geometry.
    const selected = design.objects.find((o) => o.id === selectedId);
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
  }, [sim, colors, design, selectedId, progress, showJumps, view, size]);

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

  return (
    <div className="canvas-wrap" ref={wrapRef}>
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
          drag.current = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, moved: false };
        }}
        onPointerMove={(e) => {
          const d = drag.current;
          if (!d) return;
          const dx = e.clientX - d.x, dy = e.clientY - d.y;
          if (Math.abs(dx) + Math.abs(dy) > 3) d.moved = true;
          if (d.moved) setView((v) => ({ ...v, x: d.vx + dx, y: d.vy + dy }));
        }}
        onPointerUp={(e) => {
          const d = drag.current;
          drag.current = null;
          if (d && !d.moved) {
            const [mx, my] = toMm(e);
            onSelect(pick(mx, my));
          }
        }}
      />
      <button className="fit-button" onClick={fit} title="Tasarımı sığdır">
        Sığdır
      </button>
    </div>
  );
}
