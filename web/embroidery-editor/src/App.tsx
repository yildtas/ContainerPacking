import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, ApiError } from "./api";
import { ObjectList } from "./components/ObjectList";
import { PropertiesPanel } from "./components/PropertiesPanel";
import { SimulatorBar } from "./components/SimulatorBar";
import { StitchCanvas } from "./components/StitchCanvas";
import { ThreadPanel } from "./components/ThreadPanel";
import { buildSimulation } from "./simulation";
import type { Design, Diagnostic, Hoop, Preview, ProjectState, StitchProfile } from "./types";


const severityLabel = { info: "Bilgi", warning: "Uyarı", error: "Hata" } as const;

export function App() {
  const [project, setProject] = useState<ProjectState | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [importDiagnostics, setImportDiagnostics] = useState<Diagnostic[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [progress, setProgress] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState(1000);
  const [showJumps, setShowJumps] = useState(true);
  const [fabric, setFabric] = useState(() => {
    try {
      return localStorage.getItem("fabricColor") ?? "#f4f1ea";
    } catch {
      return "#f4f1ea";
    }
  });
  const changeFabric = (color: string) => {
    setFabric(color);
    try {
      localStorage.setItem("fabricColor", color);
    } catch {
      /* per-viewer convenience only */
    }
  };
  const [targetWidth, setTargetWidth] = useState("");
  const [hoops, setHoops] = useState<Hoop[]>([]);
  const [profiles, setProfiles] = useState<StitchProfile[]>([]);
  useEffect(() => {
    api.profiles().then((p) => {
      setHoops(p.hoops);
      setProfiles(p.stitch);
    }).catch(() => undefined);
  }, []);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null);
  const previewAbort = useRef<AbortController | null>(null);
  const svgInput = useRef<HTMLInputElement>(null);
  const embxInput = useRef<HTMLInputElement>(null);

  const design = project?.design ?? null;
  const sim = useMemo(() => (preview ? buildSimulation(preview) : null), [preview]);
  const colors = useMemo(() => design?.threads.map((t) => t.colorHex) ?? [], [design]);
  const selected = design?.objects.find((o) => o.id === selectedId) ?? null;

  const notify = (text: string, error = false) => setMessage({ text, error });

  const loadPreview = useCallback(async (d: Design) => {
    previewAbort.current?.abort();
    const controller = new AbortController();
    previewAbort.current = controller;
    try {
      const p = await api.preview(d.id, controller.signal);
      // A slower answer for an older revision must not overwrite a newer one.
      setPreview((current) => (current && current.revision > p.revision && current.blocks.length ? current : p));
    } catch (e) {
      if ((e as Error).name !== "AbortError") notify((e as Error).message, true);
    }
  }, []);

  const applyState = useCallback(
    (state: ProjectState) => {
      setProject(state);
      void loadPreview(state.design);
    },
    [loadPreview],
  );

  // Show the whole design whenever a new plan arrives (unless the simulator is running).
  useEffect(() => {
    if (sim && !playing) setProgress(sim.count);
  }, [sim]);

  const run = useCallback(
    async (action: (d: Design) => Promise<ProjectState>) => {
      if (!design) return;
      setBusy(true);
      try {
        applyState(await action(design));
      } catch (e) {
        if (e instanceof ApiError && e.status === 409) {
          applyState(await api.get(design.id));
          notify("Tasarım başka bir yerde değişti; güncel hali yüklendi.", true);
        } else {
          notify((e as Error).message, true);
        }
      } finally {
        setBusy(false);
      }
    },
    [design, applyState],
  );

  const undo = useCallback(() => run((d) => api.undo(d.id)), [run]);
  const redo = useCallback(() => run((d) => api.redo(d.id)), [run]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (target.closest("input, select, textarea")) return;
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "z") {
        e.preventDefault();
        if (e.shiftKey) void redo();
        else void undo();
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "y") {
        e.preventDefault();
        void redo();
      } else if (e.key === " " && sim) {
        e.preventDefault();
        setPlaying((p) => !p);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [undo, redo, sim]);

  const openFile = async (file: File, kind: "svg" | "embx") => {
    setBusy(true);
    try {
      const width = Number(targetWidth.replace(",", "."));
      const state =
        kind === "svg"
          ? await api.importSvg(file.name, await file.text(), targetWidth && width > 0 ? width : undefined)
          : await api.open(await file.arrayBuffer());
      setSelectedId(null);
      setPreview(null);
      setImportDiagnostics(state.diagnostics);
      setPlaying(false);
      applyState(state);
      notify(`"${state.design.name}" açıldı: ${state.design.objects.length} nesne.`);
    } catch (e) {
      notify((e as Error).message, true);
    } finally {
      setBusy(false);
    }
  };

  const move = (id: string, delta: -1 | 1) =>
    run((d) => {
      const order = d.objects.map((o) => o.id);
      const i = order.indexOf(id);
      [order[i], order[i + delta]] = [order[i + delta], order[i]];
      return api.reorder(d.id, d.revision, order);
    });

  const diagnostics = [...importDiagnostics, ...(preview?.diagnostics ?? [])];
  const stats = preview?.statistics;

  return (
    <div className="app">
      <header className="toolbar">
        <strong className="brand">Nakış Digitizing</strong>
        <span className="group">
          <button className="primary" onClick={() => svgInput.current?.click()} disabled={busy}>
            SVG içe aktar
          </button>
          <input
            className="width-input"
            placeholder="Genişlik mm"
            title="İsteğe bağlı: SVG bu genişliğe ölçeklenir"
            value={targetWidth}
            onChange={(e) => setTargetWidth(e.target.value)}
          />
          <button onClick={() => embxInput.current?.click()} disabled={busy}>
            Proje aç
          </button>
        </span>
        <span className="group">
          <button onClick={undo} disabled={!project?.canUndo || busy} title="Geri al (Ctrl+Z)">
            Geri al
          </button>
          <button onClick={redo} disabled={!project?.canRedo || busy} title="Yinele (Ctrl+Shift+Z)">
            Yinele
          </button>
        </span>
        <span className="group right">
          <button onClick={() => design && api.download(design.id, "embx").catch((e) => notify(e.message, true))} disabled={!design}>
            Projeyi kaydet (.embx)
          </button>
          {preview?.diagnostics.some((d) => d.code === "Q004") && (
            <button
              onClick={() => design && api.download(design.id, "dst-parts").catch((e) => notify(e.message, true))}
              title="Tasarım kasnağa sığmıyor: her kasnaklama için ayrı DST ve hizalama işaretleri (ZIP)"
            >
              Parçalı DST (zip)
            </button>
          )}
          <button
            className="primary"
            onClick={() => design && api.download(design.id, "dst").catch((e) => notify(e.message, true))}
            disabled={!design}
          >
            DST dışa aktar
          </button>
        </span>
        <input ref={svgInput} type="file" accept=".svg,image/svg+xml" hidden onChange={(e) => {
          const f = e.target.files?.[0];
          e.target.value = "";
          if (f) void openFile(f, "svg");
        }} />
        <input ref={embxInput} type="file" accept=".embx" hidden onChange={(e) => {
          const f = e.target.files?.[0];
          e.target.value = "";
          if (f) void openFile(f, "embx");
        }} />
      </header>

      <aside className="sidebar left">
        {design ? (
          <>
            <section>
              <h2>Tasarım</h2>
              <label className="field">
                <span>Ad</span>
                <input
                  key={design.id + design.name}
                  defaultValue={design.name}
                  onBlur={(e) => e.target.value.trim() && e.target.value !== design.name && run((d) => api.updateSettings(d.id, d.revision, { name: e.target.value }))}
                />
              </label>
              <label className="field">
                <span>Kasnak</span>
                <select
                  value={design.hoop.name}
                  onChange={(e) => {
                    const hoop = hoops.find((h) => h.name === e.target.value);
                    if (hoop) void run((d) => api.updateSettings(d.id, d.revision, { hoop }));
                  }}
                >
                  {!hoops.some((h) => h.name === design.hoop.name) && <option>{design.hoop.name}</option>}
                  {hoops.map((h) => (
                    <option key={h.name} value={h.name}>
                      {h.name.includes(" ") ? h.name : `${h.widthMm} × ${h.heightMm} mm`}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Profil</span>
                <select
                  value={design.stitchProfileId}
                  title={profiles.find((p) => p.id === design.stitchProfileId)?.description}
                  onChange={(e) => run((d) => api.applyProfile(d.id, d.revision, e.target.value))}
                >
                  {profiles.map((p) => (
                    <option key={p.id} value={p.id} title={p.description}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>
              <div className="button-row">
                <button onClick={() => run((d) => api.mirror(d.id, d.revision, "horizontal"))} title="Sol/sağ panel için yatay ayna">
                  Ayna ↔
                </button>
                <button onClick={() => run((d) => api.mirror(d.id, d.revision, "vertical"))} title="Dikey ayna">
                  Ayna ↕
                </button>
              </div>
            </section>
            <section className="grow">
              <h2>
                Dikiş sırası
                <button
                  className="link-button small"
                  title="Üst üste binen nesnelerin sırasını koruyarak renk değişimini ve boş hareketi azaltır"
                  onClick={() =>
                    run(async (d) => {
                      const state = await api.optimizeOrder(d.id, d.revision);
                      const info = state.diagnostics.find((x) => x.code === "SEQ001");
                      if (info) notify(info.message);
                      return state;
                    })
                  }
                >
                  Optimize et
                </button>
              </h2>
              <ObjectList
                design={design}
                selectedId={selectedId}
                onSelect={setSelectedId}
                onMove={move}
                onToggle={(id) => run((d) => {
                  const o = d.objects.find((x) => x.id === id)!;
                  return api.updateObject(d.id, d.revision, { ...o, visible: !o.visible });
                })}
                onDelete={(id) => {
                  if (selectedId === id) setSelectedId(null);
                  void run((d) => api.deleteObject(d.id, d.revision, id));
                }}
              />
            </section>
            <section>
              <h2>İplikler</h2>
              <ThreadPanel threads={design.threads} onChange={(threads) => run((d) => api.updateThreads(d.id, d.revision, threads))} />
            </section>
          </>
        ) : (
          <div className="welcome">
            <h2>Başlayın</h2>
            <p>Bir SVG içe aktarın. Dolgulu şekiller Tatami, kalın çizgiler Satin, ince çizgiler Run olarak açılır; türü ve parametreleri sağ panelden değiştirebilirsiniz.</p>
            <p>Kaydedilmiş bir <code>.embx</code> projesini de açabilirsiniz.</p>
          </div>
        )}
      </aside>

      <main className="stage">
        <StitchCanvas
          sim={sim}
          colors={colors}
          design={design}
          selectedId={selectedId}
          progress={progress}
          showJumps={showJumps}
          background={fabric}
          onSelect={setSelectedId}
        />
        <div className="stage-footer">
          <SimulatorBar
            total={sim?.count ?? 0}
            progress={progress}
            playing={playing}
            speed={speed}
            onProgress={setProgress}
            onPlaying={setPlaying}
            onSpeed={setSpeed}
          />
          <label className="field-check inline">
            <input type="checkbox" checked={showJumps} onChange={(e) => setShowJumps(e.target.checked)} /> Jump'ları göster
          </label>
          <label className="field-check inline" title="Önizleme zemini">
            <input className="fabric-input" type="color" value={fabric} onChange={(e) => changeFabric(e.target.value)} /> Kumaş
          </label>
        </div>
      </main>

      <aside className="sidebar right">
        <section>
          <h2>Özellikler</h2>
          {selected && design ? (
            <PropertiesPanel
              key={selected.id}
              item={selected}
              threads={design.threads}
              onChange={(item) => run((d) => api.updateObject(d.id, d.revision, item))}
              onConvert={(type) => run((d) => api.convertObject(d.id, d.revision, selected.id, type))}
            />
          ) : (
            <p className="empty">Düzenlemek için listeden veya tuvalden bir nesne seçin.</p>
          )}
        </section>
        {stats && (
          <section>
            <h2>İstatistik</h2>
            <dl className="stats">
              <dt>Dikiş</dt><dd>{stats.stitchCount.toLocaleString("tr-TR")}</dd>
              <dt>Renk değişimi</dt><dd>{stats.colorChangeCount}</dd>
              <dt>Trim</dt><dd>{stats.trimCount}</dd>
              <dt>Jump</dt><dd>{stats.jumpCount}</dd>
              <dt>Boyut</dt><dd>{stats.widthMm.toFixed(1)} × {stats.heightMm.toFixed(1)} mm</dd>
              <dt>İplik</dt><dd>{stats.threadLengthM.toFixed(2)} m</dd>
            </dl>
          </section>
        )}
        <section className="grow">
          <h2>Tanılar {diagnostics.length > 0 && <span className="count">{diagnostics.length}</span>}</h2>
          {diagnostics.length === 0 ? (
            <p className="empty">Sorun yok.</p>
          ) : (
            <ul className="diagnostics">
              {diagnostics.map((d, i) => (
                <li
                  key={i}
                  className={`sev-${d.severity}${d.objectId ? " clickable" : ""}`}
                  onClick={() => d.objectId && setSelectedId(d.objectId)}
                >
                  <span className="sev">{severityLabel[d.severity]}</span>
                  <code>{d.code}</code> {d.message}
                </li>
              ))}
            </ul>
          )}
        </section>
      </aside>

      {message && (
        <div className={`toast${message.error ? " error" : ""}`} role="status" onClick={() => setMessage(null)}>
          {message.text}
        </div>
      )}
    </div>
  );
}
