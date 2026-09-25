import type { EmbroideryObject, EmbroideryThread, Hoop, Preview, ProjectState, StitchType } from "./types";

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

let tokenPromise: Promise<string> | null = null;

/** The host hands out a per-process token that every API call must echo back. */
function token(): Promise<string> {
  tokenPromise ??= fetch("/api/session")
    .then((r) => {
      if (!r.ok) throw new ApiError("Oturum başlatılamadı.", r.status);
      return r.json() as Promise<{ token: string }>;
    })
    .then((j) => j.token)
    .catch((e) => {
      tokenPromise = null;
      throw e;
    });
  return tokenPromise;
}

async function request(path: string, init: RequestInit & { revision?: number } = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("X-Embroidery-Token", await token());
  if (init.revision !== undefined) headers.set("If-Match", String(init.revision));
  if (init.body && typeof init.body === "string") headers.set("Content-Type", "application/json");
  const response = await fetch(`/api/projects${path}`, { ...init, headers });
  if (!response.ok) {
    let message = `İstek başarısız (${response.status}).`;
    try {
      const body = (await response.json()) as { title?: string };
      if (body.title) message = body.title;
    } catch {
      /* not JSON */
    }
    throw new ApiError(message, response.status);
  }
  return response;
}

const json = async <T>(r: Response) => (await r.json()) as T;

export const api = {
  importSvg: (fileName: string, svg: string, targetWidthMm?: number) =>
    request("/import/svg", { method: "POST", body: JSON.stringify({ fileName, svg, targetWidthMm }) }).then(json<ProjectState>),

  open: (data: ArrayBuffer) =>
    request("/open", { method: "POST", body: data, headers: { "Content-Type": "application/octet-stream" } }).then(json<ProjectState>),

  get: (id: string) => request(`/${id}`).then(json<ProjectState>),

  updateObject: (id: string, revision: number, item: EmbroideryObject) =>
    request(`/${id}/objects/${item.id}`, { method: "PUT", revision, body: JSON.stringify(item) }).then(json<ProjectState>),

  convertObject: (id: string, revision: number, objectId: string, type: StitchType) =>
    request(`/${id}/objects/${objectId}/convert`, { method: "POST", revision, body: JSON.stringify({ type }) }).then(json<ProjectState>),

  deleteObject: (id: string, revision: number, objectId: string) =>
    request(`/${id}/objects/${objectId}`, { method: "DELETE", revision }).then(json<ProjectState>),

  reorder: (id: string, revision: number, order: string[]) =>
    request(`/${id}/order`, { method: "PUT", revision, body: JSON.stringify({ order }) }).then(json<ProjectState>),

  updateThreads: (id: string, revision: number, threads: EmbroideryThread[]) =>
    request(`/${id}/threads`, { method: "PUT", revision, body: JSON.stringify(threads) }).then(json<ProjectState>),

  updateSettings: (id: string, revision: number, settings: { name?: string; hoop?: Hoop }) =>
    request(`/${id}/settings`, { method: "PUT", revision, body: JSON.stringify(settings) }).then(json<ProjectState>),

  undo: (id: string) => request(`/${id}/undo`, { method: "POST" }).then(json<ProjectState>),
  redo: (id: string) => request(`/${id}/redo`, { method: "POST" }).then(json<ProjectState>),

  preview: (id: string, signal?: AbortSignal) => request(`/${id}/preview`, { signal }).then(json<Preview>),

  /** Downloads an export through fetch (the token header cannot be sent by a plain link). */
  async download(id: string, format: "dst" | "embx") {
    const response = await request(`/${id}/export/${format}`);
    const disposition = response.headers.get("Content-Disposition") ?? "";
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    const fileName = match ? decodeURIComponent(match[1]) : `design.${format}`;
    const url = URL.createObjectURL(await response.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
};
