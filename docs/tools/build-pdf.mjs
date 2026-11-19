// Markdown (docs/raport-*.md) → HTML (cu diagramele Mermaid desenate) → PDF A4, prin Chrome (DevTools Protocol).
// Utilizare (din folderul docs/tools, după npm install): node build-pdf.mjs ../raport-2.md ../raport-2.pdf
// Are nevoie de Google Chrome (altă cale: variabila CHROME_PATH) și de internet (biblioteca Mermaid, de pe CDN).
// Opțiuni, scrise ca un comentariu HTML în Markdown:
//   <!-- pdf:capitole-pe-pagina-noua -->  fiecare titlu „##” (capitol) începe pe o pagină nouă
//   <!-- pdf:cuprins-cu-pagini -->        blocul dintre <!-- CUPRINS:START --> și <!-- CUPRINS:END --> se înlocuiește,
//                                         în PDF, cu un cuprins generat (capitole + subcapitole) cu numerele paginilor.
//                                         Numerele se află din PDF-ul unei prime treceri, cu pdftotext (vine cu Git for Windows).
import { readFile, writeFile, unlink as unlinkFile } from 'node:fs/promises';
import { spawn, execFileSync } from 'node:child_process';
import { mkdtemp } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { marked } from 'marked';

const [mdPath, pdfPath] = process.argv.slice(2);
const md = await readFile(mdPath, 'utf8');
const title = (md.match(/^#\s+(.+)$/m)?.[1] ?? md.match(/<h1[^>]*>(.*?)<\/h1>/)?.[1] ?? 'EventPass').replace(/[*_`]/g, '');
const chaptersOnNewPage = md.includes('<!-- pdf:capitole-pe-pagina-noua -->');
const tocWithPages = md.includes('<!-- pdf:cuprins-cu-pagini -->');
const escape = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
// Aceleași id-uri ca GitHub pentru titluri, ca legăturile din cuprins să meargă și în PDF
const slug = (text) => text.toLowerCase().replace(/<[^>]+>/g, '').replace(/[^\p{L}\p{N}\s-]/gu, '').trim().replace(/\s/g, '-');

marked.use({
  gfm: true,
  renderer: {
    code({ text, lang }) {
      return lang === 'mermaid' ? `<pre class="mermaid">${escape(text)}</pre>` : `<pre><code>${escape(text)}</code></pre>`;
    },
    heading({ tokens, depth }) {
      const inner = this.parser.parseInline(tokens);
      return `<h${depth} id="${slug(inner)}">${inner}</h${depth}>\n`;
    },
    image({ href, text }) {
      return `<figure><img src="${href}" alt="${escape(text)}"><figcaption>${escape(text)}</figcaption></figure>`;
    },
  },
});

// Descrierea unei capturi (paragraful de dinaintea imaginii) intră în aceeași „figură”,
// ca să rămână pe aceeași pagină cu imaginea (altfel textul poate rămâne singur jos, pe pagina de dinainte)
const body = marked.parse(md).replace(/<p>((?:(?!<\/p>)[\s\S])*?)<\/p>\s*<p>\s*<figure>/g, '<figure><p class="desc">$1</p>')
  .replace(/<p>\s*<figure>/g, '<figure>').replace(/<\/figure>\s*<\/p>/g, '</figure>');
const html = `<!doctype html>
<html lang="ro"><head><meta charset="utf-8">
<base href="${pathToFileURL(path.dirname(path.resolve(mdPath))).href}/">
<title>${escape(title)}</title>
<style>
  @page { size: A4; margin: 18mm 16mm 20mm; }
  body { font: 10.5pt/1.55 "Segoe UI", system-ui, sans-serif; color: #111; max-width: 100%; }
  h1 { font-size: 22pt; line-height: 1.2; margin: 0 0 12pt; letter-spacing: -0.02em; }
  h2 { font-size: 15pt; margin: 22pt 0 8pt; padding-bottom: 4pt; border-bottom: 1.5px solid #111; break-after: avoid; }
  h2#cuprins { border: 0; }
  h3 { font-size: 12pt; margin: 14pt 0 6pt; break-after: avoid; }
  p, li { orphans: 3; widows: 3; }
  hr { border: 0; border-top: 1px solid #ccc; margin: 14pt 0; }
  a { color: #111; }
  code { font: 9pt Consolas, monospace; background: #f3f3f1; padding: 1px 4px; border-radius: 3px; }
  pre { background: #f6f6f4; padding: 8pt; border-radius: 4px; overflow: hidden; white-space: pre-wrap; }
  pre code { background: none; padding: 0; }
  blockquote { margin: 10pt 0; padding: 6pt 12pt; border-left: 3px solid #111; background: #f6f6f4; }
  table { border-collapse: collapse; width: 100%; margin: 8pt 0 12pt; font-size: 9.5pt; break-inside: auto; }
  tr { break-inside: avoid; }
  th, td { border: 1px solid #d5d5d2; padding: 4pt 6pt; text-align: left; vertical-align: top; }
  th { background: #f0f0ee; }
  pre.mermaid { background: none; padding: 0; text-align: center; break-inside: avoid; margin: 10pt 0; }
  pre.mermaid svg { max-width: 100%; height: auto; }
  figure { margin: 8pt 0 16pt; break-inside: avoid; text-align: center; }
  figure img { max-width: 100%; max-height: 98mm; border: 1px solid #d5d5d2; border-radius: 4px; }
  figcaption { font-size: 8.5pt; color: #666; margin-top: 4pt; }
  /* Cuprinsul și primul titlu pe prima pagină; restul începe pe pagina următoare */
  /* Un id care începe cu cifră nu merge ca selector #id: folosim [id="..."] */
  h2[id="1-scopul-etapei"], h2[id="11-capturi-de-ecran"] { break-before: page; }
  figure .desc { text-align: left; margin: 0 0 6pt; font-size: 10.5pt; }
  ${chaptersOnNewPage ? 'h2 { break-before: page; } h2:first-of-type { break-before: auto; }' : ''}
  /* Coperta și cuprinsul generat (raportul final) */
  .coperta { height: 247mm; display: flex; flex-direction: column; justify-content: space-between; break-after: page; }
  .coperta { padding-top: 30mm; }
  .coperta__sus { font-size: 9pt; letter-spacing: .12em; color: #666; margin: 0 0 40mm; }
  .coperta__marca { font-size: 11pt; font-weight: 700; letter-spacing: .35em; margin: 0 0 6mm; }
  .coperta__titlu { font-size: 34pt; letter-spacing: -0.03em; margin: 0 0 6mm; }
  .coperta__sub { font-size: 13pt; color: #444; margin: 0; max-width: 130mm; }
  .coperta__date { font-size: 10pt; border-top: 1.5px solid #111; }
  .coperta__date td { border: 0; border-bottom: 1px solid #e2e2df; padding: 5pt 4pt; }
  .coperta__date td:first-child { color: #666; width: 34mm; }
  pre.mermaid svg { max-height: 215mm; width: auto; }
  nav.toc { break-after: page; }
  nav.toc h2 { break-before: auto; border: 0; }
  nav.toc ol { list-style: none; padding: 0; margin: 0; }
  nav.toc li { display: flex; align-items: baseline; gap: 6pt; margin: 2.5pt 0; }
  nav.toc li.h3 { padding-left: 16pt; font-size: 9.5pt; color: #333; }
  nav.toc li.h2 { font-weight: 600; margin-top: 6pt; }
  nav.toc .dots { flex: 1; border-bottom: 1px dotted #999; transform: translateY(-3pt); }
  nav.toc a { text-decoration: none; }
</style>
</head><body>
${body}
<script type="module">
  import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.esm.min.mjs';
  mermaid.initialize({ startOnLoad: false, theme: 'neutral', securityLevel: 'strict', fontFamily: 'Segoe UI, sans-serif',
    themeVariables: { fontSize: '15px' },
    sequence: { mirrorActors: false, messageFontSize: 17, actorFontSize: 17, noteFontSize: 16, width: 105, actorMargin: 12, boxMargin: 6, messageMargin: 26, wrap: false },
    flowchart: { htmlLabels: true, nodeSpacing: 40, rankSpacing: 45 } });
  try { await mermaid.run({ querySelector: 'pre.mermaid' }); window.__mermaidDone = 'ok'; }
  catch (e) { window.__mermaidDone = 'eroare: ' + e.message; }
</script>
</body></html>`;

const htmlPath = path.join(path.dirname(path.resolve(mdPath)), '.raport.tmp.html');
await writeFile(htmlPath, html);

// Chrome fără interfață, controlat prin DevTools Protocol
const port = 9700 + Math.floor(Math.random() * 200);
const profile = await mkdtemp(path.join(tmpdir(), 'ep-pdf-'));
const chrome = spawn(process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  ['--headless=new', '--disable-gpu', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--allow-file-access-from-files', 'about:blank'], { stdio: 'ignore' });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let target;
for (let i = 0; i < 60 && !target; i++) { try { target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find((t) => t.type === 'page'); } catch { await sleep(200); } }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener('open', r));
let seq = 0; const pending = new Map();
ws.addEventListener('message', (m) => { const msg = JSON.parse(m.data); if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); } });
const send = (method, params = {}) => new Promise((resolve) => { const id = ++seq; pending.set(id, resolve); ws.send(JSON.stringify({ id, method, params })); });
const evaluate = async (expr) => (await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true })).result?.result?.value;

await send('Page.enable');
await send('Page.navigate', { url: pathToFileURL(htmlPath).href });
let done;
for (let i = 0; i < 120 && !done; i++) { await sleep(250); done = await evaluate('window.__mermaidDone'); }
console.log('diagrame Mermaid:', done ?? 'timeout', '| desenate:', await evaluate(`document.querySelectorAll('pre.mermaid svg').length`), 'din', await evaluate(`document.querySelectorAll('pre.mermaid').length`));
await evaluate(`Promise.all([...document.images].map(i => i.complete ? 0 : new Promise(r => { i.onload = i.onerror = r; })))`);
console.log('imagini încărcate:', await evaluate(`[...document.images].filter(i => i.naturalWidth > 0).length`), 'din', await evaluate(`document.images.length`));

const footer = `<div style="width:100%;font:8px 'Segoe UI',sans-serif;color:#888;padding:0 16mm;display:flex;justify-content:space-between">
  <span>${escape(title)}</span><span><span class="pageNumber"></span> / <span class="totalPages"></span></span></div>`;
const print = async () => Buffer.from((await send('Page.printToPDF', {
  paperWidth: 8.27, paperHeight: 11.69, printBackground: true, preferCSSPageSize: true,
  displayHeaderFooter: true, headerTemplate: '<div></div>', footerTemplate: footer,
})).result.data, 'base64');

if (tocWithPages) {
  // 1. Cuprinsul generat din titluri, cu „?” în loc de pagini (ocupă deja tot spațiul lui, deci paginile nu se mai mută)
  const entries = await evaluate(`(() => {
    const start = [...document.body.childNodes].find(n => n.nodeType === 8 && n.data.trim() === 'CUPRINS:START');
    const end = [...document.body.childNodes].find(n => n.nodeType === 8 && n.data.trim() === 'CUPRINS:END');
    if (!start || !end) return null;
    while (start.nextSibling && start.nextSibling !== end) start.nextSibling.remove();
    const heads = [...document.querySelectorAll('h2, h3')].filter(h => !h.closest('.coperta') && h.id !== 'cuprins' && !h.closest('nav'));
    const nav = document.createElement('nav'); nav.className = 'toc';
    nav.innerHTML = '<h2 id="cuprins">Cuprins</h2><ol>' + heads.map((h, i) =>
      '<li class="' + h.tagName.toLowerCase() + '"><a href="#' + h.id + '">' + h.textContent + '</a><span class="dots"></span><span class="pg" data-i="' + i + '">00</span><span style="font-size:1px;color:#fff">CUPRINSMARK</span></li>').join('') + '</ol>';
    start.after(nav);
    return heads.map(h => h.textContent.replace(/\\s+/g, ' ').trim());
  })()`);
  if (!entries) throw new Error('Lipsesc marcajele <!-- CUPRINS:START --> / <!-- CUPRINS:END -->');
  // 2. Prima trecere: PDF temporar → textul fiecărei pagini → pe ce pagină apare fiecare titlu
  const tmpPdf = pdfPath + '.tmp.pdf';
  await writeFile(tmpPdf, await print());
  const pages = execFileSync('pdftotext', ['-enc', 'UTF-8', tmpPdf, '-'], { encoding: 'utf8' }).split('\f') // \f = sfârșit de pagină
    .map((p) => p.replace(/\s+/g, ' '));
  // căutăm după cuprins (paginile lui au un marcaj invizibil)
  let from = pages.findLastIndex((p) => p.includes('CUPRINSMARK')) + 1;
  const found = entries.map((text) => {
    const needle = text.replace(/\s+/g, ' ');
    for (let p = from; p < pages.length; p++) if (pages[p].includes(needle)) { from = p; return p + 1; }
    return '?';
  });
  console.log('cuprins:', found.filter((f) => f !== '?').length, 'din', entries.length, 'titluri găsite în PDF');
  await evaluate(`(() => { const f = ${JSON.stringify(found)}; document.querySelectorAll('nav.toc .pg').forEach((s, i) => s.textContent = f[i]); return true; })()`);
  await unlinkFile(tmpPdf);
}
await writeFile(pdfPath, await print());
console.log('PDF scris:', pdfPath);
ws.close(); chrome.kill();
const { unlink } = await import('node:fs/promises');
await unlink(htmlPath);
process.exit(0);
