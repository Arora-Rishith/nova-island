import { writeFile, mkdir } from 'node:fs/promises';
const targets = await (await fetch('http://127.0.0.1:9229/json')).json();
const target = targets.find(t => t.url.startsWith('https://nova.local/index.html'));
if (!target) throw new Error('No NOVA test instance');
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.addEventListener('open', resolve); socket.addEventListener('error', reject); });
let sequence = 0;
const calls = new Map();
socket.addEventListener('message', ({data}) => { const message = JSON.parse(data); if (calls.has(message.id)) { calls.get(message.id)(message); calls.delete(message.id); } });
const cdp = (method, params = {}) => new Promise(resolve => { const id = ++sequence; calls.set(id, resolve); socket.send(JSON.stringify({id, method, params})); });
try {
  if (process.argv.includes('--exit')) {
    await cdp('Runtime.evaluate', { expression: "import('./js/bridge.js').then(b=>b.request('app.exit'))", awaitPromise: true });
  } else {
    const response = await cdp('Runtime.evaluate', { expression: `import('./js/bridge.js').then(async b => {
      const s = await b.request('app.getState');
      return {window: s.window, focus: s.focus, ai: s.ai.activities?.map(a => ({app:a.app,status:a.status,model:a.model,permission:a.permission,taskLength:a.task.length})), media: {lyrics:s.media.lyrics.length,status:s.media.lyricsStatus}, layout: {width:innerWidth,height:innerHeight}};
    })`, awaitPromise: true, returnByValue: true });
    console.log(JSON.stringify(response.result, null, 2));
    await mkdir('artifacts/screenshots', {recursive:true});
    const shot = await cdp('Page.captureScreenshot', {format:'png'});
    await writeFile('artifacts/screenshots/current.png', Buffer.from(shot.result.data, 'base64'));
  }
} finally { socket.close(); }
