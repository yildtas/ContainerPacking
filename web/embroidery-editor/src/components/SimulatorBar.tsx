import { useEffect, useRef } from "react";

interface Props {
  total: number;
  progress: number;
  playing: boolean;
  speed: number;
  onProgress: (value: number) => void;
  onPlaying: (playing: boolean) => void;
  onSpeed: (speed: number) => void;
}

const speeds = [50, 200, 1000, 5000];

/** Play/pause, scrub and speed for the stitch-by-stitch simulation. */
export function SimulatorBar({ total, progress, playing, speed, onProgress, onPlaying, onSpeed }: Props) {
  const progressRef = useRef(progress);
  progressRef.current = progress;

  useEffect(() => {
    if (!playing) return;
    let frame = 0;
    let last = performance.now();
    let carry = 0;
    const tick = (now: number) => {
      carry += ((now - last) / 1000) * speed;
      last = now;
      const step = Math.floor(carry);
      carry -= step;
      const next = Math.min(total, progressRef.current + step);
      if (step > 0) onProgress(next);
      if (next >= total) {
        onPlaying(false);
        return;
      }
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [playing, speed, total, onProgress, onPlaying]);

  return (
    <div className="simulator">
      <button
        className="primary"
        onClick={() => {
          if (!playing && progress >= total) onProgress(0);
          onPlaying(!playing);
        }}
        disabled={total === 0}
      >
        {playing ? "Duraklat" : "Oynat"}
      </button>
      <button onClick={() => onProgress(Math.max(0, progress - 1))} disabled={progress === 0} title="Bir adım geri">
        ◀
      </button>
      <button onClick={() => onProgress(Math.min(total, progress + 1))} disabled={progress >= total} title="Bir adım ileri">
        ▶
      </button>
      <input
        type="range"
        min={0}
        max={total}
        value={progress}
        onChange={(e) => {
          onPlaying(false);
          onProgress(Number(e.target.value));
        }}
      />
      <span className="counter">
        {progress.toLocaleString("tr-TR")} / {total.toLocaleString("tr-TR")}
      </span>
      <select value={speed} onChange={(e) => onSpeed(Number(e.target.value))} title="Hız (hareket/sn)">
        {speeds.map((s) => (
          <option key={s} value={s}>
            {s}/sn
          </option>
        ))}
      </select>
    </div>
  );
}
