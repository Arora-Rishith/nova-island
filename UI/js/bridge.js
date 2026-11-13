const pending = new Map();
const listeners = new Map();
let sequence = 0;

export const isHosted = Boolean(globalThis.chrome?.webview);

export function request(action, payload = {}) {
  if (!isHosted) return Promise.reject(new Error("Open the island to use Windows controls."));
  const id = String(++sequence);
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pending.delete(id);
      reject(new Error("Windows is taking too long to respond. Please try again."));
    }, 45000);
    pending.set(id, { resolve, reject, timeout });
    chrome.webview.postMessage({ id, action, payload });
  });
}

export function subscribe(type, handler) {
  if (!listeners.has(type)) listeners.set(type, new Set());
  listeners.get(type).add(handler);
  return () => listeners.get(type)?.delete(handler);
}

if (isHosted) chrome.webview.addEventListener("message", ({ data: message }) => {
  if (!message || typeof message !== "object") return;
  if (message.type === "response") {
    const call = pending.get(message.id);
    if (!call) return;
    clearTimeout(call.timeout);
    pending.delete(message.id);
    if (message.ok) call.resolve(message.data);
    else call.reject(new Error(message.error || "The action failed."));
  } else {
    for (const handler of listeners.get(message.type) || []) handler(message.data);
  }
});
