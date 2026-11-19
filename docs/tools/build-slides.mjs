// Prezentarea: docs/prezentare/slides.html → imagini PNG (câte una pe slide) → prezentare.pdf și prezentare.pptx (cu notele vorbitorului).
// Utilizare (din rădăcina repository-ului): node docs/tools/build-slides.mjs
// Are nevoie de Google Chrome, de internet (fontul Manrope, de la Google Fonts) și de Python cu python-pptx (pip install python-pptx).
// Verifică și dacă vreun text iese din slide (se oprește cu eroare).
import { mkdir, writeFile, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { launchChrome, sleep } from './cdp.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const DIR = path.join(ROOT, 'docs/prezentare');
const PNG = path.join(tmpdir(), 'eventpass-slides'); // imaginile slide-urilor, temporare

await rm(PNG, { recursive: true, force: true });
await mkdir(PNG, { recursive: true });
const page = await launchChrome();
await page.viewport(1920, 1080, { scale: 1 });
await page.goto(pathToFileURL(path.join(DIR, 'slides.html')).href);
await page.eval(`document.fonts.ready.then(() => [...document.fonts].filter(f => f.status === 'loaded').map(f => f.family))`)
  .then((f) => console.log('fonturi încărcate:', [...new Set(f)].join(', ') || 'NICIUNUL'));
await page.eval(`Promise.all([...document.images].map(i => i.complete ? 0 : new Promise(r => { i.onload = i.onerror = r; })))`);

// Verificarea: elemente care ies din slide sau text tăiat
const problems = await page.eval(`(() => {
  const out = [];
  document.querySelectorAll('section.slide').forEach((s, i) => {
    const r = s.getBoundingClientRect();
    s.querySelectorAll('*:not(.notes):not(.notes *)').forEach(el => {
      const b = el.getBoundingClientRect();
      if (b.width === 0 || b.height === 0) return;
      if (b.right > r.right - 40 || b.bottom > r.bottom - 30 || b.left < r.left + 40)
        out.push('slide ' + (i + 1) + ': <' + el.tagName.toLowerCase() + ' class="' + el.className.baseVal + el.className + '"> ' + (el.textContent || '').trim().slice(0, 40));
      if (el.scrollWidth > el.clientWidth + 2 && getComputedStyle(el).overflow !== 'visible')
        out.push('slide ' + (i + 1) + ': text tăiat în <' + el.tagName.toLowerCase() + '>');
    });
  });
  return out;
})()`);
if (problems.length) { console.error('✘ Conținut în afara slide-ului:\n  ' + problems.join('\n  ')); await page.close(); process.exit(1); }

const slides = await page.eval(`[...document.querySelectorAll('section.slide')].map(s => {
  const r = s.getBoundingClientRect();
  return { y: r.top + scrollY, notes: s.querySelector('.notes')?.textContent.trim().replace(/\\s+/g, ' ') ?? '', time: s.dataset.time ?? '' };
})`);
for (const [i, s] of slides.entries()) {
  const { data } = await page.send('Page.captureScreenshot', {
    format: 'png', captureBeyondViewport: true, clip: { x: 0, y: s.y, width: 1920, height: 1080, scale: 1 },
  });
  await writeFile(path.join(PNG, `slide-${String(i + 1).padStart(2, '0')}.png`), Buffer.from(data, 'base64'));
}
console.log(`✔ ${slides.length} slide-uri desenate ca imagini`);

// PDF: același HTML, tipărit cu o pagină de 1920 × 1080 pe slide (textul rămâne text, fonturile sunt incluse)
await page.eval(`(() => { const st = document.createElement('style');
  st.textContent = '@page { size: 1920px 1080px; margin: 0 } html, body { background: #fff } .slide { margin: 0 !important }';
  document.head.appendChild(st); return true; })()`);
await sleep(300);
const pdf = await page.send('Page.printToPDF', { printBackground: true, preferCSSPageSize: true, marginTop: 0, marginBottom: 0, marginLeft: 0, marginRight: 0 });
await writeFile(path.join(DIR, 'prezentare.pdf'), Buffer.from(pdf.data, 'base64'));
console.log('✔ prezentare.pdf');
await page.close();

// PPTX: fiecare slide e imaginea lui (arată identic pe orice calculator, chiar fără fontul Manrope), plus notele vorbitorului
await writeFile(path.join(PNG, 'notes.json'), JSON.stringify(slides.map((s) => ({ notes: s.notes, time: s.time }))));
execFileSync('python', [path.join(ROOT, 'docs/tools/build-pptx.py'), PNG, path.join(DIR, 'prezentare.pptx')], { stdio: 'inherit' });
const total = slides.reduce((t, s) => { const [m, sec] = s.time.split(':').map(Number); return t + (m * 60 + sec || 0); }, 0);
console.log(`Durata planificată (din note): ${Math.floor(total / 60)} min ${total % 60} s, cu demonstrația`);
process.exit(0);
