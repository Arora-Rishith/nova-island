import { request } from "./bridge.js";
import { $, text, perform, formatTime } from "./dom.js";

let selectedMinutes = 25, phase = "idle";
export function initializeActivities() {
  for (const button of document.querySelectorAll("[data-focus]")) button.addEventListener("click", () => {
    selectedMinutes = Number(button.dataset.focus);
    for (const preset of document.querySelectorAll("[data-focus]")) preset.classList.toggle("selected", preset === button);
    if (phase === "idle") text("focus-time", formatTime(selectedMinutes * 60));
  });
  $("focus-toggle").addEventListener("click", () => perform(() =>
    phase === "idle" || phase === "complete" ? request("focus.start", { minutes: selectedMinutes }) : request("focus.toggle"), $("focus-toggle")));
  $("focus-reset").addEventListener("click", () => perform(() => request("focus.reset"), $("focus-reset")));
}

export function renderActivities(state) {
  const focus = state.focus || { phase: "idle", remaining: 0, duration: 0 };
  phase = focus.phase;
  const active = phase === "running" || phase === "paused";
  text("focus-time", formatTime(phase === "idle" ? selectedMinutes * 60 : focus.remaining));
  text("focus-status", { idle: "Focus", running: "Stay with it", paused: "Paused", complete: "Time for a break" }[phase]);
  text("focus-toggle", { idle: "Start focus", running: "Pause", paused: "Resume", complete: "Start again" }[phase]);
  $("focus-reset").hidden = phase === "idle";
  $("focus-presets").hidden = phase !== "idle";
  $("focus-ring").style.strokeDashoffset = String(339.292 * (1 - (phase === "idle" ? 1 : focus.remaining / Math.max(1, focus.duration))));
  const urgent = state.ai?.activities?.find(a => a.status === "waiting");
  const completed = state.ai?.activities?.find(a => a.status === "completed" && Date.now() - Date.parse(a.updated) < 45000);
  const badge = $("compact-activity");
  let label = "", kind = "", target = "";
  if (urgent) { label = `${urgent.app} · Needs you`; kind = "attention"; target = "6"; }
  else if (phase === "complete") { label = "Focus done"; kind = "complete"; target = "7"; }
  else if (active) { label = `${phase === "paused" ? "Ⅱ " : ""}${formatTime(focus.remaining)}`; kind = "focus"; target = "7"; }
  else if (completed) { label = `${completed.app} · Done`; kind = "complete"; target = "6"; }
  badge.hidden = !label;
  badge.dataset.kind = kind;
  badge.dataset.page = target;
  badge.textContent = label;
  badge.title = urgent?.task || (active ? "Focus timer" : completed?.task || label);
  $("island").classList.toggle("has-activity", Boolean(label));
  $("island").classList.toggle("needs-attention", Boolean(urgent));
}
