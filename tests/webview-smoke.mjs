// Integration tests against NOVA's real WebView2 and C# bridge. No hardware-changing actions.
// Launch with WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9229 and --devtools.
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";

const targets = await (await fetch("http://127.0.0.1:9229/json")).json();
const target = targets.find(t => t.url.startsWith("https://nova.local/index.html"));
assert.ok(target, "NOVA WebView2 document is running");
const socket = new WebSocket(target.webSocketDebuggerUrl);
const calls = new Map();
const errors = [];
let sequence = 0;
socket.addEventListener("message", ({ data }) => {
  const message = JSON.parse(data);
  if (message.method === "Runtime.exceptionThrown") errors.push(message.params.exceptionDetails.text);
  if (message.id && calls.has(message.id)) {
    const { resolve, reject, timer } = calls.get(message.id);
    clearTimeout(timer);
    calls.delete(message.id);
    message.error ? reject(new Error(message.error.message)) : resolve(message.result);
  }
});
await new Promise((resolve, reject) => { socket.addEventListener("open", resolve); socket.addEventListener("error", reject); });
function cdp(method, params = {}) {
  const id = ++sequence;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { calls.delete(id); reject(new Error(`Timed out: ${method}`)); }, 20000);
    calls.set(id, { resolve, reject, timer });
    socket.send(JSON.stringify({ id, method, params }));
  });
}
async function evaluate(expression) {
  const result = await cdp("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  assert.ok(!result.exceptionDetails, result.exceptionDetails?.exception?.description || "JavaScript evaluation failed");
  return result.result.value;
}
const request = (action, payload = {}) => evaluate(`import('./js/bridge.js').then(b => b.request(${JSON.stringify(action)}, ${JSON.stringify(payload)}))`);

try {
  await cdp("Runtime.enable");
  // Native pointer-exit behavior is checked separately with the Windows UI.
  // Hold only the OS watchdog during synthetic CDP interaction so a real user
  // moving their mouse elsewhere does not invalidate layout tests.
  await request('test.holdPointer', {enabled: true});
  await cdp('Page.reload', { ignoreCache: true });
  await evaluate('new Promise(resolve => setTimeout(resolve, 700))');
  const initial = await request("app.getState");
  assert.ok(initial.media && initial.quick && initial.system && initial.notifications && initial.devices && initial.weather, "All feature snapshots are present");
  assert.ok(initial.clipboard && initial.voice && initial.ai && Array.isArray(initial.shortcuts), "New interaction snapshots are present");
  assert.equal(typeof initial.voice.listening, "boolean");
  assert.ok(initial.shortcuts.length >= 4, "App shortcuts are available");
  assert.equal(typeof initial.ai.running, "boolean");
  assert.equal(typeof initial.weather.temperature, "string");
  assert.equal(typeof initial.quick.wifi.on, "boolean");
  assert.ok(Number.isFinite(initial.system.totalGb) && initial.system.totalGb > 0, "Actual Windows memory metrics are populated");
  for (const [action, payload] of [
    ["window.navigate", { page: 8 }], ["window.navigate", { page: 1.5 }],
    ["radio.setWifi", { enabled: "true" }], ["quickSettings.setVolume", { value: -1 }],
    ["quickSettings.setBrightness", { value: 101 }], ["settings.open", { page: "cmd.exe" }],
    ["arbitrary.shell", {}], ["shortcut.open", { id: "cmd.exe" }]
  ]) {
    const rejected = await evaluate(`import('./js/bridge.js').then(async b => { try { await b.request(${JSON.stringify(action)}, ${JSON.stringify(payload)}); return false; } catch { return true; } })`);
    assert.equal(rejected, true, `Reject invalid request: ${action}`);
  }
  await request("window.expand");
  await cdp("Input.dispatchMouseEvent", { type: "mouseMoved", x: 110, y: 55 });
  await evaluate("new Promise(resolve => setTimeout(resolve, 600))");
  assert.equal(await evaluate("document.getElementById('expanded').inert"), false, "Expansion reaches HTML");
  assert.equal(await evaluate("!document.getElementById('collapse') && !document.getElementById('exit')"), true, "Manual collapse and quit buttons are removed");
  assert.equal(await evaluate("['compact-time', 'compact-temperature', 'compact-weather-icon'].every(id => Boolean(document.getElementById(id)))"), true, "Compact media, time and weather controls are present");
  for (let page = 0; page < 8; page++) {
    await request("window.navigate", { page });
    await evaluate("new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    assert.equal((await request("app.getState")).window.page, page);
    assert.equal(await evaluate(`document.querySelectorAll('.page:not([hidden])').length === 1 && !document.getElementById('page-${page}').hidden`), true, `Page ${page} is exclusively visible`);
  }
  await request("window.navigate", { page: 2 });
  assert.equal(await evaluate("document.querySelectorAll('#calendar-days button').length"), 42);
  const monthBefore = await evaluate("document.getElementById('month-label').textContent");
  await evaluate("document.getElementById('month-next').click()");
  assert.notEqual(await evaluate("document.getElementById('month-label').textContent"), monthBefore, "Month navigation works");
  await evaluate("document.getElementById('today').click()");
  assert.equal(await evaluate("document.querySelectorAll('[aria-current=date]').length"), 1);

  await request("window.navigate", { page: 6 });
  assert.equal(await evaluate("document.querySelectorAll('.page:not([hidden])').length === 1 && !document.getElementById('page-6').hidden"), true, "AI agent page is exclusively visible");
  assert.equal(await evaluate("Boolean(document.getElementById('ai-open') && document.getElementById('voice-agent'))"), true, "AI and voice controls are present");

  // Inject untrusted display data through the normal renderer; it must remain text, not executable markup.
  assert.equal(await evaluate(`(async () => {
    const { renderPanels } = await import('./js/panels.js');
    const { request } = await import('./js/bridge.js');
    const state = await request('app.getState');
    const fixture = structuredClone(state);
    fixture.notifications.items = [{id:'test',app:'Test',title:'<img src=x onerror="window.injected=true">',body:'<script>window.injected=true</script>',time:new Date().toISOString()}];
    renderPanels(fixture);
    const safe = !window.injected && !document.querySelector('#notification-list img, #notification-list script') && document.getElementById('notification-list').textContent.includes('<img');
    renderPanels(state);
    return safe;
  })()`), true, "Notification content is rendered safely");
  await request("window.navigate", { page: 3 });
  await request("window.expand");
  await cdp("Input.dispatchMouseEvent", { type: "mouseMoved", x: 110, y: 55 });
  await evaluate("new Promise(resolve => setTimeout(resolve, 600))");
  assert.equal(await evaluate("document.getElementById('expanded').inert"), false, "Layout is measured while expanded");
  assert.equal(await evaluate("['voice-toggle', 'voice-centre', 'clipboard-preview', 'lyrics-sheet'].every(id => Boolean(document.getElementById(id))) && Boolean(document.querySelector('.music-card')) && !document.querySelector('.quick-media')"), true, "New music and interaction controls are present");
  const overflow = await evaluate(`Array.from(document.querySelectorAll('#quick-home button, #quick-home input')).filter(e => {
    const r=e.getBoundingClientRect(), parent=document.querySelector('.pages').getBoundingClientRect();
    return r.top < parent.top - 1 || r.bottom > parent.bottom + 1 || r.right > parent.right + 1;
  }).map(e=>e.id || e.textContent)`);
  assert.deepEqual(overflow, [], "Quick settings controls fit inside the page");

  await request("window.navigate", { page: 1 });
  await request("system.refresh");
  await mkdir("artifacts/screenshots", { recursive: true });
  // Let navigation settle before capturing or measuring the fixed page layers.
  await evaluate("new Promise(resolve => setTimeout(resolve, 350))");
  const screenshot = await cdp("Page.captureScreenshot", { format: "png" });
  await writeFile("artifacts/screenshots/system.png", Buffer.from(screenshot.data, "base64"));
  for (const [page, name] of [[0, 'music'], [2, 'calendar'], [3, 'control-centre'], [4, 'notifications'], [5, 'devices'], [6, 'agent'], [7, 'focus']]) {
    await request('window.navigate', { page });
    await evaluate('new Promise(resolve => setTimeout(resolve, 350))');
    const layout = await evaluate(`(() => {
      const page = document.getElementById('page-${page}');
      const box = page.getBoundingClientRect();
      const viewport = document.querySelector('.pages').getBoundingClientRect();
      return { horizontalOverflow: page.scrollWidth > page.clientWidth + 1 || box.right > viewport.right + 1,
        controls: [...page.querySelectorAll('button, input')].filter(node => {
          if (!node.getClientRects().length || node.closest('.scroll-list, .agent-page')) return false;
          const r = node.getBoundingClientRect();
          return r.left < box.left - 1 || r.right > box.right + 1 || r.bottom > box.bottom + 1;
        }).map(node => node.id || node.getAttribute('aria-label')) };
    })()`);
    assert.equal(layout.horizontalOverflow, false, `${name} has no horizontal overflow`);
    assert.deepEqual(layout.controls, [], `${name} controls fit`);
    const capture = await cdp('Page.captureScreenshot', { format: 'png' });
    await writeFile(`artifacts/screenshots/${name}.png`, Buffer.from(capture.data, 'base64'));
  }
  await request('window.navigate', { page: 0 });
  assert.equal(await evaluate("getComputedStyle(document.getElementById('navigation')).flexDirection"), 'column', 'Page navigation is vertical');
  await request('focus.start', { minutes: 5 });
  assert.equal((await request('app.getState')).focus.phase, 'running');
  await request('focus.toggle');
  assert.equal((await request('app.getState')).focus.phase, 'paused');
  await request('focus.reset');
  assert.equal((await request('app.getState')).focus.phase, 'idle');
  // Exercise long lyrics and opposing album accents without requiring playback.
  assert.equal(await evaluate(`(async () => {
    const { renderMedia } = await import('./js/media.js');
    const { request } = await import('./js/bridge.js');
    const state = await request('app.getState');
    const fixture = {...state.media, available: true, playing: true, title: 'Palette check',
      accent: {r: 30, g: 200, b: 170}, lyrics: Array.from({length: 60}, (_, i) => 'Lyric line ' + i)};
    renderMedia(fixture);
    const toggle = document.getElementById('lyrics-toggle');
    toggle.click();
    const sheet = document.getElementById('lyrics-sheet');
    sheet.scrollTop = 120;
    const previousScroll = sheet.scrollTop;
    renderMedia(fixture);
    const stable = !sheet.hidden && sheet.scrollTop === previousScroll && previousScroll > 0;
    document.getElementById('island').getAnimations({subtree: true}).forEach(animation => animation.finish());
    const accent = getComputedStyle(document.getElementById('island'), '::before').boxShadow;
    toggle.click();
    renderMedia(state.media);
    return stable && accent.includes('30, 200, 170');
  })()`), true, 'Album halo follows the palette and lyric updates preserve scrolling');

  // Sample frames while reversing real bridge-driven motion. Report timing as
  // diagnostic evidence rather than asserting a hardware-dependent frame rate.
  const timing = await evaluate(`(async () => {
    const { request } = await import('./js/bridge.js');
    const frames = []; let previous = performance.now(), running = true;
    function sample(now) { frames.push(now - previous); previous = now; if (running) requestAnimationFrame(sample); }
    requestAnimationFrame(sample);
    const widths = [];
    for (let i = 0; i < 8; i++) {
      await request(i % 2 ? 'window.expand' : 'window.collapse');
      await new Promise(resolve => setTimeout(resolve, 90));
      widths.push(innerWidth);
    }
    await new Promise(resolve => setTimeout(resolve, 600));
    running = false;
    frames.sort((a,b) => a-b);
    return { hostWidths: [...new Set(widths)], frames: frames.length,
      p95Ms: frames[Math.floor(frames.length * .95)], maxMs: frames.at(-1),
      width: document.getElementById('island').getBoundingClientRect().width };
  })()`);
  assert.equal(timing.hostWidths.length, 1, 'Native browser width stays fixed throughout motion');
  assert.ok(Math.abs(timing.width - 400) < 1, 'Interrupted animation settles at expanded width');
  console.log('Animation frame timing:', JSON.stringify(timing));
  await request("window.collapse");
  await request("window.expand");
  await request("window.collapse");
  await evaluate("new Promise(resolve => setTimeout(resolve, 750))");
  assert.equal((await request("app.getState")).window.expanded, false, "Rapid expansion/collapse resolves to the latest request");
  assert.equal(await evaluate("document.getElementById('compact').inert"), false);
  assert.equal(await evaluate("document.getElementById('expanded').inert"), true, 'Collapsed controls cannot receive keyboard focus');
  const collapsed = await cdp('Page.captureScreenshot', { format: 'png' });
  await writeFile('artifacts/screenshots/compact.png', Buffer.from(collapsed.data, 'base64'));
  await cdp('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] });
  assert.equal(await evaluate("getComputedStyle(document.getElementById('island')).transitionDuration"), '0s', 'Reduced motion disables morphing');
  await cdp('Emulation.setEmulatedMedia', { features: [] });
  // Programmatic collapse can leave the test pointer inside the compact pill;
  // move outside first so this measures a real entry rather than an in-place move.
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 12, y: 10 });
  await evaluate('new Promise(resolve => setTimeout(resolve, 200))');
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 232, y: 55 });
  await evaluate('new Promise(resolve => setTimeout(resolve, 700))');
  assert.equal((await request('app.getState')).window.expanded, true, 'Pointer entry expands the island');
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 12, y: 10 });
  await evaluate('new Promise(resolve => setTimeout(resolve, 800))');
  assert.equal((await request('app.getState')).window.expanded, false, 'Pointer exit collapses without native-hover interference');
  assert.deepEqual(errors, [], "No uncaught frontend errors");
   console.log("PASS: native bridge, eight pages, vertical navigation, focus timer, lyrics, calendar, safe rendering, layout, real metrics and window animation.");
  console.log("Screenshot: artifacts/screenshots/system.png");
  if (process.argv.includes('--exit')) await request('app.exit');
} finally {
  if (!process.argv.includes('--exit')) await request('test.holdPointer', {enabled: false});
  socket.close();
  for (const call of calls.values()) clearTimeout(call.timer);
}
