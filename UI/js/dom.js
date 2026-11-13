export const $ = id => document.getElementById(id);
export function text(id, value) {
  const node = $(id), next = String(value ?? "");
  if (node.textContent !== next) node.textContent = next;
}
export function element(tag, className, content) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (content !== undefined) node.textContent = content;
  return node;
}
export function empty(container, title, subtitle) {
  const card = element("div", "empty");
  card.append(element("strong", "", title), element("span", "", subtitle));
  container.replaceChildren(card);
}
let toastTimer;
export function showMessage(message) {
  text("toast-message", message);
  $("toast").hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => $("toast").hidden = true, 9000);
}
export async function perform(action, control) {
  if (control) control.disabled = true;
  try { return await action(); }
  catch (error) { showMessage(error.message); }
  finally { if (control) control.disabled = false; }
}
export function formatTime(seconds) {
  seconds = Math.max(0, Math.floor(Number.isFinite(seconds) ? seconds : 0));
  const minutes = Math.floor(seconds / 60);
  return minutes >= 60 ? `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`
    : `${minutes}:${String(seconds % 60).padStart(2, "0")}`;
}
