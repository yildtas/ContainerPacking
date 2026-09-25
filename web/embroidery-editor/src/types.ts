// Mirrors the JSON contract of Embroidery.Application (camelCase, enums as camelCase strings).

export type Vec2 = [number, number];

export type StitchType = "run" | "satin" | "tatami";
export type FillRule = "nonZero" | "evenOdd";
export type ShortStitchMode = "none" | "innerOnly";
export type Severity = "info" | "warning" | "error";

export interface RunParameters {
  stitchLengthMm: number;
  cornerAngleDeg: number;
  repeats: number;
}

export interface SatinUnderlay {
  centerWalk: boolean;
  edgeWalk: boolean;
  zigZag: boolean;
  edgeInsetMm: number;
  zigZagSpacingMm: number;
  stitchLengthMm: number;
}

export interface SatinParameters {
  spacingMm: number;
  pullCompensationMm: number;
  pushCompensationMm: number;
  maxWidthMm: number;
  shortStitch: ShortStitchMode;
  shortStitchThresholdMm: number;
  shortStitchFraction: number;
  underlay: SatinUnderlay;
}

export interface TatamiUnderlay {
  edgeRun: boolean;
  fill: boolean;
  insetMm: number;
  rowSpacingMm: number;
  stitchLengthMm: number;
}

export interface TatamiParameters {
  angleDeg: number;
  rowSpacingMm: number;
  stitchLengthMm: number;
  staggerFraction: number;
  edgeInsetMm: number;
  pullCompensationMm: number;
  minStitchLengthMm: number;
  underlay: TatamiUnderlay;
}

interface ObjectBase {
  id: string;
  name: string;
  threadIndex: number;
  visible: boolean;
  entryPoint: Vec2 | null;
}

export interface RunObject extends ObjectBase {
  type: "run";
  path: Vec2[];
  parameters: RunParameters;
}

export interface SatinObject extends ObjectBase {
  type: "satin";
  railA: Vec2[];
  railB: Vec2[];
  parameters: SatinParameters;
}

export interface TatamiObject extends ObjectBase {
  type: "tatami";
  region: { rings: Vec2[][]; fillRule: FillRule };
  parameters: TatamiParameters;
}

export type EmbroideryObject = RunObject | SatinObject | TatamiObject;

export interface EmbroideryThread {
  name: string;
  colorHex: string;
  brand?: string | null;
  code?: string | null;
}

export interface Hoop {
  name: string;
  widthMm: number;
  heightMm: number;
}

export interface Design {
  id: string;
  name: string;
  revision: number;
  threads: EmbroideryThread[];
  objects: EmbroideryObject[];
  hoop: Hoop;
  machineProfileId: string;
}

export interface Diagnostic {
  severity: Severity;
  code: string;
  message: string;
  objectId?: string | null;
}

export interface ProjectState {
  design: Design;
  canUndo: boolean;
  canRedo: boolean;
  diagnostics: Diagnostic[];
}

/** Numeric values of Embroidery.Core.StitchPlan.StitchCommand. */
export const Cmd = {
  Stitch: 0,
  Travel: 1,
  Jump: 2,
  Trim: 3,
  ColorChange: 4,
  Stop: 5,
  TieIn: 6,
  TieOff: 7,
  End: 8,
} as const;

export interface PreviewBlock {
  objectId: string;
  kind: "object" | "connector";
  threadIndex: number;
  points: number[];
  commands: number[];
  layers: number[];
}

export interface PreviewStatistics {
  stitchCount: number;
  jumpCount: number;
  trimCount: number;
  colorChangeCount: number;
  widthMm: number;
  heightMm: number;
  threadLengthM: number;
  minX: number;
  minY: number;
}

export interface Preview {
  revision: number;
  threads: { name: string; color: string }[];
  blocks: PreviewBlock[];
  statistics: PreviewStatistics;
  diagnostics: Diagnostic[];
}
