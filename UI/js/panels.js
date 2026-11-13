import { request } from "./bridge.js";
import { $, text, element, empty, perform } from "./dom.js";

let notificationKey = "", deviceKey = "";
export function initializePanels() {
  $("notifications-enable").addEventListener("click", () => perform(() => request("notifications.enable"), $("notifications-enable")));
  $("notifications-clear").addEventListener("click", () => perform(() => request("notifications.clear"), $("notifications-clear")));
  $("devices-refresh").addEventListener("click", () => perform(() => request("devices.refresh"), $("devices-refresh")));
}
export function renderPanels(state) {
  for (const name of ["cpu", "ram", "gpu", "disk"]) {
    if (!$(`${name}`) || !$(`${name}-meter`)) continue;
    const value = state.system[name];
    text(name, Number.isFinite(value) ? `${Math.round(value)}%` : "—");
    $(`${name}-meter`).style.width = `${Math.max(0, Math.min(100, value || 0))}%`;
  }
  const formatRate = value => Number.isFinite(value) ? `${value < 10 ? value.toFixed(1) : Math.round(value)} Mbps` : "—";
  if ($("net-up")) text("net-up", formatRate(state.system.netUpMbps));
  if ($("net-down")) text("net-down", formatRate(state.system.netDownMbps));
  if ($("net-up-meter")) $("net-up-meter").style.width = `${Math.min(100, (state.system.netUpMbps || 0) * 8)}%`;
  if ($("net-down-meter")) $("net-down-meter").style.width = `${Math.min(100, (state.system.netDownMbps || 0) * 8)}%`;
  text("notification-status", state.notifications.status);
  $("notifications-enable").hidden = state.notifications.access;
  $("notifications-clear").disabled = !state.notifications.items.length;
  const nextKey = JSON.stringify(state.notifications.items);
  if (notificationKey !== nextKey) {
    notificationKey = nextKey;
    const list = $("notification-list");
    if (!state.notifications.items.length) empty(list, "All caught up", "Your notifications will appear here.");
    else {
      const scroll = list.scrollTop;
      list.replaceChildren(...state.notifications.items.map(item => {
        const card = element("article", "notification");
        const header = element("div", "notification-heading");
        const dismiss = element("button", "", "×");
        dismiss.setAttribute("aria-label", "Dismiss " + item.title);
        dismiss.addEventListener("click", () => perform(() => request("notifications.dismiss", { id: item.id }), dismiss));
        header.append(element("span", "", item.app), element("time", "", new Date(item.time).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" })), dismiss);
        card.append(header, element("strong", "", item.title), element("p", "", item.body));
        return card;
      }));
      list.scrollTop = scroll;
    }
  }
  text("device-status", state.devices.status);
  $("devices-refresh").disabled = state.devices.busy;
  const nextDeviceKey = JSON.stringify(state.devices.items);
  if (deviceKey !== nextDeviceKey) {
    deviceKey = nextDeviceKey;
    const list = $("device-list");
    if (!state.devices.items.length) empty(list, "Your connected world", "Refresh to discover Bluetooth, audio and USB devices.");
    else {
      const scroll = list.scrollTop;
      list.replaceChildren(...state.devices.items.map(device => {
        const row = element("div", "list-row");
        const copy = element("span", "list-copy");
        copy.append(element("strong", "", device.name), element("small", "", `${device.kind} · ${device.status}`));
        row.append(element("span", "list-badge", device.kind === "Bluetooth" ? "ᛒ" : device.kind === "Audio" ? "♫" : "⌘"), copy);
        return row;
      }));
      list.scrollTop = scroll;
    }
  }
}
