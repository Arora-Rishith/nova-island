import { request, subscribe, isHosted } from "./bridge.js";
import { $, text, element, perform, showMessage } from "./dom.js";
import { initializeMedia, renderMedia } from "./media.js";
import { initializeQuickSettings, renderQuickSettings, closePicker, pickerIsOpen } from "./quick-settings.js";
import { initializeCalendar, renderClock } from "./calendar.js";
import { initializePanels, renderPanels } from "./panels.js";
import { initializeAgent, renderAgent } from "./agent.js";
import { initializeActivities, renderActivities } from "./activities.js";

const titles = ["Music", "Performance", "Calendar", "CONTROL CENTRE", "Notifications", "Devices", "AI activity", "Focus"];
const labels = ["Music", "Performance", "Calendar", "Quick settings", "Notifications", "Devices", "AI activity", "Focus"];
let expanded = false, expansionInitialized = false, page = 0, hoverTimer, wheelTime = 0, voiceState = { listening: false }, clipboardRevision = 0, clipboardTimer;

function showPage(index) {
  const direction = index >= page ? "next" : "previous";
  if (index !== page) closePicker();
  page = index;
  $("island").dataset.page = String(index);
  for (let i = 0; i < titles.length; i++) {
    const pageElement = $(`page-${i}`);
    pageElement.hidden = i !== index;
    pageElement.classList.remove("page-enter-next", "page-enter-previous");
    if (i === index) {
      void pageElement.offsetWidth;
      pageElement.classList.add(`page-enter-${direction}`);
      pageElement.addEventListener("animationend", () => pageElement.classList.remove(`page-enter-${direction}`), { once: true });
    }
    $("navigation").children[i].setAttribute("aria-current", i === index ? "page" : "false");
  }
  text("page-title", titles[index]);
  text("page-number", `${index + 1} / ${titles.length}`);
}
async function navigate(index) {
  if (index < 0 || index >= titles.length) return;
  if (!isHosted) { showPage(index); return; }
  await perform(() => request("window.navigate", { page: index }));
}
function setExpanded(value) {
  if (expansionInitialized && expanded === value) return;
  expansionInitialized = true;
  if (expanded && !value) { closePicker(); $("toast").hidden = true; }
  expanded = value;
  $("island").classList.toggle("is-expanded", value);
  // Keep both fixed-size layers mounted so they can crossfade without reflow.
  $("compact").hidden = false;
  $("expanded").hidden = false;
  $("compact").inert = value;
  $("expanded").inert = !value;
  $("compact").setAttribute("aria-expanded", String(value));
  if (!value && $("expanded").contains(document.activeElement)) $("compact").focus({ preventScroll: true });
}
for (let i = 0; i < titles.length; i++) {
  const button = element("button");
  button.title = labels[i];
  button.setAttribute("aria-label", labels[i]);
  button.addEventListener("click", () => navigate(i));
  $("navigation").append(button);
}
initializeMedia();
initializeQuickSettings();
initializeCalendar();
initializePanels();
initializeAgent();
initializeActivities();
showPage(0);

subscribe("state", state => {
  setExpanded(state.window.expanded);
  if (page !== state.window.page) showPage(state.window.page);
  renderMedia(state.media);
  renderQuickSettings(state);
  renderPanels(state);
  renderAgent(state.ai);
  renderClock(state.time);
  renderWeather(state.weather);
  renderVoice(state.voice);
  renderClipboard(state.clipboard);
  renderActivities(state);
});
const expand = () => isHosted ? perform(() => request("window.expand")) : setExpanded(true);
const collapse = () => isHosted ? perform(() => request("window.collapse")) : setExpanded(false);
$("compact").addEventListener("click", () => {
  expand();
  const target = $("compact-activity").dataset.page;
  if (target) navigate(Number(target));
});
$("toast-dismiss").addEventListener("click", () => $("toast").hidden = true);
for (const button of [$("voice-toggle"), $("voice-centre"), $("voice-agent")]) button.addEventListener("click", () => {
  const action = voiceState.listening ? "voice.stop" : "voice.start";
  perform(() => request(action), button);
});
$("clipboard-dismiss").addEventListener("click", () => {
  $("clipboard-preview").hidden = true;
  perform(() => request("clipboard.dismiss"));
});
const island = $("island");
island.addEventListener("pointerenter", () => {
  clearTimeout(hoverTimer);
  if (!expanded && isHosted) hoverTimer = setTimeout(expand, 70);
});
island.addEventListener("pointerleave", event => {
  clearTimeout(hoverTimer);
  if (expanded && (!event.relatedTarget || !island.contains(event.relatedTarget))) collapse();
});
document.addEventListener("wheel", event => {
  if (!expanded || pickerIsOpen() || event.target.closest("input")) return;
  const scrollable = event.target.closest(".scroll-list, .lyrics-sheet, .agent-page");
  if (scrollable && (event.deltaY > 0 ? scrollable.scrollTop + scrollable.clientHeight < scrollable.scrollHeight - 2 : scrollable.scrollTop > 2)) return;
  event.preventDefault();
  if (Date.now() - wheelTime < 280 || Math.abs(event.deltaY) < 4) return;
  wheelTime = Date.now();
  navigate(page + (event.deltaY > 0 ? 1 : -1));
}, { passive: false });
document.addEventListener("keydown", event => {
  if (event.key === "Escape" && pickerIsOpen()) { event.preventDefault(); closePicker(); return; }
  if (event.key === "Escape" && expanded) { event.preventDefault(); collapse(); return; }
  if (event.target.closest("input, textarea") || !expanded) return;
  if (["ArrowDown", "ArrowUp", "ArrowRight", "ArrowLeft"].includes(event.key)) {
    event.preventDefault();
    navigate(page + (["ArrowDown", "ArrowRight"].includes(event.key) ? 1 : -1));
  }
});
if (isHosted) perform(() => request("app.ready"));
else {
  setExpanded(true);
  text("connection-label", "BROWSER PREVIEW");
  renderClock(new Date().toISOString());
  renderWeather();
  setInterval(() => renderClock(new Date().toISOString()), 1000);
}

function renderWeather(weather = {}) {
  text("compact-temperature", weather.temperature || "--°");
  const icon = ["sun", "cloud", "rain", "snow", "storm"].includes(weather.icon) ? weather.icon : "cloud";
  $("compact-weather-icon").querySelector("use").setAttribute("href", `#${icon === "sun" ? "sun-weather" : icon}-icon`);
  $("compact-weather-icon").title = weather.condition || "Weather unavailable";
}

function renderVoice(voice = {}) {
  voiceState = voice;
  const active = Boolean(voice.listening);
  for (const button of [$("voice-toggle"), $("voice-centre")]) {
    button.classList.toggle("active", active);
    button.title = voice.status || "Voice commands";
  }
  const agentButton = $("voice-agent");
  if (agentButton) {
    text("voice-agent-status", active ? "Listening for your command" : "Voice commands are off");
    agentButton.classList.toggle("active", active);
    agentButton.title = voice.status || "Voice commands";
    agentButton.querySelector("span:last-child").textContent = active ? "Listening" : "Enable voice";
  }
  $("compact-voice-dot").classList.toggle("active", active);
  $("voice-centre").querySelector("span:last-child").textContent = active ? "Listening" : "Voice";
}

function renderClipboard(clipboard = {}) {
  if (!clipboard.available || !clipboard.preview) {
    $("clipboard-preview").hidden = true;
    return;
  }
  if (clipboard.revision === clipboardRevision) return;
  clipboardRevision = clipboard.revision;
  text("clipboard-text", clipboard.preview);
  text("clipboard-meta", clipboard.kind === "text" ? `${clipboard.length} characters` : clipboard.kind === "files" ? `${clipboard.length} items` : "Ready to paste");
  $("clipboard-preview").hidden = false;
  clearTimeout(clipboardTimer);
  clipboardTimer = setTimeout(() => $("clipboard-preview").hidden = true, 9000);
}
