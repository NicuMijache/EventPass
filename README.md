# EventPass

[![CI](https://github.com/NicuMijache/EventPass/actions/workflows/ci.yml/badge.svg)](https://github.com/NicuMijache/EventPass/actions/workflows/ci.yml)

Platformă web de vânzare a biletelor la evenimente cu plată online (Stripe) și cod QR trimis pe email. La intrare, biletul se scanează și se validează pe server.

## Cum funcționează
1. Organizatorul creează un eveniment (dată, locație, tipuri de bilete, preț, număr de locuri).
2. Clientul alege biletele și plătește prin Stripe Checkout (mod test).
3. Stripe trimite un webhook; serverul emite biletele, fiecare cu token unic (GUID), și generează codul QR.
4. Clientul primește biletul cu QR pe email.
5. Staff-ul scanează QR-ul; serverul verifică biletul (existent, plătit, nefolosit) și îl marchează ca utilizat.

## Tehnologii
| Zonă | Alegere |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, EF Core |
| Frontend | Angular |
| Bază de date | PostgreSQL |
| Plăți | Stripe (Checkout + Webhooks, mod test), `Stripe.net` |
| Cod QR | `QRCoder` (generare), `jsQR` (scanare în browser; citește toate codurile noastre, spre deosebire de ZXing) |
| Email | `MailKit` + SMTP (în dezvoltare: `smtp4dev`, server SMTP de test) |
| API | Swagger / OpenAPI |
| Teste | xUnit |
| CI | GitHub Actions |
| Containere | Docker Compose (PostgreSQL + SMTP de test) |
| Management | GitHub Projects, Issues, Milestones |

## Cerințe
- Visual Studio Community (workload ASP.NET) și .NET 10 SDK
- Node.js LTS și Angular CLI
- Git, `dotnet-ef`
- Cont Stripe în mod test + Stripe CLI (pentru webhook-uri locale)
- **Varianta cu Docker (recomandată):** Docker Desktop (pe Windows, cu WSL2 activ)
- **Varianta fără Docker:** PostgreSQL 18 instalat local și `smtp4dev` (unealtă locală a proiectului, se instalează cu `dotnet tool restore`)

## Rulare

Baza de date (PostgreSQL) și serverul de email de test (smtp4dev) pot rula în containere Docker (varianta 1) sau direct pe calculator (varianta 2). Aplicația (API-ul .NET și clientul Angular) rulează oricum pe calculator.

### Varianta 1: cu Docker (recomandată)
1. `git clone` al repository-ului, apoi în folderul clientului: `npm install`
2. Creează fișierul `.env` din șablon și pune o parolă în el (`.env` nu intră în git):
   `copy .env.example .env` (Windows) sau `cp .env.example .env`
3. Pornește totul cu o singură comandă: `docker compose up -d`
   Verifică cu `docker compose ps`: serviciul `db` trebuie să fie `healthy`. Pe calculator, containerele sunt la:
   - PostgreSQL: `localhost:5433` (portul 5432 rămâne liber pentru un PostgreSQL local)
   - SMTP de test: `localhost:2525`, iar emailurile se văd la http://localhost:5025
4. Setează secretele (User Secrets): șirul de conexiune `Host=localhost;Port=5433;Database=eventpass;Username=eventpass;Password=<parola din .env>`, `Jwt:Key`, `SeedAdmin:Email` și `SeedAdmin:Password`, `Stripe:SecretKey`, `Stripe:WebhookSecret`
   (alternativ, doar pentru fereastra curentă de PowerShell: `$env:ConnectionStrings__DefaultConnection = "..."`; o variabilă de mediu are prioritate peste User Secrets)
5. Creează schema în baza nouă: `dotnet ef database update --project EventPass.Server`
6. Pornește webhook-urile locale (secretul `whsec_...` pentru `Stripe:WebhookSecret` îl afișează `stripe listen --print-secret`):
   `stripe listen --events checkout.session.completed,checkout.session.async_payment_succeeded --forward-to http://localhost:5003/api/webhooks/stripe`
7. Pornește API-ul (`dotnet run --project EventPass.Server --launch-profile http`) și clientul (`cd eventpass.client`, `npm start`), apoi deschide http://localhost:4200

Oprire: `docker compose down` oprește containerele și **păstrează** datele (volumul `eventpass_eventpass-db-data`). `docker compose down -v` șterge și volumul, deci **pierzi toate datele din baza de test**.

### Varianta 2: fără Docker
1. `git clone` al repository-ului, apoi în folderul clientului: `npm install`
2. Instalează PostgreSQL 18 local (portul 5432) și creează baza `eventpass`
3. Setează secretele (User Secrets): șirul de conexiune la baza de date, `Jwt:Key`, `SeedAdmin:Email` și `SeedAdmin:Password`, `Stripe:SecretKey`, `Stripe:WebhookSecret` (pentru un server SMTP real: și `Smtp:Username`, `Smtp:Password`)
4. `dotnet ef database update --project EventPass.Server`
5. Pornește serverul SMTP de test (emailurile se văd în browser, la http://localhost:5025):
   `dotnet tool restore` apoi `dotnet tool run smtp4dev -- --smtpport=2525 --urls=http://localhost:5025 --db="" --nousersettings`
6. Pornește webhook-urile locale: `stripe listen --events checkout.session.completed,checkout.session.async_payment_succeeded --forward-to http://localhost:5003/api/webhooks/stripe`
   (secretul `whsec_...` pentru `Stripe:WebhookSecret` îl afișează `stripe listen --print-secret`)
7. Pornește API-ul (`dotnet run --project EventPass.Server --launch-profile http`) și clientul (`cd eventpass.client`, `npm start`), apoi deschide http://localhost:4200

Carduri de test Stripe: `4242 4242 4242 4242` (succes), orice dată viitoare și orice CVC.

### Probleme frecvente
- **Port ocupat** (`5433`, `2525` sau `5025`): află ce îl folosește cu `netstat -ano | findstr :5433`. Dacă ai `smtp4dev` pornit local, oprește-l, altfel intră în conflict cu containerul. Poți schimba și portul din `docker-compose.yml`.
- **Docker Desktop oprit** (`failed to connect to the docker API ... dockerDesktopLinuxEngine`): pornește Docker Desktop și așteaptă să apară „Engine running”.
- **WSL2**: Docker Desktop are nevoie de el. Verifică cu `wsl --status`; dacă lipsește, `wsl --install` și repornește calculatorul. Virtualizarea trebuie să fie activă în BIOS.
- **Am schimbat parola în `.env`, dar baza o refuză**: Postgres citește parola doar la prima pornire. Ori pui parola veche înapoi, ori ștergi datele cu `docker compose down -v` și pornești din nou.
- **Plata trece, dar comanda rămâne „Pending”** (webhook-ul primește 400): `Stripe:WebhookSecret` din secrete diferă de cel afișat de `stripe listen --print-secret`.
- **`stripe listen` cere `--events`**: pe versiunile noi ale Stripe CLI trebuie dată lista de evenimente, ca în comenzile de mai sus.

## Stocul de bilete și rezervările
- La „Rezervă”, locurile se scad din stoc printr-un singur `UPDATE ... WHERE Sold + n <= Capacity`, atomic: oricâți clienți apasă simultan, nu se vând mai multe locuri decât există (plus o regulă `CHECK` în baza de date).
- O comandă neplătită ține locurile 15 minute; după ce clientul apasă „Plătește”, cât e valabilă sesiunea Stripe (31 de minute).
- Un proces de fundal (`ReservationExpiryWorker`, la fiecare 30 s) anulează rezervările expirate și pune locurile înapoi în stoc, chiar dacă nimeni nu mai deschide comanda.
- Înainte să anuleze o comandă care a ajuns la plată, întreabă Stripe: dacă s-a plătit, dar webhook-ul nu a ajuns, emite biletele în loc s-o anuleze. Dacă Stripe nu răspunde, amână (nu anulează niciodată „pe ghicite”).

## Retrimiterea biletelor pe email
Pe pagina unei comenzi plătite, butonul **Retrimite biletele pe email** trimite din nou aceleași bilete (aceleași coduri QR) la adresa contului (`POST /api/orders/{id}/resend-tickets`). Cel mult o dată la 2 minute pe comandă (altfel `429`, cu antetul `Retry-After`). Dacă trimiterea eșuase de mai multe ori și procesul de fundal renunțase, retrimiterea o repornește.

## Panoul organizatorului
Organizatorii văd în meniu **Panou** (`/organizator`): toate evenimentele lor, cu vânzările pe scurt (întâi cele care urmează). Pe pagina unui eveniment:
- **cifrele principale**: bilete vândute din total, încasări (fără rambursări), câte bilete au fost scanate la intrare;
- **pe categorii** (Standard, VIP...): vândute, intrate, libere, încasări;
- **participanții**: fiecare bilet emis, cu numele și emailul cumpărătorului; căutare după nume, email sau codul scurt; filtre (au intrat / încă n-au intrat / rambursați); buton **Rambursează** pe comandă.

Se numără doar comenzile plătite; rezervările neplătite apar separat. API: `GET /api/organizer/dashboard`, `GET /api/organizer/events/{id}/sales`, `GET /api/organizer/events/{id}/attendees`. Evenimentele și tipurile de bilete se creează deocamdată din Swagger.

## Anulare și rambursare
- **Clientul** își poate anula singur o comandă plătită, din pagina comenzii, până cu **24 de ore** înainte de eveniment, dacă niciun bilet n-a fost folosit. Primește toți banii înapoi pe card (rambursare Stripe).
- **Organizatorul** (pentru evenimentele lui) și **Adminul** pot rambursa oricând: `POST /api/manage/orders/{id}/refund` (ex. un eveniment anulat).
- Biletele se **anulează în aceeași clipă** în care începe rambursarea: un bilet rambursat e respins la intrare, iar un bilet deja scanat oprește rambursarea (niciodată amândouă).
- Rambursarea la Stripe se cere cu o **cheie de idempotență**: oricâte reîncercări, banii pleacă o singură dată. Dacă Stripe nu răspunde, comanda rămâne „Rambursare în curs”, iar procesul de fundal o reîncearcă.
- Anularea unei rezervări neplătite **închide întâi pagina de plată Stripe**, ca nimeni să nu mai poată plăti după ce locurile s-au eliberat. Dacă totuși ajunge o plată pentru o comandă anulată, banii se dau înapoi automat.

## Scannerul de bilete (de pe telefon)
Pagina **Scanează** (`/scaneaza`) e pentru personal (rolurile Staff, Organizer, Admin). Rolul îl schimbă un Admin (`PUT /api/users/{id}/role`, din Swagger).

Browserele permit camera **doar pe pagini securizate**: HTTPS sau `localhost`. De aceea, ca să scanezi cu telefonul:
1. Telefonul și laptopul trebuie să fie pe aceeași rețea Wi-Fi.
2. În folderul clientului: `npm run start:phone` (pornește Angular cu HTTPS, vizibil în rețea; API-ul rămâne pe laptop, prin proxy).
3. Află adresa laptopului (`ipconfig`, adresa IPv4 din rețeaua Wi-Fi, de ex. `192.168.0.127`) și deschide pe telefon `https://<adresa>:4200/scaneaza`.
4. Telefonul avertizează că certificatul nu e de încredere (e unul de test, generat local): alege „Avansat” → „Continuă”. Apoi permite accesul la cameră.
5. Dacă nu se deschide, Windows Firewall poate bloca portul 4200 pentru rețele private; permite-l când apare fereastra.

Fără cameră (sau dacă nu merge), codul se poate introduce de mână: cele 8 caractere de sub codul QR sau tot codul biletului.

Fiecare cod citit se verifică pe server (`POST /api/tickets/validate`):
- **verde, „Bilet valid”**: biletul e plătit și nefolosit; din acest moment e marcat ca folosit;
- **galben, „Deja utilizat”**: a mai fost scanat (se vede și ora primei scanări);
- **roșu, „Respins”**: cod inexistent, comandă rambursată/anulată sau biletul altui organizator (un Organizer validează doar biletele evenimentelor lui; Staff și Admin, pe toate).

Dacă două telefoane scanează același bilet în același moment, intră o singură persoană: marcarea se face printr-un singur `UPDATE ... WHERE IsUsed = false`, atomic în baza de date.

## Teste
- Backend (xUnit): `dotnet test` — inclusiv teste de cursă cu conexiuni paralele la o bază SQLite reală.
- Frontend (Vitest): în folderul clientului, `npm test -- --watch=false`.

Regulile critice au etichete și se pot rula separat:

| Regulă | Comandă | Ce garantează |
|---|---|---|
| Bilet de unică folosință | `dotnet test --filter Regula=BiletUnic` | un bilet intră o singură dată, chiar cu scanări simultane; un bilet rambursat e respins |
| Stoc | `dotnet test --filter Regula=Stoc` | nu se vând mai multe locuri decât capacitatea; rezervările expirate eliberează locurile o singură dată |
| Emitere doar din webhook | `dotnet test --filter Regula=EmitereWebhook` | biletele apar doar după confirmarea Stripe (semnătură verificată), niciodată pentru că un client spune „am plătit” |

`CriticalRulesGuardTests` sunt „gărzi”: verifică în codul sursă că biletele se creează, comenzile devin plătite, biletele se marchează folosite și stocul se modifică **fiecare într-un singur loc**, și că orice endpoint care modifică date cere login (în afară de cont nou, login și webhook-ul Stripe).

**Acoperirea** (ce procent din cod rulează testele) apare la fiecare rulare CI, în pagina rulării (Summary), iar raportul HTML se descarcă de acolo (`coverage-report`). Local:
```
dotnet test --collect:"XPlat Code Coverage" --results-directory coverage
dotnet tool restore
dotnet tool run reportgenerator -reports:"coverage/**/coverage.cobertura.xml" -targetdir:coverage/report -reporttypes:HtmlInline "-classfilters:-System.*;-Microsoft.*;-EventPass.Server.Data.Migrations.*"
```

## Documentație
- [Raport 2 — Proiectare și implementare minimală](docs/raport-2.md) (și în [PDF](docs/raport-2.pdf)): arhitectura, modelul de date, fluxul plății, API-ul, securitatea, testele, capturi de ecran.
- PDF-ul se regenerează din Markdown (cu diagramele): în `docs/tools`, `npm install`, apoi `npm run raport-2`.

## Secrete
Nu se păstrează în repository. Se folosesc User Secrets local și GitHub Secrets în CI.

## Flux de lucru (trasabilitate)
- Fiecare funcționalitate are un **Issue**, atribuit unui **Milestone** (Raport 1, Raport 2, Raport final)
- Un **branch** per funcționalitate: `feature/<nume>`
- Commit-uri mici, cu referință la issue: `Add Stripe webhook endpoint (#7)`
- **Pull Request** cu `Closes #<nr>`; GitHub Actions rulează build și teste
- Progresul se vede în tabloul **GitHub Projects**

## Structură (planificată)
```
EventPass.Server/     # ASP.NET Core Web API
EventPass.Tests/      # xUnit
eventpass.client/     # Angular
docker-compose.yml
```
