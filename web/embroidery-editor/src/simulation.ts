import { Cmd, type Preview } from "./types";

export const SegmentKind = { Stitch: 0, Travel: 1, Jump: 2 } as const;

/**
 * The plan flattened into needle moves, in sew order. Stored in typed arrays so that
 * hundreds of thousands of stitches can be redrawn every animation frame.
 */
export interface Simulation {
  count: number;
  /** x0, y0, x1, y1 per segment (mm). */
  coords: Float32Array;
  kind: Uint8Array;
  thread: Uint16Array;
  /** Index into {@link objectIds}. */
  object: Int32Array;
  objectIds: string[];
  /** Segment index at which each colour change happens. */
  colorChanges: number[];
  bounds: { minX: number; minY: number; maxX: number; maxY: number } | null;
}

export function buildSimulation(preview: Preview): Simulation {
  let total = 0;
  for (const b of preview.blocks) total += b.commands.length;

  const coords = new Float32Array(total * 4);
  const kind = new Uint8Array(total);
  const thread = new Uint16Array(total);
  const object = new Int32Array(total);
  const objectIds: string[] = [];
  const objectIndex = new Map<string, number>();
  const colorChanges: number[] = [];
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

  let n = 0;
  let cx = NaN, cy = NaN;
  for (const block of preview.blocks) {
    let oi = objectIndex.get(block.objectId);
    if (oi === undefined) {
      oi = objectIds.length;
      objectIds.push(block.objectId);
      objectIndex.set(block.objectId, oi);
    }

    for (let i = 0; i < block.commands.length; i++) {
      const c = block.commands[i];
      const x = block.points[2 * i];
      const y = block.points[2 * i + 1];
      if (c === Cmd.ColorChange) {
        colorChanges.push(n);
        continue;
      }
      if (c !== Cmd.Stitch && c !== Cmd.Travel && c !== Cmd.Jump) continue;

      if (!Number.isNaN(cx) && (x !== cx || y !== cy)) {
        coords[4 * n] = cx;
        coords[4 * n + 1] = cy;
        coords[4 * n + 2] = x;
        coords[4 * n + 3] = y;
        kind[n] = c === Cmd.Jump ? SegmentKind.Jump : c === Cmd.Travel ? SegmentKind.Travel : SegmentKind.Stitch;
        thread[n] = block.threadIndex;
        object[n] = oi;
        n++;
      }
      if (c !== Cmd.Jump) {
        minX = Math.min(minX, x); minY = Math.min(minY, y);
        maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
      }
      cx = x;
      cy = y;
    }
  }

  return {
    count: n,
    coords: coords.subarray(0, n * 4),
    kind: kind.subarray(0, n),
    thread: thread.subarray(0, n),
    object: object.subarray(0, n),
    objectIds,
    colorChanges,
    bounds: Number.isFinite(minX) ? { minX, minY, maxX, maxY } : null,
  };
}

/** Number of sewn (non-jump) segments among the first {@link upTo} segments. */
export function sewnCount(sim: Simulation, upTo: number): number {
  let count = 0;
  for (let i = 0; i < Math.min(upTo, sim.count); i++) if (sim.kind[i] !== SegmentKind.Jump) count++;
  return count;
}
