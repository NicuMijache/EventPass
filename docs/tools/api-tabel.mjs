// Generează tabelul API-ului pentru anexa raportului final, din documentul OpenAPI al serverului (nu scris de mână).
// Rutele, metodele și ce primește / întoarce fiecare endpoint vin din /openapi/v1.json;
// cine are acces se citește din atributele [Authorize] / [AllowAnonymous] ale controllerelor (OpenAPI nu le conține).
// Tabelul se scrie în raport între marcajele <!-- API:START --> și <!-- API:END -->.
// Utilizare (API-ul pornit): node docs/tools/api-tabel.mjs [http://localhost:5003] [docs/raport-final.md]
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const API = process.argv[2] ?? 'http://localhost:5003';
const REPORT = path.resolve(process.argv[3] ?? path.join(ROOT, 'docs/raport-final.md'));

const doc = await (await fetch(`${API}/openapi/v1.json`)).json();

// Accesul, din codul controllerelor: atributul clasei, suprascris (dacă e cazul) de atributul metodei
async function accessOf(tag, method, route) {
  const src = await readFile(path.join(ROOT, 'EventPass.Server/Controllers', `${tag}Controller.cs`), 'utf8');
  const classPart = src.slice(0, src.indexOf('class '));
  const describe = (text) => {
    if (/\[AllowAnonymous\]/.test(text)) return 'public';
    const roles = text.match(/\[Authorize\(Roles = ([^\]]+)\)\]/);
    if (roles) return [...roles[1].matchAll(/nameof\(UserRole\.(\w+)\)/g)].map((m) => m[1]).join(', ');
    if (/\[Authorize\]/.test(text)) return 'logat';
    return null;
  };
  // metoda: blocul de atribute dinaintea [HttpX("...")] care se potrivește cu ruta
  const verb = method[0] + method.slice(1).toLowerCase();
  const attrs = [...src.matchAll(new RegExp(`((?:\\s*\\[[^\\]]*\\]\\s*)*)\\[Http${verb}(?:\\("([^"]*)"\\))?\\]`, 'g'))];
  const tail = route.replace(/^\/api\//, '');
  const match = attrs.find((m) => {
    const sub = (m[2] ?? '').replace(/:\w+/g, '');
    return sub === '' ? true : tail.endsWith(sub);
  });
  const own = match ? describe(match[1]) : null;
  if (own) return own;
  const cls = describe(classPart);
  if (cls) return cls;
  return tag === 'StripeWebhook' ? 'semnătura Stripe' : 'public';
}

const schemaName = (content) => {
  const schema = content?.['application/json']?.schema ?? content?.['multipart/form-data']?.schema;
  if (!schema) return content?.['image/png'] ? 'imagine PNG' : '—';
  if (schema.$ref) return schema.$ref.split('/').pop();
  if (schema.type === 'array') return `listă de ${schema.items?.$ref?.split('/').pop() ?? schema.items?.type}`;
  return schema.type === 'object' && content['multipart/form-data'] ? 'fișier (form-data)' : schema.type ?? '—';
};

const rows = [];
for (const [route, ops] of Object.entries(doc.paths)) {
  for (const [method, op] of Object.entries(ops)) {
    if (!['get', 'post', 'put', 'delete', 'patch'].includes(method)) continue;
    const tag = op.tags?.[0] ?? '';
    const ok = op.responses?.['200'] ?? op.responses?.['201'] ?? op.responses?.['204'];
    rows.push({
      tag, route, method: method.toUpperCase(),
      access: await accessOf(tag, method.toUpperCase(), route),
      body: schemaName(op.requestBody?.content),
      res: schemaName(ok?.content),
    });
  }
}

const table = [
  `Generat automat din \`/openapi/v1.json\` (${doc.info.title}, ${rows.length} endpoint-uri) cu \`docs/tools/api-tabel.mjs\`; coloana „Acces” vine din atributele \`[Authorize]\` ale controllerelor.`,
  '',
  '| Grup | Metodă | Rută | Acces | Primește | Întoarce |',
  '|---|---|---|---|---|---|',
  ...rows.map((r) => `| ${r.tag} | ${r.method} | \`${r.route}\` | ${r.access} | ${r.body} | ${r.res} |`),
].join('\n');

const md = await readFile(REPORT, 'utf8');
const start = md.indexOf('<!-- API:START -->'), end = md.indexOf('<!-- API:END -->');
if (start < 0 || end < 0) throw new Error('Lipsesc marcajele <!-- API:START --> / <!-- API:END --> din ' + REPORT);
await writeFile(REPORT, md.slice(0, start) + '<!-- API:START -->\n' + table + '\n' + md.slice(end));
console.log(`Tabelul API (${rows.length} endpoint-uri) a fost scris în ${path.relative(ROOT, REPORT)}`);
