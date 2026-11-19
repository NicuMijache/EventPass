// Un mic „pilot automat” pentru Chrome, prin DevTools Protocol (fără biblioteci externe).
// Folosit de demo-capturi.mjs (capturile de ecran și repetiția demonstrației).
import { spawn } from 'node:child_process';
import { mkdtemp, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export async function launchChrome({ headless = true } = {}) {
  const port = 9300 + Math.floor(Math.random() * 300);
  const profile = await mkdtemp(path.join(tmpdir(), 'ep-demo-'));
  const args = [`--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check',
    '--lang=ro-RO', '--accept-lang=ro-RO,ro', '--disable-features=Translate', '--hide-scrollbars', 'about:blank'];
  // site-ul de test pe HTTPS (npm run start:phone) are un certificat generat local
  if (process.env.EP_SITE?.startsWith('https://localhost')) args.unshift('--ignore-certificate-errors');
  if (headless) args.unshift('--headless=new', '--disable-gpu');
  const proc = spawn(process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe', args, { stdio: 'ignore' });
  let target;
  for (let i = 0; i < 80 && !target; i++) {
    try { target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find((t) => t.type === 'page'); } catch { await sleep(200); }
  }
  const page = await Page.connect(target.webSocketDebuggerUrl);
  page.close = async () => { try { page.ws.close(); } catch {} proc.kill(); };
  return page;
}

export class Page {
  static async connect(url) {
    const p = new Page();
    p.ws = new WebSocket(url);
    p.seq = 0; p.pending = new Map(); p.listeners = [];
    p.ws.addEventListener('message', (m) => {
      const msg = JSON.parse(m.data);
      if (msg.id && p.pending.has(msg.id)) { p.pending.get(msg.id)(msg); p.pending.delete(msg.id); }
      else if (msg.method) p.listeners.forEach((l) => l(msg));
    });
    await new Promise((r) => p.ws.addEventListener('open', r));
    await p.send('Page.enable');
    await p.send('Runtime.enable');
    return p;
  }

  send(method, params = {}) {
    return new Promise((resolve, reject) => {
      const id = ++this.seq;
      this.pending.set(id, (msg) => (msg.error ? reject(new Error(`${method}: ${msg.error.message}`)) : resolve(msg.result)));
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }

  async eval(expr) {
    const r = await this.send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error('eval: ' + (r.exceptionDetails.exception?.description ?? r.exceptionDetails.text));
    return r.result?.value;
  }

  async viewport(width, height, { mobile = false, scale = 1 } = {}) {
    await this.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: scale, mobile });
    await this.send('Emulation.setTouchEmulationEnabled', { enabled: mobile });
    if (mobile) {
      await this.send('Emulation.setUserAgentOverride', {
        userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36',
      });
    }
  }

  async goto(url) {
    await this.send('Page.navigate', { url });
    await this.waitFor(() => this.eval('document.readyState === "complete"'), 30000, 'încărcarea ' + url);
    await sleep(400);
  }

  async waitFor(fn, timeout = 20000, what = 'condiție') {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
      try { const v = await fn(); if (v) return v; } catch { /* pagina se încarcă */ }
      await sleep(250);
    }
    throw new Error('timeout: ' + what);
  }

  waitText(text, timeout = 20000) {
    return this.waitFor(() => this.eval(`document.body && document.body.innerText.toLowerCase().includes(${JSON.stringify(text.toLowerCase())})`), timeout, `textul „${text}”`);
  }

  // Click pe primul element vizibil (buton / link) al cărui text conține `text`
  async clickText(text, selector = 'button, a, [role=button], label') {
    const ok = await this.waitFor(() => this.eval(`(() => {
      const el = [...document.querySelectorAll(${JSON.stringify(selector)})]
        .find(e => e.innerText && e.innerText.replace(/\\s+/g,' ').toLowerCase().includes(${JSON.stringify(text.toLowerCase())}) && e.offsetParent !== null && !e.disabled);
      if (!el) return false; el.scrollIntoView({block:'center'}); el.click(); return true; })()`), 15000, `butonul „${text}”`);
    await sleep(300);
    return ok;
  }

  async click(selector) {
    await this.waitFor(() => this.eval(`(() => { const el = document.querySelector(${JSON.stringify(selector)});
      if (!el || el.disabled) return false; el.scrollIntoView({block:'center'}); el.click(); return true; })()`), 15000, selector);
    await sleep(250);
  }

  // Scrie într-un câmp ca un utilizator (focus + text real), ca formularele Angular să vadă modificarea
  async type(selector, text) {
    await this.waitFor(() => this.eval(`(() => { const el = document.querySelector(${JSON.stringify(selector)});
      if (!el) return false; el.scrollIntoView({block:'center'}); el.focus(); el.select?.(); return true; })()`), 15000, selector);
    await this.send('Input.insertText', { text });
    await sleep(150);
  }

  async mouseClick(x, y) {
    for (const type of ['mouseMoved', 'mousePressed', 'mouseReleased'])
      await this.send('Input.dispatchMouseEvent', { type, x, y, button: 'left', clickCount: 1 });
    await sleep(200);
  }

  async key(key, code = key, keyCode = 0) {
    await this.send('Input.dispatchKeyEvent', { type: 'keyDown', key, code, windowsVirtualKeyCode: keyCode });
    await this.send('Input.dispatchKeyEvent', { type: 'keyUp', key, code, windowsVirtualKeyCode: keyCode });
  }

  async screenshot(file, { fullPage = false } = {}) {
    await this.eval('document.fonts ? document.fonts.ready.then(() => true) : true');
    await sleep(300);
    const params = { format: 'png' };
    if (fullPage) {
      const h = await this.eval('Math.ceil(document.documentElement.scrollHeight)');
      const w = await this.eval('document.documentElement.clientWidth');
      params.clip = { x: 0, y: 0, width: w, height: Math.min(h, 4000), scale: 1 };
      params.captureBeyondViewport = true;
    }
    const { data } = await this.send('Page.captureScreenshot', params);
    await writeFile(file, Buffer.from(data, 'base64'));
    console.log('  📸', path.basename(file));
  }
}
