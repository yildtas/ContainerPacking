import { describe, expect, it } from "vitest";
import { addRung, dragVertex, falloff, handles, removeRung, translateObject } from "./editing";
import type { RunObject, SatinObject, TatamiObject, Vec2 } from "./types";

const base = { id: "x", name: "x", threadIndex: 0, visible: true, entryPoint: null };
const line = (n: number): Vec2[] => Array.from({ length: n + 1 }, (_, i) => [i, 0] as Vec2);
const run = (path: Vec2[]): RunObject =>
  ({ ...base, type: "run", path, parameters: { stitchLengthMm: 2.5, cornerAngleDeg: 30, repeats: 1 } }) as RunObject;
const rails = (): SatinObject =>
  ({
    ...base, type: "satin", source: "rails", railA: line(10), railB: line(10).map(([x]) => [x, 4] as Vec2),
    rungs: [{ a: [5, 0], b: [5, 4] }], centerline: [], widthMm: 4, startTaperMm: 0, endTaperMm: 0,
  }) as unknown as SatinObject;

describe("editing", () => {
  it("translates every geometry field and the entry point", () => {
    const moved = translateObject({ ...rails(), entryPoint: [1, 1] }, 2, 3) as SatinObject;
    expect(moved.railA[0]).toEqual([2, 3]);
    expect(moved.rungs[0].b).toEqual([7, 7]);
    expect(moved.entryPoint).toEqual([3, 4]);
    const fill = { ...base, type: "tatami", region: { rings: [[[0, 0], [1, 0], [1, 1]]], fillRule: "evenOdd" } } as unknown as TatamiObject;
    expect((translateObject(fill, 1, 1) as TatamiObject).region.rings[0][2]).toEqual([2, 2]);
  });

  it("thins handles but keeps the ends", () => {
    const h = handles(run(line(10)), 3);
    expect(h.map((x) => x.index)).toEqual([0, 3, 6, 9, 10]);
  });

  it("drags proportionally with a smooth falloff", () => {
    expect(falloff(0, 2)).toBe(1);
    expect(falloff(2, 2)).toBe(0);
    const moved = dragVertex(run(line(10)), { kind: "path" }, 5, 0, 2, 2) as RunObject;
    expect(moved.path[5]).toEqual([5, 2]);
    expect(moved.path[4][1]).toBeGreaterThan(0);
    expect(moved.path[4][1]).toBeLessThan(2);
    expect(moved.path[4][1]).toBeCloseTo(moved.path[6][1]);
    expect(moved.path[2]).toEqual([2, 0]);
    // Radius 0 moves only the grabbed vertex.
    const single = dragVertex(run(line(10)), { kind: "path" }, 5, 0, 2, 0) as RunObject;
    expect(single.path.filter((p) => p[1] !== 0)).toHaveLength(1);
  });

  it("moves rung ends with their rail", () => {
    const moved = dragVertex(rails(), { kind: "railA" }, 5, 0, -1, 0) as SatinObject;
    expect(moved.rungs[0].a).toEqual([5, -1]);
    expect(moved.rungs[0].b).toEqual([5, 4]);
  });

  it("adds a rung across the rails and removes the nearest one", () => {
    const withRung = addRung(rails(), [2.3, 1.5]);
    expect(withRung.rungs).toHaveLength(2);
    expect(withRung.rungs[1].a[1]).toBe(0);
    expect(withRung.rungs[1].b[1]).toBe(4);
    expect(removeRung(withRung, [2.4, 2], 1).rungs).toEqual(rails().rungs);
    expect(removeRung(withRung, [8, 2], 1).rungs).toHaveLength(2);
  });
});
