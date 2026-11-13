import { request } from "./bridge.js";
import { $, text, element, empty, perform } from "./dom.js";

let latest;
let panel = "home";
let selectedNetwork = null;
let wifiKey = "", bluetoothKey = "";
const editing = new Set();
const pendingSlider = new Map();

export function pickerIsOpen() { return panel !== "home"; }
export function closePicker() {
  if (panel === "bluetooth") perform(() => request("bluetooth.stop"));
  panel = "home";
  $("wifi-panel").hidden = true;
  $("bluetooth-panel").hidden = true;
  $("quick-home").hidden = false;
  $("wifi-password").value = "";
  selectedNetwork = null;
  $("wifi-join").hidden = true;
}
function openPicker(name) {
  closePicker();
  panel = name;
  $("quick-home").hidden = true;
  $(`${name}-panel`).hidden = false;
  perform(() => request(`${name}.scan`), $(`${name}-refresh`));
}

// Only one hardware write per slider is in flight; keep the latest value during a drag.
async function flushSlider(id) {
  const pending = pendingSlider.get(id);
  if (!pending || pending.running) return;
  pending.running = true;
  while (pending.value !== null) {
    const value = pending.value;
    pending.value = null;
    await perform(() => request(`quickSettings.set${id === "volume" ? "Volume" : "Brightness"}`, { value }));
  }
  pending.running = false;
  editing.delete(id);
}

export function initializeQuickSettings() {
  for (const name of ["wifi", "bluetooth"]) {
    $(`${name}-picker`).addEventListener("click", () => openPicker(name));
    $(`${name}-refresh`).addEventListener("click", () => perform(() => request(`${name}.scan`), $(`${name}-refresh`)));
    $(`${name}-toggle`).addEventListener("click", () => {
      if (!latest) return;
      perform(() => request(name === "wifi" ? "radio.setWifi" : "radio.setBluetooth", { enabled: !latest.quick[name].on }), $(`${name}-toggle`));
    });
  }
  for (const back of document.querySelectorAll(".picker-back")) back.addEventListener("click", closePicker);
  for (const button of document.querySelectorAll("[data-settings]")) button.addEventListener("click", () =>
    perform(() => request("settings.open", { page: button.dataset.settings }), button));
  for (const button of document.querySelectorAll("[data-shortcut]")) button.addEventListener("click", () =>
    perform(() => request("shortcut.open", { id: button.dataset.shortcut }), button));
  $("hotspot").addEventListener("click", () => {
    if (!latest) return;
    perform(() => latest.quick.hotspot.available
      ? request("hotspot.set", { enabled: !latest.quick.hotspot.on })
      : request("settings.open", { page: "hotspot" }), $("hotspot"));
  });
  for (const id of ["volume", "brightness"]) {
    const input = $(id);
    let timer;
    pendingSlider.set(id, { value: null, running: false });
    input.addEventListener("input", () => {
      editing.add(id);
      const value = Math.round(Number(input.value));
      text(`${id}-value`, `${value}%`);
      input.style.setProperty("--fill", `${value}%`);
      pendingSlider.get(id).value = value;
      clearTimeout(timer);
      timer = setTimeout(() => flushSlider(id), id === "brightness" ? 180 : 60);
    });
    input.addEventListener("change", () => { clearTimeout(timer); flushSlider(id); });
    input.addEventListener("pointercancel", () => { clearTimeout(timer); flushSlider(id); });
  }
  $("wifi-connect").addEventListener("click", async () => {
    if (!selectedNetwork) return;
    const password = $("wifi-password").value;
    $("wifi-password").value = "";
    await perform(() => request("wifi.connect", { id: selectedNetwork.id, password }), $("wifi-connect"));
  });
  $("wifi-password").addEventListener("keydown", event => {
    if (event.key === "Enter" && !$("wifi-connect").disabled) $("wifi-connect").click();
  });
}

export function renderQuickSettings(state) {
  latest = state;
  const quick = state.quick;
  for (const name of ["wifi", "bluetooth"]) {
    text(`${name}-status`, quick[name].status);
    $(`${name}-tile`).classList.toggle("active", quick[name].on);
    $(`${name}-toggle`).setAttribute("aria-pressed", String(quick[name].on));
    $(`${name}-toggle`).disabled = !quick[name].available || quick.radioBusy;
    $(`${name}-toggle`).title = quick[name].status;
  }
  text("hotspot-status", quick.hotspot.busy ? "Changing…" : !quick.hotspot.available ? "Open settings" : quick.hotspot.on ? "Sharing internet" : "Off");
  $("hotspot").classList.toggle("active", quick.hotspot.on);
  $("hotspot").disabled = quick.hotspot.busy;
  $("hotspot").title = `${quick.hotspot.capability}: ${quick.hotspot.detail}`;
  for (const id of ["volume", "brightness"]) {
    $(id).disabled = !quick[`${id}Available`];
    if (!editing.has(id)) {
      $(id).value = quick[id];
      $(id).style.setProperty("--fill", `${quick[id]}%`);
      text(`${id}-value`, quick[`${id}Available`] ? `${Math.round(quick[id])}%` : "—");
    }
  }
  text("wifi-detail", state.wifi.status);
  $("wifi-refresh").disabled = state.wifi.busy;
  $("wifi-connect").disabled = state.wifi.busy || !selectedNetwork;
  const newWifiKey = JSON.stringify(state.wifi.networks);
  if (wifiKey !== newWifiKey) { wifiKey = newWifiKey; renderWifi(state.wifi.networks); }
  text("bluetooth-detail", state.bluetooth.status);
  const newBluetoothKey = JSON.stringify(state.bluetooth);
  if (bluetoothKey !== newBluetoothKey) { bluetoothKey = newBluetoothKey; renderBluetooth(state.bluetooth); }
}

function renderWifi(networks) {
  const container = $("wifi-list");
  if (selectedNetwork && !networks.some(n => n.id === selectedNetwork.id)) {
    selectedNetwork = null;
    $("wifi-password").value = "";
    $("wifi-join").hidden = true;
  }
  if (!networks.length) { empty(container, "No networks listed", "Turn on Wi-Fi, then refresh."); return; }
  const scroll = container.scrollTop;
  container.replaceChildren(...networks.map(network => {
    const row = element("button", "list-row");
    row.classList.toggle("selected", network.id === selectedNetwork?.id);
    const copy = element("span", "list-copy");
    copy.append(element("strong", "", network.ssid), element("small", "", `${network.security}${network.saved ? " · Saved" : ""}`));
    row.append(element("span", "list-badge", network.connected ? "✓" : "⌁"), copy, element("small", "", `${network.signal}%`));
    row.addEventListener("click", () => {
      selectedNetwork = network;
      $("wifi-password").value = "";
      $("wifi-password").hidden = !network.secure;
      $("wifi-join").hidden = false;
      $("wifi-connect").disabled = false;
      for (const other of container.children) other.classList.remove("selected");
      row.classList.add("selected");
      if (network.secure) $("wifi-password").focus();
    });
    return row;
  }));
  container.scrollTop = scroll;
}

function renderBluetooth(bluetooth) {
  const container = $("bluetooth-list");
  if (!bluetooth.devices.length) { empty(container, "Looking for devices", "Put your device in pairing mode."); return; }
  const scroll = container.scrollTop;
  container.replaceChildren(...bluetooth.devices.map(device => {
    const row = element("div", "list-row");
    const copy = element("span", "list-copy");
    copy.append(element("strong", "", device.name), element("small", "", device.connected ? "Connected" : device.paired ? "Paired · Not connected" : "Nearby device"));
    row.append(element("span", "list-badge", "ᛒ"), copy);
    if (!device.paired && device.canPair) {
      const button = element("button", "text-button", "Pair");
      button.disabled = bluetooth.busy;
      button.addEventListener("click", () => perform(() => request("bluetooth.pair", { id: device.id }), button));
      row.append(button);
    }
    return row;
  }));
  container.scrollTop = scroll;
}
