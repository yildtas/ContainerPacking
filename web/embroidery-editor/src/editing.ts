// Pure geometry edits made on the canvas. The server validates and regenerates on commit.
import type { EmbroideryObject, SatinObject, Vec2 } from "./types";

/** Which polyline of an object a handle belongs to. */
export type PathRef =
  | { kind: "path" }
  | { kind: "centerline" }
  | { kind: "railA" }
  | { kind: "railB" }
  | { kind: "ring"; ring: number };

export interface Handle {
  ref: PathRef;
  index: number;
  point: Vec2;
}

const add = (a: Vec2, dx: number, dy: number): Vec2 => [a[0] + dx, a[1] + dy];
const dist = (a: Vec2, b: Vec2) => Math.hypot(a[0] - b[0], a[1] - b[1]);

export function translateObject(o: EmbroideryObject, dx: number, dy: number): EmbroideryObject {
  const mv = (p: Vec2) => add(p, dx, dy);
  const entryPoint = o.entryPoint ? mv(o.entryPoint) : null;
  switch (o.type) {
    case "run":
    case "rope":
      return { ...o, entryPoint, path: o.path.map(mv) };
    case "satin":
      return {
        ...o,
        entryPoint,
        railA: o.railA.map(mv),
        railB: o.railB.map(mv),
        rungs: o.rungs.map((r) => ({ a: mv(r.a), b: mv(r.b) })),
        centerline: o.centerline.map(mv),
      };
    case "tatami":
      return { ...o, entryPoint, region: { ...o.region, rings: o.region.rings.map((r) => r.map(mv)) } };
  }
}

/** The editable polylines of an object. Rings are closed implicitly. */
export function editablePaths(o: EmbroideryObject): { ref: PathRef; points: Vec2[]; closed: boolean }[] {
  switch (o.type) {
    case "run":
    case "rope":
      return [{ ref: { kind: "path" }, points: o.path, closed: false }];
    case "satin":
      return o.source === "stroke"
        ? [{ ref: { kind: "centerline" }, points: o.centerline, closed: false }]
        : [
            { ref: { kind: "railA" }, points: o.railA, closed: false },
            { ref: { kind: "railB" }, points: o.railB, closed: false },
          ];
    case "tatami":
      return o.region.rings.map((points, ring) => ({ ref: { kind: "ring", ring } as PathRef, points, closed: true }));
  }
}

/**
 * Handles on the editable polylines, thinned so neighbours are at least `spacingMm` apart
 * (imported curves have a vertex every few tenths of a millimetre). Ends are always kept.
 */
export function handles(o: EmbroideryObject, spacingMm: number): Handle[] {
  const result: Handle[] = [];
  for (const { ref, points, closed } of editablePaths(o)) {
    let last: Vec2 | null = null;
    points.forEach((p, index) => {
      const isEnd = !closed && (index === 0 || index === points.length - 1);
      if (isEnd || last === null || dist(p, last) >= spacingMm) {
        result.push({ ref, index, point: p });
        last = p;
      }
    });
  }
  return result;
}

function getPath(o: EmbroideryObject, ref: PathRef): Vec2[] {
  const found = editablePaths(o).find((p) => sameRef(p.ref, ref));
  return found ? found.points : [];
}

function sameRef(a: PathRef, b: PathRef) {
  return a.kind === b.kind && (a.kind !== "ring" || (b.kind === "ring" && a.ring === b.ring));
}

function setPath(o: EmbroideryObject, ref: PathRef, points: Vec2[]): EmbroideryObject {
  switch (ref.kind) {
    case "path":
      return o.type === "run" || o.type === "rope" ? { ...o, path: points } : o;
    case "centerline":
      return o.type === "satin" ? { ...o, centerline: points } : o;
    case "railA":
      return o.type === "satin" ? { ...o, railA: points } : o;
    case "railB":
      return o.type === "satin" ? { ...o, railB: points } : o;
    case "ring":
      return o.type === "tatami"
        ? { ...o, region: { ...o.region, rings: o.region.rings.map((r, i) => (i === ref.ring ? points : r)) } }
        : o;
  }
}

/** Smooth falloff: 1 at the grabbed vertex, 0 at `radius` along the line. */
export function falloff(d: number, radius: number): number {
  if (radius <= 0) return d === 0 ? 1 : 0;
  if (d >= radius) return 0;
  const t = d / radius;
  return (1 - t * t) * (1 - t * t);
}

/**
 * Drags vertex `index` of a polyline by (dx, dy); vertices within `radiusMm` along the line follow
 * with a smooth falloff (proportional editing), so a dense curve bends instead of kinking.
 * Rung ends sitting on a moved rail vertex move with it.
 */
export function dragVertex(o: EmbroideryObject, ref: PathRef, index: number, dx: number, dy: number, radiusMm: number): EmbroideryObject {
  const points = getPath(o, ref);
  if (index < 0 || index >= points.length) return o;
  const closed = ref.kind === "ring";
  const n = points.length;

  // Arc length from the grabbed vertex, both ways (wrapping for rings).
  const along = new Array<number>(n).fill(Infinity);
  along[index] = 0;
  for (const step of [1, -1]) {
    let d = 0;
    for (let k = 1; k < n; k++) {
      const i = index + step * k;
      const j = closed ? ((i % n) + n) % n : i;
      if (j < 0 || j >= n) break;
      const prev = closed ? (((j - step) % n) + n) % n : j - step;
      d += dist(points[prev], points[j]);
      if (d > radiusMm) break;
      along[j] = Math.min(along[j], d);
    }
  }

  const weights = along.map((d) => falloff(d, radiusMm));
  const moved = points.map((p, i) => (weights[i] > 0 ? add(p, dx * weights[i], dy * weights[i]) : p));
  let result = setPath(o, ref, moved);

  if (result.type === "satin" && (ref.kind === "railA" || ref.kind === "railB")) {
    const follow = (p: Vec2): Vec2 => {
      let best = -1;
      let bestD = 0.05;
      points.forEach((q, i) => {
        const d = dist(p, q);
        if (weights[i] > 0 && d <= bestD) {
          bestD = d;
          best = i;
        }
      });
      return best < 0 ? p : add(p, dx * weights[best], dy * weights[best]);
    };
    result = { ...result, rungs: result.rungs.map((r) => ({ a: follow(r.a), b: follow(r.b) })) };
  }
  return result;
}

export function nearestOnPolyline(points: Vec2[], p: Vec2): { point: Vec2; distance: number } {
  let best: Vec2 = points[0];
  let bestD = Infinity;
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1];
    const b = points[i];
    const vx = b[0] - a[0];
    const vy = b[1] - a[1];
    const len2 = vx * vx + vy * vy;
    const t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * vx + (p[1] - a[1]) * vy) / len2));
    const q: Vec2 = [a[0] + vx * t, a[1] + vy * t];
    const d = dist(p, q);
    if (d < bestD) {
      bestD = d;
      best = q;
    }
  }
  return { point: best, distance: bestD };
}

/** A rung through the clicked point: from the nearest point on rail A to the nearest on rail B. */
export function addRung(o: SatinObject, p: Vec2): SatinObject {
  if (o.source !== "rails" || o.railA.length < 2 || o.railB.length < 2) return o;
  const a = nearestOnPolyline(o.railA, p).point;
  const b = nearestOnPolyline(o.railB, a).point;
  return { ...o, rungs: [...o.rungs, { a, b }] };
}

/** Removes the rung nearest to the clicked point (if within `maxMm`). */
export function removeRung(o: SatinObject, p: Vec2, maxMm: number): SatinObject {
  let best = -1;
  let bestD = maxMm;
  o.rungs.forEach((r, i) => {
    const d = nearestOnPolyline([r.a, r.b], p).distance;
    if (d <= bestD) {
      bestD = d;
      best = i;
    }
  });
  return best < 0 ? o : { ...o, rungs: o.rungs.filter((_, i) => i !== best) };
}

export function setEntryPoint(o: EmbroideryObject, p: Vec2 | null): EmbroideryObject {
  return { ...o, entryPoint: p };
}
