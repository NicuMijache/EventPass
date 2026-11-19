// Datele de demonstrație: conturile (client, organizator, staff) și evenimentele cu postere și tipuri de bilete.
// Totul se creează prin API (ca un utilizator real), nu direct în baza de date.
//
// Utilizare (API-ul pornit, cu SeedAdmin setat; vezi docs/demo.md):
//   node docs/tools/seed-demo.mjs                 (implicit: http://localhost:5003)
//   node docs/tools/seed-demo.mjs http://localhost:5003
//
// Se poate rula de mai multe ori: conturile existente doar se autentifică, iar evenimentele
// care există deja (același titlu) nu se mai creează a doua oară.
// ATENȚIE: parolele de mai jos sunt DOAR pentru demonstrația locală, cu Stripe în mod test.
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const API = (process.argv[2] ?? 'http://localhost:5003').replace(/\/$/, '');
const POSTERS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../demo/postere');
const PAROLA = 'Demo2026!';

export const CONTURI = {
  admin: { email: 'admin@eventpass.demo', fullName: 'Administrator' }, // creat de API la pornire (SeedAdmin)
  organizator: { email: 'organizator@eventpass.demo', fullName: 'Andrei Popescu', role: 'Organizer' },
  staff: { email: 'staff@eventpass.demo', fullName: 'Ioana Marin', role: 'Staff' },
  client: { email: 'client@eventpass.demo', fullName: 'Maria Ionescu', role: 'Client' },
  client2: { email: 'client2@eventpass.demo', fullName: 'Radu Stan', role: 'Client' },
};

// Orele sunt ora României (UTC+2 iarna, UTC+3 vara); serverul le salvează în UTC.
const EVENIMENTE = [
  {
    title: 'Nocturne Jazz Session', venue: 'Form Space', city: 'Cluj-Napoca', startsAt: '2027-02-12T21:00:00+02:00', poster: 'jazz.jpg',
    description: 'O seară de jazz modern, cu un cvartet invitat și o sesiune de improvizație la final. Accesul se face pe baza biletului cu cod QR.',
    tickets: [['Standard', 120, 150], ['VIP (masă rezervată)', 250, 30]],
  },
  {
    title: 'Seara de stand-up', venue: 'Club Subsol', city: 'Iași', startsAt: '2027-01-26T20:30:00+02:00', poster: 'standup.jpg',
    description: 'Trei comedianți, un microfon și o sală mică. Material nou, testat în premieră.',
    tickets: [['Standard', 60, 80], ['Primul rând', 90, 12]],
  },
  {
    title: 'Simfonii de primăvară', venue: 'Filarmonica Moldova', city: 'Iași', startsAt: '2027-04-04T19:00:00+03:00', poster: 'simfonii.jpg',
    description: 'Concert simfonic: Vivaldi, Grieg și Enescu, interpretați de orchestra filarmonicii.',
    tickets: [['Balcon', 50, 120], ['Parter', 80, 200], ['Lojă', 150, 16]],
  },
  {
    title: 'DevFest Studențesc', venue: 'Centrul Multifuncțional', city: 'Timișoara', startsAt: '2027-03-10T09:00:00+02:00', poster: 'devfest.jpg',
    description: 'O zi de prezentări și ateliere despre web, cloud și inteligență artificială, ținute de studenți și ingineri.',
    tickets: [['Student', 30, 250], ['General', 90, 150]],
  },
  {
    title: 'Retro Party 90s', venue: 'Club Control', city: 'București', startsAt: '2027-01-30T22:00:00+02:00', poster: 'retro.jpg',
    description: 'Muzica anilor 90, toată noaptea. Dress code: cât mai colorat.',
    tickets: [['Intrare', 40, 300]],
  },
  {
    title: 'Visul unei nopți de vară', venue: 'Teatrul Odeon', city: 'București', startsAt: '2027-03-20T19:00:00+02:00', poster: 'teatru.jpg',
    description: 'Comedia lui Shakespeare, într-o montare nouă, cu muzică live.',
    tickets: [['Categoria I', 120, 180], ['Categoria a II-a', 80, 120]],
  },
  {
    // Puține locuri: bun pentru a arăta „s-au epuizat” și concurența pe ultimul loc
    title: 'Atelier de fotografie', venue: 'Casa Tranzit', city: 'Cluj-Napoca', startsAt: '2027-02-06T11:00:00+02:00', poster: 'atelier.jpg',
    description: 'Atelier practic de fotografie de stradă, cu un fotograf profesionist. Locuri foarte puține.',
    tickets: [['Participant', 75, 5]],
  },
];

async function call(method, url, { token, json, form } = {}) {
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (json) headers['Content-Type'] = 'application/json';
  const res = await fetch(API + url, { method, headers, body: json ? JSON.stringify(json) : form });
  const text = await res.text();
  const body = text ? JSON.parse(text) : null;
  return { status: res.status, body };
}

async function ensureAccount({ email, fullName }) {
  const login = await call('POST', '/api/auth/login', { json: { email, password: PAROLA } });
  if (login.status === 200) return login.body;
  const reg = await call('POST', '/api/auth/register', { json: { email, password: PAROLA, fullName } });
  if (reg.status !== 200 && reg.status !== 201) throw new Error(`Nu am putut crea contul ${email}: ${reg.status} ${JSON.stringify(reg.body)}`);
  return reg.body;
}

async function main() {
  const health = await fetch(API + '/api/events').catch(() => null);
  if (!health?.ok) throw new Error(`API-ul nu răspunde la ${API}. Pornește-l întâi (vezi docs/demo.md).`);

  const admin = await call('POST', '/api/auth/login', { json: { email: CONTURI.admin.email, password: PAROLA } });
  if (admin.status !== 200)
    throw new Error('Nu mă pot autentifica drept Admin: API-ul trebuie pornit cu SeedAdmin__Email=admin@eventpass.demo și SeedAdmin__Password=Demo2026! (vezi docs/demo.md).');
  console.log('✔ Admin:', CONTURI.admin.email);

  const tokens = {};
  for (const key of ['organizator', 'staff', 'client', 'client2']) {
    const acc = CONTURI[key];
    let auth = await ensureAccount(acc);
    if (auth.user.role !== acc.role) {
      const r = await call('PUT', `/api/users/${auth.user.id}/role`, { token: admin.body.token, json: { role: acc.role } });
      if (r.status !== 200) throw new Error(`Rolul pentru ${acc.email}: ${r.status} ${JSON.stringify(r.body)}`);
      auth = await ensureAccount(acc); // rolul nou apare în token abia la un login nou
    }
    tokens[key] = auth.token;
    console.log(`✔ ${acc.role.padEnd(9)} ${acc.email} (${acc.fullName})`);
  }

  const mine = await call('GET', '/api/organizer/events', { token: tokens.organizator });
  const existing = new Set((mine.body ?? []).map((e) => e.title));
  for (const ev of EVENIMENTE) {
    if (existing.has(ev.title)) { console.log('• există deja:', ev.title); continue; }
    const created = await call('POST', '/api/organizer/events', {
      token: tokens.organizator,
      json: { title: ev.title, description: ev.description, venue: ev.venue, city: ev.city, startsAt: new Date(ev.startsAt).toISOString() },
    });
    if (created.status !== 201 && created.status !== 200) throw new Error(`${ev.title}: ${created.status} ${JSON.stringify(created.body)}`);
    const id = created.body.id;

    for (const [name, price, capacity] of ev.tickets) {
      const t = await call('POST', `/api/organizer/events/${id}/ticket-types`, { token: tokens.organizator, json: { name, price, capacity } });
      if (t.status !== 201 && t.status !== 200) throw new Error(`${ev.title} / ${name}: ${t.status} ${JSON.stringify(t.body)}`);
    }

    const form = new FormData();
    form.append('file', new Blob([await readFile(path.join(POSTERS, ev.poster))], { type: 'image/jpeg' }), ev.poster);
    const img = await call('POST', `/api/organizer/events/${id}/image`, { token: tokens.organizator, form });
    if (img.status !== 200) throw new Error(`Posterul pentru ${ev.title}: ${img.status} ${JSON.stringify(img.body)}`);

    const pub = await call('POST', `/api/organizer/events/${id}/publish`, { token: tokens.organizator });
    if (pub.status !== 200) throw new Error(`Publicarea ${ev.title}: ${pub.status} ${JSON.stringify(pub.body)}`);
    console.log(`✔ eveniment #${id}: ${ev.title} (${ev.tickets.length} ${ev.tickets.length === 1 ? "tip" : "tipuri"} de bilete, poster, publicat)`);
  }
  console.log(`\nGata. Toate conturile au parola ${PAROLA}`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((e) => { console.error('✘', e.message); process.exit(1); });
}
