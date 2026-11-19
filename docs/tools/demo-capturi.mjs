// Repetiția automată a demonstrației + capturile de ecran pentru raportul final și prezentare.
// Parcurge, într-un Chrome real (fără fereastră), exact fluxul din docs/demo.md:
//   client cumpără → plătește pe Stripe (mod test) → email cu QR → retrimitere → staff scanează (valid, apoi „deja utilizat”)
//   → panoul organizatorului → rambursare → biletul rambursat e respins la scanare → vederi de telefon.
//
// Cerințe: mediul pornit ca în docs/demo.md (Docker, API cu baza de demo, stripe listen, site pe :4200)
// și datele de demo create cu seed-demo.mjs. Utilizare (din rădăcina repository-ului):
//   node docs/tools/demo-capturi.mjs [folder-capturi]      (implicit: docs/img/final)
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launchChrome, sleep } from './cdp.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const OUT = path.resolve(process.argv[2] ?? path.join(ROOT, 'docs/img/final'));
const SITE = process.env.EP_SITE ?? 'http://localhost:4200'; // https://localhost:4200 cu npm run start:phone
const API = 'http://localhost:5003';
const SMTP = 'http://localhost:5025';
const PAROLA = 'Demo2026!';
const CARD = { number: '4242424242424242', exp: '1230', cvc: '123' }; // cardul de test publicat de Stripe

const steps = [];
const t0 = Date.now();
function step(name) {
  const s = { name, start: Date.now(), ok: false };
  steps.push(s);
  console.log(`\n▶ ${name}`);
  return { done: (note = '') => { s.ok = true; s.ms = Date.now() - s.start; s.note = note; console.log(`  ✔ ${name} (${(s.ms / 1000).toFixed(1)} s) ${note}`); } };
}

async function api(method, url, token, body) {
  const res = await fetch(API + url, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  return { status: res.status, body: text ? JSON.parse(text) : null };
}

const shot = (page, name, opts) => page.screenshot(path.join(OUT, name), opts);

async function logout(page) {
  await page.eval(`localStorage.removeItem('eventpass.auth'), true`);
}

async function login(page, email) {
  await logout(page);
  await page.goto(`${SITE}/login`);
  await page.type('input[type=email]', email);
  await page.type('input[type=password]', PAROLA);
  await page.click('button[type=submit]');
  await page.waitFor(() => page.eval(`!location.pathname.startsWith('/login')`), 15000, 'login ' + email);
  await sleep(500);
}

// Plata pe pagina Stripe Checkout. Formularul e într-un iframe (alt domeniu), deci nu-l putem citi din pagină:
// apăsăm la coordonate, relativ la colțul iframe-ului, și scriem cu Input.insertText (ca tastatura).
async function payOnStripe(page, screenshotName) {
  await page.waitFor(() => page.eval(`location.host === 'checkout.stripe.com'`), 30000, 'pagina Stripe');
  const frame = await page.waitFor(() => page.eval(`(() => {
      const f = [...document.querySelectorAll('iframe')].map(f => f.getBoundingClientRect()).filter(r => r.height > 300)[0];
      return f ? { x: f.x, y: f.y } : null; })()`), 30000, 'formularul Stripe');
  await sleep(2500);
  const at = (dx, dy) => page.mouseClick(frame.x + dx, frame.y + dy);
  await at(26, 274); // „Card”
  await sleep(2000);
  await at(120, 339); await page.send('Input.insertText', { text: CARD.number }); await sleep(400);
  await at(60, 378); await page.send('Input.insertText', { text: CARD.exp }); await sleep(300);
  await at(240, 378); await page.send('Input.insertText', { text: CARD.cvc }); await sleep(300);
  await at(120, 445); await page.send('Input.insertText', { text: 'Maria Ionescu' }); await sleep(500);
  if (screenshotName) await shot(page, screenshotName);
  await at(194, 714); // „Plătiți”
  await page.waitFor(() => page.eval(`location.host === ${JSON.stringify(new URL(SITE).host)}`), 60000, 'întoarcerea de pe Stripe');
}

// O comandă completă, prin interfață: eveniment → cantități → Rezervă → Plătește → Stripe → „Comanda e plătită”
async function buy(page, eventId, quantities, shots = {}) {
  await page.goto(`${SITE}/evenimente/${eventId}`);
  await page.waitText('Rezervă biletele');
  for (const [index, qty] of quantities.entries())
    for (let i = 0; i < qty; i++)
      await page.eval(`document.querySelectorAll('button[aria-label="Mai multe"]')[${index}].click(), true`);
  await sleep(400);
  if (shots.event) await shot(page, shots.event);
  await page.clickText('Rezervă biletele');
  await page.waitText('Biletele sunt rezervate');
  const orderId = Number(await page.eval(`location.pathname.split('/').pop()`));
  await sleep(1200);
  if (shots.reserved) await shot(page, shots.reserved);
  await page.clickText('Plătește');
  await payOnStripe(page, shots.stripe);
  const confirm = Date.now();
  await page.waitText('Comanda e plătită', 90000);
  const webhookMs = Date.now() - confirm;
  await sleep(1500);
  if (shots.paid) await shot(page, shots.paid);
  return { orderId, webhookMs };
}

async function scan(page, code, expected, name) {
  await page.type('.manual__input', code);
  await page.eval(`document.querySelector('form.manual button[type=submit]').click(), true`);
  await page.waitText(expected, 15000);
  await sleep(700);
  if (name) await shot(page, name);
}

async function main() {
  await mkdir(OUT, { recursive: true });
  const page = await launchChrome();
  const desktop = () => page.viewport(1366, 854, { scale: 1.5 });
  await desktop();

  let s = step('Pagina principală (vizitator)');
  await page.goto(SITE);
  await page.waitText('Nocturne Jazz Session');
  await shot(page, '01-pagina-principala.png');
  s.done();

  s = step('Vânzări de fundal (alt client), ca panoul să aibă date');
  await login(page, 'client2@eventpass.demo');
  const bg1 = await buy(page, 1, [3, 0]);           // 3 × Standard la jazz
  const bg2 = await buy(page, 1, [0, 2]);           // 2 × VIP la jazz (o vom rambursa)
  const bg3 = await buy(page, 7, [5]);              // atelierul: toate cele 5 locuri → „Epuizat”
  s.done(`comenzile #${bg1.orderId}, #${bg2.orderId}, #${bg3.orderId}`);

  s = step('Clientul cumpără 2 bilete și plătește pe Stripe');
  await login(page, 'client@eventpass.demo');
  const main = await buy(page, 1, [2, 0], {
    event: '02-eveniment.png', reserved: '03-comanda-rezervata.png', stripe: '04-plata-stripe.png', paid: '05-comanda-platita.png',
  });
  s.done(`comanda #${main.orderId}; webhook → bilete pe pagină în ${(main.webhookMs / 1000).toFixed(1)} s`);

  s = step('Emailul cu biletele (smtp4dev)');
  const mail = await page.waitFor(async () => {
    const r = await (await fetch(`${SMTP}/api/messages?pageSize=20`)).json();
    return r.results.find((m) => m.to.includes('client@eventpass.demo'));
  }, 60000, 'emailul în smtp4dev');
  // Mesajul exact cum îl vede clientul: HTML-ul emailului, cu imaginile QR din mesaj (cid:), luate din smtp4dev
  await page.viewport(900, 1100, { scale: 1.5 });
  await page.goto(`${SMTP}/`);
  const html = await (await fetch(`${SMTP}/api/messages/${mail.id}/html`)).text();
  await page.eval(`(() => { document.open(); document.write(${JSON.stringify(
    `<base href="${SMTP}/"><div style="font:13px Segoe UI,sans-serif;padding:10px 16px;background:#f1f1ef;border-bottom:1px solid #ddd">` +
    `De la: ${mail.from} · Către: ${mail.to.join(', ')} · Subiect: <b>${mail.subject}</b></div>`)} + ${JSON.stringify(html)}); document.close(); return true; })()`);
  await page.waitFor(() => page.eval(`[...document.images].every(i => i.complete && i.naturalWidth > 0)`), 15000, 'imaginile QR din email');
  await shot(page, '06-email-bilete.png');
  await desktop();
  s.done(`primit ${mail.receivedDate}`);

  s = step('Retrimiterea imediat după plată: limita de 2 minute');
  await page.goto(`${SITE}/comenzi/${main.orderId}`);
  await page.clickText('Retrimite biletele pe email');
  await page.waitFor(() => page.eval(`document.querySelector('.resend')?.innerText.length > 40`), 15000, 'mesajul de retrimitere');
  await sleep(800);
  await page.eval(`document.querySelector('.resend').scrollIntoView({block:'center'}), true`);
  await sleep(500);
  await shot(page, '07b-retrimitere-limita.png');
  s.done(JSON.stringify((await page.eval(`document.querySelector('.resend').innerText`)).replace(/\s+/g, ' ').slice(0, 120)));
  const paidAt = Date.now();

  // Codurile biletelor, citite prin API ca proprietarul comenzii (exact ce e în QR)
  const clientAuth = (await api('POST', '/api/auth/login', null, { email: 'client@eventpass.demo', password: PAROLA })).body;
  const order = (await api('GET', `/api/orders/${main.orderId}`, clientAuth.token)).body;
  const shortCode = order.tickets[0].token.replace(/-/g, '').slice(0, 8).toUpperCase();
  const c2 = (await api('POST', '/api/auth/login', null, { email: 'client2@eventpass.demo', password: PAROLA })).body;
  const refundOrder = (await api('GET', `/api/orders/${bg2.orderId}`, c2.token)).body;

  s = step('Staff: scanare validă, apoi a doua scanare');
  await login(page, 'staff@eventpass.demo');
  await page.goto(`${SITE}/scaneaza`);
  await page.waitText('Scanează bilete');
  await scan(page, order.tickets[0].token, 'Bilet valid', '08-scanner-valid.png');
  await scan(page, shortCode, 'Deja utilizat', '09-scanner-deja-utilizat.png');
  s.done(`cod scurt ${shortCode}`);

  s = step('Panoul organizatorului și rambursarea');
  await login(page, 'organizator@eventpass.demo');
  await page.goto(`${SITE}/organizator`);
  await page.waitText('Nocturne Jazz Session');
  await sleep(800);
  await shot(page, '10-panou-organizator.png');
  await page.goto(`${SITE}/organizator/evenimente/1`);
  await page.waitText('Radu Stan');
  await sleep(800);
  await page.eval(`[...document.querySelectorAll('*')].find(e => e.children.length === 0 && e.innerText?.trim().toLowerCase() === 'participanți')?.scrollIntoView({block:'start'}), true`);
  await sleep(600);
  await shot(page, '11-panou-participanti.png');
  // butonul „Rambursează” al comenzii de 2 × VIP
  await page.waitFor(() => page.eval(`(() => {
      const row = [...document.querySelectorAll('li')].find(li => li.innerText.includes('VIP') && li.innerText.includes('Rambursează'));
      if (!row) return false; const b = [...row.querySelectorAll('button')].find(b => b.innerText.includes('Rambursează'));
      b.scrollIntoView({block:'center'}); b.click(); return true; })()`), 15000, 'butonul Rambursează');
  await sleep(500);
  await shot(page, '12a-rambursare-confirmare.png');
  await page.clickText('Da', 'button');
  await page.waitText('Rambursat', 60000);
  await sleep(800);
  await page.eval(`[...document.querySelectorAll('li')].find(li => li.innerText.includes('Rambursat'))?.scrollIntoView({block:'center'}), true`);
  await sleep(400);
  await shot(page, '12-rambursare.png');
  s.done(`comanda #${bg2.orderId} rambursată`);

  s = step('Biletul rambursat e respins la scanare');
  await login(page, 'staff@eventpass.demo');
  await page.goto(`${SITE}/scaneaza`);
  await page.waitText('Scanează bilete');
  await scan(page, refundOrder.tickets[0].token, 'Respins', '13-scanner-respins.png');
  s.done();

  s = step('Panoul după scanare (intrați: 1)');
  await login(page, 'organizator@eventpass.demo');
  await page.goto(`${SITE}/organizator/evenimente/1`);
  await page.waitText('Radu Stan');
  await sleep(800);
  await shot(page, '14-panou-dupa-scanare.png');
  s.done();

  s = step('Vederi de telefon');
  await page.viewport(390, 844, { mobile: true, scale: 2.5 });
  await logout(page);
  await page.goto(SITE);
  await page.waitText('Nocturne Jazz Session');
  await shot(page, '15-telefon-acasa.png');
  await login(page, 'client@eventpass.demo');
  await page.goto(`${SITE}/comenzi/${main.orderId}`);
  await page.waitText('Biletele tale');
  await page.eval(`document.querySelector('.tickets').scrollIntoView({block:'start'}), true`);
  await sleep(1200);
  await shot(page, '16-telefon-bilet.png');
  await login(page, 'staff@eventpass.demo');
  await page.goto(`${SITE}/scaneaza`);
  await page.waitText('Scanează bilete');
  await scan(page, order.tickets[1].token, 'Bilet valid');
  await page.eval(`document.querySelector('.result').scrollIntoView({block:'center'}), true`);
  await sleep(600);
  await shot(page, '17-telefon-scanner-valid.png');
  await scan(page, order.tickets[1].token, 'Deja utilizat');
  await page.eval(`document.querySelector('.result').scrollIntoView({block:'center'}), true`);
  await sleep(600);
  await shot(page, '18-telefon-scanner-deja-utilizat.png');
  s.done();

  s = step('Retrimiterea biletelor, după 2 minute');
  await desktop();
  await login(page, 'client@eventpass.demo');
  const wait = 125000 - (Date.now() - paidAt);
  if (wait > 0) { console.log(`  (aștept ${Math.ceil(wait / 1000)} s: limita de 2 minute de la ultimul email)`); await sleep(wait); }
  await page.goto(`${SITE}/comenzi/${main.orderId}`);
  const before = (await (await fetch(`${SMTP}/api/messages?pageSize=50`)).json()).results.filter((m) => m.to.includes('client@eventpass.demo')).length;
  await page.clickText('Retrimite biletele pe email');
  await page.waitFor(() => page.eval(`document.querySelector('.resend')?.innerText.length > 40`), 15000, 'mesajul de retrimitere');
  await sleep(800);
  await page.eval(`document.querySelector('.resend').scrollIntoView({block:'center'}), true`);
  await sleep(500);
  await shot(page, '07-retrimitere.png');
  const after = await page.waitFor(async () => {
    const n = (await (await fetch(`${SMTP}/api/messages?pageSize=50`)).json()).results.filter((m) => m.to.includes('client@eventpass.demo')).length;
    return n > before ? n : 0; }, 60000, 'al doilea email');
  s.done(`emailuri primite de client: ${before} → ${after}`);

  s = step('Swagger UI');
  await desktop();
  await page.goto(`${API}/swagger`);
  await page.waitText('EventPass');
  await sleep(1500);
  await shot(page, '19-swagger.png');
  s.done();

  await page.close();
  console.log(`\n══ Repetiția a reușit: ${steps.length} pași, ${((Date.now() - t0) / 1000).toFixed(0)} s în total ══`);
  for (const st of steps) console.log(`  ${st.ok ? '✔' : '✘'} ${st.name} ${st.note ?? ''}`);
  process.exit(0);
}

main().catch((e) => {
  console.error('\n✘ Repetiția s-a oprit:', e.message);
  for (const st of steps) console.error(`  ${st.ok ? '✔' : '✘'} ${st.name}`);
  process.exit(1);
});
