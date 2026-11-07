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
| Cod QR | `QRCoder` (generare), `html5-qrcode` (scanare) |
| Email | `MailKit` + SMTP (în dezvoltare: `smtp4dev`, server SMTP de test) |
| API | Swagger / OpenAPI |
| Teste | xUnit |
| CI | GitHub Actions |
| Containere | Docker Compose (PostgreSQL + SMTP de test) |
| Management | GitHub Projects, Issues, Milestones |

## Cerințe
- Visual Studio Community (workload ASP.NET) și .NET 10 SDK
- Node.js LTS și Angular CLI
- PostgreSQL (local sau Docker)
- Git, `dotnet-ef`
- Cont Stripe în mod test + Stripe CLI (pentru webhook-uri locale)
- `smtp4dev` pentru emailuri de test (unealtă locală a proiectului, se instalează cu `dotnet tool restore`)

## Rulare
1. `git clone` al repository-ului
2. În folderul clientului: `npm install`
3. Setează secretele (User Secrets): șirul de conexiune la baza de date, `Jwt:Key`, `SeedAdmin:Email` și `SeedAdmin:Password`, `Stripe:SecretKey`, `Stripe:WebhookSecret` (pentru un server SMTP real: și `Smtp:Username`, `Smtp:Password`)
4. `dotnet ef database update --project EventPass.Server`
5. Pornește serverul SMTP de test (emailurile se văd în browser, la http://localhost:5025):
   `dotnet tool restore` apoi `dotnet tool run smtp4dev -- --smtpport=2525 --urls=http://localhost:5025 --db="" --nousersettings`
6. Pornește webhook-urile locale: `stripe listen --forward-to http://localhost:5003/api/webhooks/stripe`
   (secretul `whsec_...` pentru `Stripe:WebhookSecret` îl afișează `stripe listen --print-secret`)
7. Pornește API-ul (`dotnet run --project EventPass.Server --launch-profile http`) și clientul (`cd eventpass.client`, `npm start`), apoi deschide http://localhost:4200

Carduri de test Stripe: `4242 4242 4242 4242` (succes), orice dată viitoare și orice CVC.

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
