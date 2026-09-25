import { describe, expect, it } from "vitest";
import { buildSimulation, SegmentKind, sewnCount } from "./simulation";
import { Cmd, type Preview } from "./types";

const preview: Preview = {
  revision: 1,
  threads: [{ name: "a", color: "#ff0000" }, { name: "b", color: "#0000ff" }],
  statistics: { stitchCount: 0, jumpCount: 0, trimCount: 0, colorChangeCount: 0, widthMm: 0, heightMm: 0, threadLengthM: 0, minX: 0, minY: 0 },
  diagnostics: [],
  blocks: [
    { objectId: "o1", kind: "connector", threadIndex: 0, points: [0, 0, 0, 0], commands: [Cmd.Jump, Cmd.TieIn], layers: [2, 2] },
    { objectId: "o1", kind: "object", threadIndex: 0, points: [0, 0, 10, 0, 10, 5], commands: [Cmd.Stitch, Cmd.Stitch, Cmd.Travel], layers: [0, 0, 0] },
    { objectId: "o2", kind: "connector", threadIndex: 1, points: [10, 5, 10, 5, 30, 5], commands: [Cmd.Trim, Cmd.ColorChange, Cmd.Jump], layers: [2, 2, 2] },
    { objectId: "o2", kind: "object", threadIndex: 1, points: [40, 5], commands: [Cmd.Stitch], layers: [0] },
  ],
};

describe("buildSimulation", () => {
  const sim = buildSimulation(preview);

  it("turns moves into segments and skips zero-length moves", () => {
    // (0,0)->(10,0) stitch, (10,0)->(10,5) travel, (10,5)->(30,5) jump, (30,5)->(40,5) stitch
    expect(sim.count).toBe(4);
    expect(Array.from(sim.kind)).toEqual([SegmentKind.Stitch, SegmentKind.Travel, SegmentKind.Jump, SegmentKind.Stitch]);
    expect(Array.from(sim.coords.subarray(0, 4))).toEqual([0, 0, 10, 0]);
  });

  it("tracks threads, objects and colour changes", () => {
    expect(Array.from(sim.thread)).toEqual([0, 0, 1, 1]);
    expect(sim.objectIds).toEqual(["o1", "o2"]);
    expect(sim.colorChanges).toEqual([2]);
  });

  it("computes bounds from sewn points only", () => {
    expect(sim.bounds).toEqual({ minX: 0, minY: 0, maxX: 40, maxY: 5 });
  });

  it("counts sewn segments", () => {
    expect(sewnCount(sim, 4)).toBe(3);
    expect(sewnCount(sim, 2)).toBe(2);
  });
});
