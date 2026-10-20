# EventPass

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
| Email | `MailKit` + SMTP (Mailtrap / Papercut în dezvoltare) |
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
- Mailtrap sau Papercut pentru emailuri de test

## Rulare
1. `git clone` al repository-ului
2. În folderul clientului: `npm install`
3. Setează secretele (User Secrets): șirul de conexiune la baza de date, cheia JWT, `Stripe:SecretKey`, `Stripe:WebhookSecret`, parola SMTP
4. `dotnet ef database update`
5. Pornește webhook-urile locale: `stripe listen --forward-to https://localhost:<port>/api/webhooks/stripe`
6. Deschide soluția în Visual Studio și apasă F5

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
