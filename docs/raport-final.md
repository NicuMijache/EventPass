<!-- pdf:capitole-pe-pagina-noua -->
<!-- pdf:cuprins-cu-pagini -->
<div class="coperta">
<div>
<p class="coperta__sus">PROIECT III — INSTRUMENTE PENTRU DEZVOLTAREA PROGRAMELOR</p>
<p class="coperta__marca">EVENTPASS</p>
<h1 class="coperta__titlu">EventPass — Raport final</h1>
<p class="coperta__sub">Bilete la evenimente, cu plată online și cod QR validat la intrare</p>
</div>
<table class="coperta__date">
<tr><td>Student</td><td>Nicu Mijache</td></tr>
<tr><td>Grupa</td><td>CR4.S2A</td></tr>
<tr><td>Cadru didactic</td><td>C. Ivanescu</td></tr>
<tr><td>Repository</td><td><a href="https://github.com/NicuMijache/EventPass">github.com/NicuMijache/EventPass</a></td></tr>
<tr><td>Etapa</td><td>Raport final: documentație, demonstrație, prezentare (milestone „Raport final”, issue-urile #16–#24)</td></tr>
<tr><td>Data</td><td>noiembrie 2026</td></tr>
</table>
</div>

<!-- CUPRINS:START -->
## Cuprins

1. [Rezumat](#1-rezumat)
2. [Introducere](#2-introducere)
3. [Cerințe](#3-cerințe)
4. [Cazuri de utilizare](#4-cazuri-de-utilizare)
5. [Arhitectura și tehnologiile](#5-arhitectura-și-tehnologiile)
6. [Modelul de date](#6-modelul-de-date)
7. [Implementarea, pe funcționalități](#7-implementarea-pe-funcționalități)
8. [Problemele grele și cum le-am rezolvat](#8-problemele-grele-și-cum-le-am-rezolvat)
9. [Securitate](#9-securitate)
10. [Testare](#10-testare)
11. [Managementul proiectului](#11-managementul-proiectului)
12. [Rulare și instalare](#12-rulare-și-instalare)
13. [Limitări și dezvoltări viitoare](#13-limitări-și-dezvoltări-viitoare)
14. [Concluzii](#14-concluzii)
15. [Bibliografie](#15-bibliografie)
16. [Anexe](#16-anexe)
<!-- CUPRINS:END -->

## 1. Rezumat

Biletele tipărite sau trimise ca fișier se copiază ușor, iar aceeași poză a unui bilet poate deschide ușa de mai multe ori. **EventPass** este o aplicație web în care organizatorii pun în vânzare bilete, clienții le plătesc online cu cardul (Stripe Checkout, mod test), iar fiecare bilet primește un **cod QR unic**, trimis pe email. La intrare, personalul scanează codul cu camera telefonului, direct din browser, iar **serverul decide**: un bilet trece **o singură dată**, chiar dacă două telefoane îl scanează în aceeași secundă. Aplicația are patru roluri (Client, Organizer, Staff, Admin) și acoperă tot drumul unui bilet: rezervarea atomică a locurilor, confirmarea plății doar prin webhook-ul semnat de Stripe, emiterea biletelor, emailul trimis de un proces de fundal cu reîncercări, scanarea, rambursarea și panoul de vânzări al organizatorului. Proiectul are **33 de endpoint-uri REST**, **300 de teste backend** (xUnit) și **186 de teste frontend** (Vitest), toate trecând; acoperirea codului backend este de **79,4% din linii**, iar serviciile cu regulile critice au între 92% și 100%. Dezvoltarea a fost urmărită în GitHub: **24 de issue-uri**, **20 de Pull Request-uri** integrate, fiecare verificat automat de GitHub Actions (39 din cele 40 de rulări CI au trecut).

> Toate cifrele din acest raport au fost **măsurate** pe 15 noiembrie 2026, pe ramura `main` (commit `48b07af`); comanda folosită apare lângă fiecare cifră.

## 2. Introducere

### 2.1 Problema

La un eveniment cu bilete apar patru probleme practice:

- **Frauda.** Un bilet PDF sau o captură de ecran poate fi trimisă mai multor persoane; fără o verificare centralizată, toți intră.
- **Vânzarea peste capacitate.** Dacă doi clienți cumpără ultimul loc în aceeași clipă, un sistem naiv le vinde amândurora.
- **Plata nesigură.** Dacă site-ul „crede” browserul că plata a reușit, un utilizator rău-intenționat poate obține bilete fără să plătească.
- **Cozile și evidența manuală.** Fără un scanner rapid și fără un panou de vânzări, organizatorul nu știe câte bilete a vândut și câți oameni au intrat.

### 2.2 Obiective

Obiectivele au fost stabilite în Raportul 1 (secțiunea 1.1) și au fost atinse astfel:

| Obiectiv (Raport 1) | Rezultat |
|---|---|
| Evenimente cu mai multe categorii de bilete, fiecare cu preț și stoc | Realizat (API; în interfață doar vizualizare, vezi limitările) |
| Cumpărare de unul sau mai multe bilete, plată pe Stripe Checkout | Realizat, verificat cu cardul de test Stripe |
| Bilete emise doar după confirmarea Stripe (webhook) | Realizat și păzit de teste |
| Bilet cu identificator aleatoriu (GUID) în cod QR, trimis pe email | Realizat |
| La intrare, un bilet e acceptat o singură dată | Realizat, inclusiv la scanări simultane |
| Dezvoltare trasabilă în GitHub | Realizat: issue → ramură → PR → CI |

### 2.3 Cui se adresează

Organizatorilor mici și mijlocii (cluburi, teatre, conferințe studențești), care vor să vândă bilete fără o platformă comercială scumpă, și personalului de la intrare, care are nevoie doar de un telefon cu browser.

## 3. Cerințe

Cerințele și codurile sunt **exact cele din Raportul 1** (capitolele 3–5). Coloana „Stare” spune cinstit cât s-a realizat; „parțial” și abaterile sunt explicate sub tabele și în capitolul 13.

### 3.1 Cerințe funcționale

| Cod | Cerință (Raport 1) | Prior. | Issue | Stare |
|---|---|---|---|---|
| CF-01 | Cont cu email și parolă, autentificare; sesiunea folosește un token JWT | M | #7 | Realizat |
| CF-02 | Organizatorul creează, editează și publică un eveniment (titlu, descriere, dată, locație, imagine) | M | #8 | Realizat în API; fără pagini în interfață |
| CF-03 | Organizatorul definește categorii de bilete, fiecare cu preț și stoc | M | #8 | Realizat în API; fără pagini în interfață |
| CF-04 | Clientul vede evenimentele publicate și le poate filtra după dată și oraș | M | #9 | Parțial: filtre după oraș, locație și text; după dată doar ordonare și ascunderea celor trecute |
| CF-05 | Alegerea numărului de bilete pe categorii; locurile rezervate temporar (15 minute) | M | #10 | Realizat |
| CF-06 | Redirecționare către Stripe Checkout pentru plată | M | #11 | Realizat |
| CF-07 | Webhook Stripe cu verificarea semnăturii; abia apoi comanda devine plătită și se emit biletele | M | #12 | Realizat |
| CF-08 | Cod QR care conține doar tokenul biletului | M | #13 | Realizat |
| CF-09 | Email cu biletele, cu codul QR afișat în mesaj | M | #14 | Realizat |
| CF-10 | Staff-ul scanează codul QR din browserul telefonului | M | #16 | Realizat (jsQR în loc de html5-qrcode) |
| CF-11 | Serverul validează biletul (există, aparține evenimentului, plătit, nefolosit) și îl marchează folosit | M | #17 | Realizat |
| CF-12 | Rezervările neplătite expiră și locurile revin în stoc | S | #18 | Realizat |
| CF-13 | Organizatorul anulează o comandă; plata se rambursează prin Stripe, biletele devin invalide | S | #19 | Realizat (și clientul, până cu 24 h înainte) |
| CF-14 | Organizatorul vede biletele vândute, încasările și câți participanți au intrat | S | #20 | Realizat |
| CF-15 | Clientul poate cere retrimiterea emailului cu biletele | C | #21 | Realizat |
| CF-16 | Administratorul schimbă rolul unui utilizator și poate ascunde un eveniment | C | #7 | Parțial: schimbarea rolului da; ascunderea unui eveniment de către Admin nu |

### 3.2 Cerințe nefuncționale

| Cod | Cerință (Raport 1, pe scurt) | Cum e îndeplinită | Dovadă |
|---|---|---|---|
| CNF-01 | Datele cardului nu ajung pe serverul EventPass | Cardul se introduce doar pe pagina Stripe Checkout | fluxul din 7.4; nicio tabelă nu are câmpuri de card |
| CNF-02 | Parole doar ca hash; rute protejate cu JWT și rol | `PasswordHasher` din ASP.NET Core Identity (PBKDF2 cu sare), `[Authorize(Roles = …)]` | `AuthControllerTests`, garda `OriceEndpointCareModificaDate_CereLogin…` |
| CNF-03 | Webhook fără semnătură validă → 400; secrete în afara codului | `EventUtility.ConstructEvent` + User Secrets / GitHub Secrets | 6 teste `Receive_…_Returneaza400` |
| CNF-04 | Tokenul QR e un GUID aleatoriu; validitatea o decide baza de date | `Guid.NewGuid()` (122 de biți aleatori), index unic | garda `BazaDeDate_RefuzaDouaBileteCuAcelasiToken` |
| CNF-05 | Stocul nu devine negativ la cumpărări simultane | `UPDATE … WHERE Sold + n <= Capacity` + `CHECK` | `StockRaceTests` (30 de clienți, 5 locuri) |
| CNF-06 | Webhook idempotent | `UPDATE … WHERE Status = Pending` | `Receive_AcelasiMesajDeDouaOri_NuDuplicaBiletele` |
| CNF-07 | Răspunsul la validare în sub o secundă | măsurat local: **mediana 9 ms**, maximum 20 ms, din 50 de cereri (`curl -w %{time_total}`); **pe telefon, prin Wi-Fi, nu a fost măsurat** | secțiunea 10.4 |
| CNF-08 | Interfață responsive; scannerul fără aplicație instalată | pagini adaptate pentru telefon; scanner în browser | capturile 7.8 |
| CNF-09 | Regulile critice au teste xUnit, rulate la fiecare push | etichete `Regula=BiletUnic`, `Regula=Stoc`, `Regula=EmitereWebhook`; CI | 36 + 21 + 34 de teste |
| CNF-10 | Proiectul pornește cu .NET 10, Node.js și Docker urmând README-ul | `docker-compose.yml` (PostgreSQL + smtp4dev), README cu două variante | job-ul CI „Docker Compose (validare)” |

### 3.3 Reguli de business

| Cod | Regulă (Raport 1) | Stare | Unde în cod |
|---|---|---|---|
| RB-01 | Bilet emis doar pentru o comandă `Paid`; `Paid` se setează doar din webhook | Realizat (plus recuperarea la Stripe, vezi 8.2) | `OrderPaymentService.ConfirmAsync` |
| RB-02 | Stări: `Pending → Paid` sau `Pending → Expired/Cancelled`; `Paid → Refunded` | Realizat, cu două ajustări: expirarea duce tot în `Cancelled`, iar între `Paid` și `Refunded` există `RefundPending` | `OrderStatus` |
| RB-03 | Un bilet e validat o singură dată; se salvează data, ora și cine a scanat | Parțial: data și ora se salvează (`UsedAt`); cine a scanat apare doar în jurnalul serverului, nu în baza de date | `TicketValidationService` |
| RB-04 | Bilet dintr-o comandă rambursată sau dintr-un eveniment anulat: respins | Realizat pentru rambursare; „eveniment anulat” nu există ca stare separată (organizatorul retrage evenimentul și rambursează comenzile) | `Ticket.VoidedAt` |
| RB-05 | Cel mult 10 bilete pe comandă | Realizat (plus maximum 3 rezervări neplătite pe utilizator) | `OrdersController.MaxTicketsPerOrder` |
| RB-06 | Vânzarea se închide la începerea evenimentului sau când stocul e zero | Realizat | `OrdersController` (verifică `StartsAt`), `SeatReservationService` |
| RB-07 | Evenimentul cu bilete vândute nu se poate șterge | Realizat (răspuns 409; se poate doar retrage din vânzare) | `OrganizerEventsController.Delete` |
| RB-08 | Staff-ul validează doar la evenimentele la care a fost atribuit | **Neimplementat, abatere asumată:** Staff validează la orice eveniment; Organizer doar la evenimentele lui | `TicketValidationService`, capitolul 13 |

## 4. Cazuri de utilizare

Diagrama de mai jos actualizează Figura 1 din Raportul 1 cu ce s-a implementat efectiv.

![Cazurile de utilizare: actorii Client, Organizer, Staff, Admin și sistemul extern Stripe; liniile punctate sunt relațiile «include»](img/cazuri-utilizare.svg)

**UC-01 „Cumpără bilete”** și **UC-02 „Validează biletul la intrare”** sunt descrise pe larg în Raportul 1 (secțiunile 6.1 și 6.2). Fluxurile lor alternative au devenit teste automate:

| Flux alternativ (Raport 1) | Comportamentul implementat | Test |
|---|---|---|
| UC-01 3a: stocul s-a epuizat între timp | Mesaj „s-au epuizat”, comanda nu se creează | `TreizeciDeClientiSimultan_PentruCinciLocuri_DoarCinciPrimescBilete` |
| UC-01 4a: plata refuzată sau pagina închisă | Comanda rămâne `Pending`, expiră, locurile revin | `ExpirataCuSesiuneaExpirataLaStripe_SeAnuleaza` |
| UC-01 5a: Stripe retrimite webhook-ul | Nu se emit bilete în plus | `Receive_AcelasiMesajDeDouaOri_NuDuplicaBiletele` |
| UC-02 4a: token inexistent / alt organizator | Roșu, „Respins” | `TokenInexistent_NuEsteGasit`, `Organizatorul_NuPoateValidaBileteleAltuiOrganizator` |
| UC-02 4b: bilet deja folosit | Galben, „Deja utilizat”, cu ora primei scanări | `AlDoileaScan_ReturneazaDejaUtilizat_CuOraPrimeiScanari` |
| UC-02 4c: comandă rambursată | Roșu, „Bilet anulat: comanda a fost rambursată” | `BiletRambursat_LaIntrare_EsteRespins` |

## 5. Arhitectura și tehnologiile

![Arhitectura finală: browserul și telefonul vorbesc doar cu API-ul; Stripe confirmă plata prin webhook; două procese de fundal se ocupă de rezervările expirate și de emailuri](img/arhitectura-finala.svg)

Aplicația are trei componente proprii, frontend-ul, backend-ul și baza de date, plus două servicii externe: Stripe și serverul SMTP. Regula de bază: **serverul e singura sursă de adevăr**. Browserul doar afișează și cere; nu decide nimic despre plăți sau bilete.

**Backend-ul este organizat pe straturi:** `Controllers` (HTTP, drepturi de acces), `Services` (regulile de business, testabile separat), `Data` (EF Core și migrări), `Models` (entitățile) și `Dtos` (ce intră și ce iese prin API). Dependențele externe stau în spatele unor interfețe (`IPaymentGateway`, `IEmailSender`, `IQrCodeGenerator`), așa că testele folosesc variante false, fără internet.

### 5.1 Tehnologii și justificarea alegerilor

| Zonă | Tehnologie (versiune măsurată) | De ce |
|---|---|---|
| Backend | .NET 10, ASP.NET Core Web API, EF Core 10 | Tipizare puternică, injecție de dependențe nativă, `ExecuteUpdate` pentru UPDATE-uri atomice fără SQL scris de mână |
| Frontend | Angular 22, TypeScript 6 | Componente, rutare cu *guards* pe roluri, formulare reactive; structură clară pentru o aplicație cu mai multe roluri |
| Bază de date | PostgreSQL 18 (Npgsql 10) | Tranzacții reale și blocări pe rând, constrângeri `CHECK`, indexuri unice: exact ce trebuie pentru stoc și bilete |
| Plăți | Stripe Checkout + Webhooks, `Stripe.net` 53.1 | Vezi 5.2 |
| Cod QR | `QRCoder` 1.8.1 (server), `jsQR` 1.4 (browser) | QRCoder generează PNG fără dependențe externe; la scanare, jsQR a citit 200 din 200 de coduri generate de noi, iar ZXing (motorul din html5-qrcode, ales inițial) a pierdut 3–10% (măsurat în #16) |
| Email | `MailKit` 4.18.2, `smtp4dev` 3.16.1 | MailKit e biblioteca recomandată de Microsoft pentru SMTP; smtp4dev prinde emailurile în dezvoltare, fără să le livreze |
| Autentificare | JWT Bearer (HMAC-SHA256, 60 min) | Vezi 5.2 |
| Teste | xUnit 2.9, SQLite în memorie, Vitest 5, coverlet + ReportGenerator | Teste rapide, pe o bază de date reală (nu simulată) |
| CI | GitHub Actions | Integrat cu Pull Request-urile; trei job-uri: backend, frontend, validarea Docker Compose |
| Mediu | Docker Compose (PostgreSQL + smtp4dev) | Un singur `docker compose up -d` în loc de instalări manuale |

Versiunile sunt cele din `EventPass.Server.csproj`, `EventPass.Tests.csproj`, `package.json` și `docker-compose.yml`.

### 5.2 Deciziile importante

**De ce PostgreSQL.** Regulile critice (stocul, biletul de unică folosință) se sprijină pe garanțiile bazei de date: un `UPDATE` cu condiție e atomic, iar o regulă `CHECK` nu poate fi ocolită de un cod greșit. PostgreSQL le oferă pe toate, e gratuit, rulează identic în Docker și local.

**De ce Stripe Checkout și nu un formular de card propriu.** Cu un formular propriu, numărul cardului ar trece prin serverul nostru, ceea ce aduce obligațiile standardului PCI DSS și un risc mare. Checkout e o pagină găzduită de Stripe: cardul nu ajunge la EventPass (CNF-01), iar 3-D Secure, validarea cardului și traducerea în română vin gata făcute.

**De ce webhook și nu pagina de întoarcere.** Întoarcerea pe site (`?plata=succes`) e doar o adresă din browser: oricine o poate scrie de mână. Webhook-ul e un mesaj de la Stripe către server, **semnat** (HMAC cu un secret comun) și verificat. În plus, webhook-ul ajunge chiar dacă clientul închide pagina după plată.

**De ce JWT.** API-ul nu ține sesiuni în memorie: tokenul semnat conține id-ul și rolul, se verifică fără interogări în baza de date și merge la fel din browser, de pe telefon și din Swagger. Dezavantajul (tokenul nu se poate revoca înainte să expire) e limitat prin durata scurtă: 60 de minute.

**De ce un proces de fundal pentru email.** Stripe așteaptă un răspuns rapid la webhook, iar un server SMTP căzut nu trebuie să anuleze o plată reușită. Webhook-ul doar marchează comanda și „trezește” procesul de email. Starea (trimis / încercări / ultima încercare) stă în baza de date, deci nimic nu se pierde la o repornire, iar eșecurile se reîncearcă după 2, 4, 8… minute (maximum 6 încercări).

## 6. Modelul de date

```mermaid
erDiagram
    USER ||--o{ EVENT : "organizează"
    USER ||--o{ ORDER : "plasează"
    EVENT ||--o{ TICKET_TYPE : "are"
    ORDER ||--|{ ORDER_ITEM : "conține"
    TICKET_TYPE ||--o{ ORDER_ITEM : "comandat în"
    ORDER ||--o{ TICKET : "primește"
    TICKET_TYPE ||--o{ TICKET : "de tipul"

    USER {
        int Id PK
        string Email UK
        string PasswordHash
        string FullName
        string Role "Client|Organizer|Staff|Admin"
        datetime CreatedAt
    }
    EVENT {
        int Id PK
        string Title
        string Description
        string Venue
        string City
        string ImagePath
        datetime StartsAt
        datetime CreatedAt
        bool IsPublished
        int OrganizerId FK
    }
    TICKET_TYPE {
        int Id PK
        string Name
        decimal Price "CHECK >= 0"
        int Capacity
        int Sold "CHECK 0..Capacity"
        int EventId FK
    }
    ORDER {
        int Id PK
        string Status "Pending|Paid|Cancelled|RefundPending|Refunded"
        decimal TotalAmount
        string StripeSessionId UK
        datetime CreatedAt
        datetime ExpiresAt
        datetime PaidAt
        datetime CancelledAt
        datetime TicketsEmailedAt
        int TicketsEmailAttempts
        datetime TicketsEmailLastAttemptAt
        datetime RefundRequestedAt
        datetime RefundedAt
        string StripeRefundId
        int RefundAttempts
        int UserId FK
    }
    ORDER_ITEM {
        int Id PK
        int Quantity "CHECK > 0"
        decimal UnitPrice
        int OrderId FK
        int TicketTypeId FK
    }
    TICKET {
        int Id PK
        guid Token UK "conținutul QR"
        bool IsUsed
        datetime UsedAt
        datetime VoidedAt "anulat la rambursare"
        int OrderId FK
        int TicketTypeId FK
    }
```

### 6.1 Stările unei comenzi

```mermaid
stateDiagram-v2
    direction LR
    [*] --> Pending: Rezervă (locurile scad din stoc)
    Pending --> Paid: webhook Stripe semnat / verificare la Stripe
    Pending --> Cancelled: expirare (15 min) sau anulare
    Paid --> RefundPending: rambursare cerută (biletele se anulează)
    RefundPending --> Refunded: Stripe confirmă banii întorși
    Cancelled --> [*]
    Refunded --> [*]
```

Fiecare săgeată e un singur `UPDATE … WHERE Status = <starea de plecare>`. Dacă două evenimente încearcă aceeași tranziție simultan (de exemplu webhook-ul și expirarea), doar unul găsește rândul în starea așteptată; celălalt modifică 0 rânduri și știe că a pierdut.

### 6.2 Regulile de integritate din baza de date

Acestea există chiar în schema PostgreSQL, create de migrările EF Core (`AppDbContext.OnModelCreating`), deci se aplică indiferent de ce face codul C#:

| Regulă | Tip | Ce previne |
|---|---|---|
| `CK_TicketType_Sold`: `Sold >= 0 AND Sold <= Capacity` | `CHECK` | vânzarea peste capacitate, stoc negativ |
| `CK_TicketType_Price`: `Price >= 0` | `CHECK` | preț negativ |
| `CK_OrderItem_Quantity`: `Quantity > 0` | `CHECK` | linii de comandă goale |
| `Tickets.Token` | index unic | două bilete cu același cod QR |
| `Orders.StripeSessionId` | index unic | o plată Stripe legată de două comenzi |
| `Users.Email` | index unic | două conturi cu același email |
| Comenzi și bilete: `OnDelete(Restrict)` | cheie străină | ștergerea accidentală a istoricului financiar |
| Bani: `decimal(10,2)`; stări: text | tipuri | erori de rotunjire; o bază de date lizibilă |

Schema are **6 migrări** versionate (`EventPass.Server/Data/Migrations`, de la `InitialCreate` la `AddRefunds`), aplicate cu `dotnet ef database update`.

## 7. Implementarea, pe funcționalități

Capturile au fost făcute automat, într-un Chrome real, pe o bază de date nouă, cu scriptul `docs/tools/demo-capturi.mjs` (vezi capitolul 10.5). Datele (conturi, evenimente, postere) au fost create prin API cu `docs/tools/seed-demo.mjs`.

### 7.1 Conturi și roluri (CF-01, CF-16)

**Ce face:** înregistrare, login, patru roluri. Contul nou e `Client`; Adminul schimbă rolurile (`PUT /api/users/{id}/role`), iar primul Admin se creează la pornire din User Secrets. **Cum:** parola se salvează ca hash PBKDF2 (`PasswordHasher`). Login-ul întoarce un JWT semnat cu id-ul, emailul, numele și rolul. În Angular, un *interceptor* adaugă tokenul la fiecare cerere, iar *guard*-urile ascund paginile fără drept. **Decizia importantă:** resursa altcuiva (comandă, eveniment) întoarce **404, nu 403**: serverul nu confirmă nici măcar că există.

### 7.2 Evenimente, filtre și postere (CF-02 – CF-04)

Pagina principală arată evenimentele publicate și viitoare, cu filtre după oraș și locație și căutare după text. Posterele se încarcă prin API (JPG, PNG sau WebP, maximum 5 MB). **Tipul se verifică după conținutul fișierului**, nu după extensie, iar numele fișierului îl generează serverul.

![Pagina principală: 7 evenimente de demonstrație, în 4 orașe, cu filtre](img/final/01-pagina-principala.png)

### 7.3 Rezervarea locurilor (CF-05, CF-12)

Clientul alege cantitățile, iar „Rezervă biletele” scade locurile din stoc **atomic** (vezi 8.1) și creează o comandă `Pending`, ținută 15 minute. Prețul îl calculează serverul din baza de date: clientul trimite doar *ce* vrea și *cât*.

![Pagina evenimentului: categoriile, prețurile, totalul calculat pe loc](img/final/02-eveniment.png)

![Comanda rezervată: numărătoarea inversă arată cât timp mai sunt păstrate locurile](img/final/03-comanda-rezervata.png)

### 7.4 Plata prin Stripe Checkout (CF-06, CF-07)

„Plătește” creează o sesiune Stripe pentru suma exactă a comenzii și trimite clientul pe pagina Stripe. Plata se confirmă **doar** prin webhook (vezi 8.2).

```mermaid
sequenceDiagram
    autonumber
    actor C as Client
    participant A as Angular
    participant API as Web API
    participant DB as PostgreSQL
    participant S as Stripe
    participant W as Proces email
    C->>A: Rezervă 2 bilete
    A->>API: POST /api/orders
    API->>DB: UPDATE atomic (stoc) + Order Pending
    C->>A: Plătește
    A->>API: POST /orders/{id}/checkout
    API->>S: creează sesiunea (suma, moneda)
    A->>S: clientul plătește pe pagina Stripe
    S-->>A: înapoi pe site (NU e dovada plății)
    S->>API: webhook semnat
    API->>API: semnătură, vechime, sumă, monedă
    API->>DB: Pending → Paid + bilete (o tranzacție)
    API-->>S: 200 OK
    API->>W: „sunt bilete de trimis”
    W-->>C: email cu codurile QR
    A->>API: întreabă din 2 în 2 s
    API-->>A: Paid, cu biletele
```

![Pagina Stripe Checkout (mod test), cu cardul de test 4242 4242 4242 4242; cardul nu trece prin serverul EventPass](img/final/04-plata-stripe.png)

![Comanda plătită: biletele apar singure după confirmarea prin webhook, fiecare cu cod QR și cod scurt](img/final/05-comanda-platita.png)

### 7.5 Codul QR și emailul (CF-08, CF-09, CF-15)

QR-ul conține doar tokenul biletului (GUID cu majuscule: modul alfanumeric al standardului QR, deci pătrățele mai mari), cu corecție de erori nivel Q (~25%). Sub cod apare un **cod scurt** de 8 caractere, pentru introducere manuală. Emailul pleacă din procesul de fundal, cu imaginile QR incluse în mesaj (`cid:`).

![Emailul primit în smtp4dev: evenimentul și fiecare bilet cu codul QR și codul scurt](img/final/06-email-bilete.png)

**Retrimiterea** trimite din nou *aceleași* bilete, cel mult o dată la 2 minute pe comandă (altfel `429 Too Many Requests` cu antetul `Retry-After`). Limita se aplică și imediat după plată, pentru că emailul tocmai a plecat:

![Retrimitere cerută imediat după plată: limita de 2 minute](img/final/07b-retrimitere-limita.png)

![Retrimitere după 2 minute: emailul pleacă din nou](img/final/07-retrimitere.png)

### 7.6 Scannerul și validarea la intrare (CF-10, CF-11)

Pagina `/scaneaza` (rolurile Staff, Organizer, Admin) pornește camera telefonului și citește QR-ul cu jsQR. Ca rezervă, codul scurt se poate introduce de mână. Fiecare cod se verifică pe server (`POST /api/tickets/validate`), iar rezultatul e mare și colorat, ca să se vadă dintr-o privire. Browserele dau acces la cameră doar pe HTTPS sau `localhost`, de aceea pentru telefon există `npm run start:phone` (HTTPS în rețeaua locală).

![Prima scanare: verde, „Bilet valid”; biletul e marcat folosit](img/final/08-scanner-valid.png)

![A doua scanare a aceluiași bilet (de data asta cu codul scurt): galben, „Deja utilizat”, cu ora primei scanări](img/final/09-scanner-deja-utilizat.png)

![Biletul unei comenzi rambursate: roșu, „Respins”](img/final/13-scanner-respins.png)

### 7.7 Panoul organizatorului și rambursarea (CF-13, CF-14)

Organizatorul vede toate evenimentele lui, cu vânzările pe scurt, iar pe pagina unui eveniment: bilete vândute, încasări (fără rambursări), câți au intrat, cifrele pe categorii și lista participanților, cu căutare și filtre. Se numără doar comenzile plătite.

![Panoul organizatorului: vânzări, încasări și bilete validate, pe fiecare eveniment](img/final/10-panou-organizator.png)

![Un eveniment după o scanare și o rambursare: 5 bilete vândute, 600 lei încasați, 500 lei rambursați, 1 participant intrat](img/final/14-panou-dupa-scanare.png)

**Rambursarea** o pot cere organizatorul evenimentului și Adminul oricând, iar clientul până cu 24 de ore înainte, dacă niciun bilet nu a fost folosit. Biletele se anulează **în aceeași tranzacție** în care începe rambursarea; banii se cer la Stripe cu o **cheie de idempotență**.

![Lista participanților după rambursarea comenzii #2: biletele ei apar tăiate, „Rambursat”](img/final/12-rambursare.png)

### 7.8 Pe telefon (CNF-08)

<div style="display:flex;gap:10pt;justify-content:center;break-inside:avoid">
<figure style="margin:0"><img src="img/final/15-telefon-acasa.png" style="height:92mm"><figcaption>Pagina principală</figcaption></figure>
<figure style="margin:0"><img src="img/final/16-telefon-bilet.png" style="height:92mm"><figcaption>Biletele clientului</figcaption></figure>
<figure style="margin:0"><img src="img/final/17-telefon-scanner-valid.png" style="height:92mm"><figcaption>Scanner: valid</figcaption></figure>
<figure style="margin:0"><img src="img/final/18-telefon-scanner-deja-utilizat.png" style="height:92mm"><figcaption>Scanner: deja utilizat</figcaption></figure>
</div>

Capturile de telefon sunt făcute cu emularea unui ecran de 390 × 844 de pixeli, în Chrome. Scanarea propriu-zisă cu camera unui telefon real nu poate fi automatizată; ea a fost probată manual la #16 și se arată live la demonstrație.

### 7.9 Documentația API

API-ul e descris automat (OpenAPI, `/openapi/v1.json`) și poate fi încercat din Swagger UI (`/swagger`), inclusiv cu tokenul JWT („Authorize”). Tabelul complet e în Anexa A.

![Swagger UI: fiecare endpoint poate fi încercat din browser](img/final/19-swagger.png)

## 8. Problemele grele și cum le-am rezolvat

Fiecare problemă de mai jos e o situație reală de concurență sau de eșec. Pentru fiecare: **problema → soluția → dovada** (testul automat care o verifică, rulat la fiecare push).

### 8.1 Doi clienți pentru ultimul loc

**Problema.** Varianta naivă „citesc câte locuri sunt libere → dacă e cel puțin unul, îl vând” are o fereastră între citire și scriere: doi clienți citesc amândoi „1 liber” și cumpără amândoi.

**Soluția.** Verificarea și modificarea sunt **o singură comandă SQL**, atomică:

```
UPDATE "TicketTypes" SET "Sold" = "Sold" + @q
WHERE "Id" = @id AND "Sold" + @q <= "Capacity"
```

Baza de date blochează rândul cât timp îl modifică, deci al doilea client vede deja stocul nou: 0 rânduri modificate înseamnă „s-au epuizat”. Toate liniile unei comenzi se rezervă într-o tranzacție (totul sau nimic). Plasa de siguranță: regula `CHECK (Sold <= Capacity)` refuză orice depășire, chiar și dintr-un cod greșit. Stocul se modifică **dintr-un singur loc** (`SeatReservationService`), iar o „gardă” verifică asta în codul sursă.

**Dovada.** `StockRaceTests.TreizeciDeClientiSimultan_PentruCinciLocuri_DoarCinciPrimescBilete`: 30 de clienți, pe conexiuni separate la o bază SQLite reală, pentru 5 locuri; exact 5 reușesc. Plus `CumparaturiDeCateDouaLocuri_NuDepasescNiciodataCapacitatea` și garda `BazaDeDate_RefuzaMaiMulteLocuriVanduteDecatCapacitatea`. Rulare: `dotnet test --filter Regula=Stoc` → **21 de teste**.

### 8.2 Plata confirmată doar de Stripe: mesaje repetate și webhook pierdut

**Problema.** (a) Browserul poate minți („am plătit”). (b) Stripe poate trimite **același** webhook de mai multe ori. (c) Webhook-ul se poate **pierde** (serverul era oprit, rețeaua a căzut), iar clientul a plătit.

**Soluția.**

- (a) Comanda devine `Paid` doar în `OrderPaymentService.ConfirmAsync`, apelat **numai** din webhook (după verificarea semnăturii HMAC, a vechimii sub 5 minute, a sumei, a monedei și a sesiunii) și din verificarea la Stripe de la punctul (c). O gardă verifică în codul sursă că nu există alt apelant.
- (b) **Idempotență:** trecerea e `UPDATE … SET Status = Paid WHERE Id = @id AND Status = Pending`, în aceeași tranzacție cu emiterea biletelor. Al doilea mesaj găsește comanda deja `Paid`, modifică 0 rânduri și nu emite nimic.
- (c) Procesul de fundal care anulează rezervările expirate **întreabă întâi Stripe** despre sesiunea comenzii: dacă a fost plătită, emite biletele în loc s-o anuleze; dacă Stripe nu răspunde, amână. Nu anulează niciodată „pe ghicite”. Dacă totuși ajunge o plată pentru o comandă deja anulată, banii se întorc automat.

**Dovada.** `Receive_AcelasiMesajDeDouaOri_NuDuplicaBiletele`, `Confirm_AceeasiPlataDeDouaOri_EmiteBileteleOSingurDataSiRaspundeAlreadyPaid`, `PlatitaLaStripeDarFaraWebhook_NuSeAnuleaza_CiSeEmitBiletele`, `PlataRecuperata_WebhookulCareVineDupa_NuMaiEmiteBileteInPlus`, `StripeIndisponibil_SeAmana_ApoiSeAnuleazaCandRaspunde`, `Receive_PlataPentruOComandaAnulata_DaBaniiInapoiAutomat`, garda `ClientulIntorsDePeStripe_NuPrimesteBileteFaraConfirmareaStripe`. Rulare: `dotnet test --filter Regula=EmitereWebhook` → **34 de teste**.

### 8.3 Același bilet scanat simultan la două intrări

**Problema.** Două telefoane citesc același QR în aceeași secundă. „Citesc biletul → e nefolosit → îl marchez” lasă să intre două persoane.

**Soluția.** Marcarea e un singur `UPDATE` cu condiția în `WHERE`:

```
UPDATE "Tickets" SET "IsUsed" = true, "UsedAt" = now
WHERE "Id" = @id AND NOT "IsUsed" AND "VoidedAt" IS NULL
  AND comanda este Paid
```

Baza de date execută cele două cereri una după alta: prima modifică 1 rând („Bilet valid”), a doua 0 rânduri și primește „Deja utilizat”, cu ora primei scanări. Biletul nu se poate „demarca” nicăieri în cod (verificat de o gardă).

**Dovada.** `TicketValidationServiceTests.DouaScanariSimultane_DoarUnaEsteValida`, `AlDoileaScan_ReturneazaDejaUtilizat_CuOraPrimeiScanari`, garda `BiletulSeMarcheazaFolositIntrUnSingurLoc_SiNuSeDemarcheazaNiciodata`. Rulare: `dotnet test --filter Regula=BiletUnic` → **36 de teste**. Pe date reale, în repetiția automată: prima scanare „Bilet valid”, a doua „Deja utilizat” (capturile 08 și 09).

### 8.4 Scanare și rambursare în aceeași secundă

**Problema.** Organizatorul rambursează o comandă exact când clientul e scanat la intrare. Rezultatul greșit: și intră, și primește banii înapoi.

**Soluția.** Rambursarea începe într-o tranzacție: comanda trece `Paid → RefundPending` (un singur `UPDATE` condiționat, deci un dublu-click nu rambursează de două ori), apoi biletele **nefolosite** primesc `VoidedAt`. `UPDATE`-ul blochează rândurile biletelor, deci scanarea și rambursarea se execută una după alta:

- dacă **scanarea** a fost prima, biletul e folosit, numărul biletelor anulate e mai mic decât totalul, tranzacția se anulează și comanda rămâne plătită („un bilet a fost folosit”);
- dacă **rambursarea** a fost prima, biletul are `VoidedAt`, iar condiția `VoidedAt IS NULL` din scanare îl respinge.

Banii se cer la Stripe abia după ce biletele sunt anulate, cu o **cheie de idempotență**: oricâte reîncercări, Stripe întoarce banii o singură dată.

**Dovada.** `OrderRefundTests.ScanareSiRambursareInAcelasiTimp_NiciodataAmandoua`, `ZeceCereriSimultane_OSinguraRambursare_LocurileEliberateOData`, `DeDouaOri_BaniiPleacaOSinguraData`, `UnBiletFolosit_NuSeRamburseaza_SiNimicNuSeSchimba`, `StripeIndisponibil_BileteleRamanAnulate_ApoiProcesulDeFundalTermina`.

### 8.5 Rezervări abandonate

**Problema.** Clientul rezervă și pleacă. Fără curățenie, locurile ar rămâne blocate pentru totdeauna. Mai rău: expirarea poate coincide cu plata.

**Soluția.** `ReservationExpiryWorker` rulează la fiecare 30 de secunde și anulează comenzile `Pending` expirate, punând locurile înapoi o singură dată (`UPDATE … WHERE Status = Pending`). O comandă care a ajuns la plată e ținută cât e valabilă sesiunea Stripe (31 de minute, minimul Stripe), ca să nu expire cât clientul e pe pagina de plată. Înainte de anulare, serverul întreabă Stripe (8.2 c). Anularea unei rezervări de către client **închide întâi pagina de plată Stripe**, ca nimeni să nu mai poată plăti după eliberarea locurilor.

**Dovada.** `StockRaceTests.ExpirareaSiCumparareaInAcelasiTimp_StoculRamaneCorect`, `ProcesulDeFundalSiClientul_AnuleazaSimultanAceeasiComanda_LocurileSeElibereazaODataSingura`, `PlataSiExpirareaInAcelasiTimp_ComandaEsteSauPlatitaSauAnulata_NiciodataAmandoua`, `Anulare_CuPaginaDePlataDeschisa_OInchideIntaiLaStripe`.

### 8.6 Emailul care eșuează

**Problema.** Serverul SMTP e căzut sau lent. Dacă emailul s-ar trimite în timpul webhook-ului, Stripe ar primi o eroare și ar retrimite, iar o repornire a serverului ar pierde emailurile „în curs”.

**Soluția.** Starea emailului stă în baza de date (`TicketsEmailedAt`, `TicketsEmailAttempts`, `TicketsEmailLastAttemptAt`). `TicketEmailWorker` e trezit de webhook și trimite. La eșec, notează încercarea și reîncearcă după 2, 4, 8, 16, 32 de minute, cu maximum 6 încercări. O comandă cu probleme nu le blochează pe celelalte. Retrimiterea cerută de client repornește un email abandonat și nu dublează unul aflat deja pe drum.

**Dovada.** `TicketEmailServiceTests.SendPending_CandTrimitereaEsueaza_NuMarcheazaTrimisSiNoteazaIncercarea`, `SendPending_DupaEsec_NuReincearcaImediat_CiDupaPauza`, `SendPending_DupaMaximulDeIncercari_RenuntaSiNuMaiTrimite`, `SendPending_OComandaCareEsueaza_NuBlocheazaPeCelelalte`, `TicketEmailResendTests.EmailulNuAPlecatInca_EDejaPeDrum_NuSeDubleaza`, `DupaCeProcesulARenuntat_RetrimitereaIlRepornește`.

## 9. Securitate

| Amenințare | Măsură | Unde în cod |
|---|---|---|
| Furtul datelor de card | Cardul se introduce doar pe pagina Stripe; serverul nu le vede | `StripePaymentGateway` (creează doar sesiunea) |
| „Am plătit”, fals, din browser | Biletele se emit doar după webhook-ul semnat sau după verificarea la Stripe | `StripeWebhookController`, `OrderPaymentService`, garda din `CriticalRulesGuardTests` |
| Webhook falsificat sau retrimis de un atacator | Semnătură HMAC, toleranță 5 minute, verificarea sumei, monedei și sesiunii | `StripeWebhookController.Receive` |
| Ghicirea unui bilet | Token GUID aleatoriu (122 de biți), index unic; codul scurt e doar pentru personal autentificat | `Ticket.Token`, `TicketValidationService` |
| Poza QR-ului, folosită de două persoane | Bilet de unică folosință, marcat atomic | 8.3 |
| Parole furate din baza de date | Hash PBKDF2 cu sare (`PasswordHasher`) | `AuthController` |
| Acces la datele altcuiva | JWT + roluri pe fiecare endpoint; interogările filtrează după utilizatorul din token; 404 pentru resursa altcuiva | `[Authorize]`, `User.GetUserId()` în controllers |
| Un endpoint nou, uitat fără `[Authorize]` | Garda verifică automat că orice endpoint care modifică date cere login | `OriceEndpointCareModificaDate_CereLogin_CuExceptiaCelorPubliceIntentionat` |
| Secrete în repository | User Secrets local, GitHub Secrets în CI, `.env` ignorat de git | `.gitignore`, `.env.example` |
| Fișier malițios ca „poster” | Tip verificat după conținut, maximum 5 MB, nume generat de server | `ImageValidator`, `ImageStorage` |
| Injectare HTML în email | Textele utilizatorilor sunt escapate | `TicketEmailBuilder` |
| Biletul rămas în cache-ul browserului | `Cache-Control: private, no-store` pe imaginea QR | `OrderTicketsController` |
| Rambursare dublă | Ușa `Paid → RefundPending` + cheie de idempotență la Stripe | `OrderRefundService` |
| Spam cu retrimiterea emailului | Cel mult o dată la 2 minute pe comandă (429 + `Retry-After`) | `OrderEmailsController` |

**Ce nu e acoperit** (vezi capitolul 13): nu există limitare a încercărilor de login (brute force), tokenul JWT stă în `localStorage` (expus la un eventual atac XSS) și nu poate fi revocat înainte de expirare.

## 10. Testare

### 10.1 Strategia

| Nivel | Ce verifică | Cum |
|---|---|---|
| Unitare (backend) | Serviciile cu reguli de business, generarea QR, emailul, tokenul JWT | xUnit; Stripe și SMTP înlocuite prin implementări false ale `IPaymentGateway` / `IEmailSender` |
| Integrare cu baza de date | Tranzacții, `UPDATE`-uri condiționate, regulile `CHECK` și indexurile unice | **SQLite reală în memorie** (nu un simulator), cu schema din `AppDbContext` |
| Teste de cursă | Concurența: 30 de clienți pe 5 locuri, scanări simultane, rambursare și scanare simultane | `Task`-uri paralele, pe conexiuni separate |
| „Gărzi” pe codul sursă | Că regulile critice au **un singur** loc în cod și că endpoint-urile cer login | `CriticalRulesGuardTests` citește fișierele `.cs` și caută apelurile |
| Frontend | Componente și servicii: scannerul, citirea QR, comanda, panoul, gărzile de rol | Vitest, server HTTP simulat |
| Probe reale, cap-coadă | Tot fluxul, cu PostgreSQL, Stripe în mod test, smtp4dev | `docs/tools/demo-capturi.mjs`, într-un Chrome real (10.5) |

### 10.2 Cifrele măsurate

| Ce | Rezultat | Comanda |
|---|---|---|
| Teste backend | **300**, toate trec (0 picate, 0 sărite) | `dotnet test EventPass.slnx` |
| Teste frontend | **186**, toate trec, în 12 fișiere | `npm test -- --watch=false` (în `eventpass.client`) |
| Acoperire backend, linii | **79,4%** (1665 din 2095 de linii acoperibile) | `dotnet test --collect:"XPlat Code Coverage"` + ReportGenerator, fără `Migrations` |
| Acoperire backend, ramuri | **76,4%** (382 din 500) | idem |
| Acoperire backend, metode | **80,4%** (326 din 405) | idem |
| Regula „bilet unic” | **36** de teste | `dotnet test --filter Regula=BiletUnic` |
| Regula „stoc” | **21** de teste | `dotnet test --filter Regula=Stoc` |
| Regula „emitere doar din webhook” | **34** de teste | `dotnet test --filter Regula=EmitereWebhook` |

**Acoperirea pe clasele critice** (din același raport ReportGenerator):

| Clasă | Linii acoperite |
|---|---|
| `SeatReservationService` (stocul) | 100% |
| `TicketValidationService` (biletul unic) | 100% |
| `StripeWebhookController` | 100% |
| `TicketEmailService` | 100% |
| `OrdersController` | 95,7% |
| `ReservationExpiryService` | 94,5% |
| `OrderPaymentService` | 92,4% |
| `OrderRefundService` | 92,4% |

**Ce are 0% și de ce:** `StripePaymentGateway` (apelează Stripe pe internet), `SmtpEmailSender` (trimite prin SMTP), cele două procese de fundal (`ReservationExpiryWorker`, `TicketEmailWorker`, doar bucle care apelează serviciile testate) și `Program.cs` (configurarea). Ele sunt verificate de probele reale cap-coadă, nu de testele unitare; de aici vin cele ~20 de procente neacoperite.

### 10.3 Testele pe clase (backend)

Numărul de teste din fiecare clasă, numărat din rezultatele rulării (`.trx`):

| Clasă de teste | Teste | Clasă de teste | Teste |
|---|---|---|---|
| TicketValidationServiceTests | 34 | QrCodeGeneratorTests | 15 |
| OrdersControllerTests | 27 | TicketEmailBuilderTests | 13 |
| OrganizerDashboardTests | 26 | OrderPaymentServiceTests | 12 |
| OrderRefundTests | 24 | EventsControllerTests | 12 |
| TicketEmailServiceTests | 17 | CriticalRulesGuardTests | 12 |
| StripeWebhookControllerTests | 17 | OrganizerEventsControllerTests | 10 |
| TicketEmailResendTests | 14 | EventImageTests | 10 |
| ReservationExpiryServiceTests | 14 | celelalte 7 clase | 37 |

### 10.4 Performanța validării (CNF-07)

Pe laptop, cu API-ul și PostgreSQL în Docker, 50 de validări consecutive cu codul scurt (calea cea mai lentă: caută după începutul tokenului):

```
for i in $(seq 1 50); do curl -s -o /dev/null -w "%{time_total}\n" -X POST \
  localhost:5003/api/tickets/validate -H "Authorization: Bearer $T" -d '{"code":"0189D426"}' ...; done
→ minim 6,8 ms · mediana 9,0 ms · p95 13,5 ms · maxim 19,7 ms
```

Cerința de sub o secundă e îndeplinită cu o marjă mare **pe server**. Timpul real pe un telefon în Wi-Fi depinde de rețea și **nu a fost măsurat**.

### 10.5 Repetiția automată cap-coadă

`docs/tools/demo-capturi.mjs` conduce un Chrome real prin DevTools Protocol și parcurge exact scenariul demonstrației, pe o bază de date nouă. Pagina Stripe are formularul într-un iframe de pe alt domeniu, deci scriptul apasă la coordonate și scrie ca o tastatură (`Input.dispatchMouseEvent`, `Input.insertText`). Rularea care a produs capturile (15.11.2026, 215 s, **12 din 12 pași reușiți**):

| Pas | Rezultat |
|---|---|
| Trei cumpărături de fundal (alt client), plătite pe Stripe | comenzile #1–#3 plătite |
| Clientul cumpără 2 bilete, plătește | comanda #4 `Paid`; biletele pe pagină la 0,3 s după întoarcerea de pe Stripe |
| Emailul | primit în smtp4dev, cu 2 coduri QR |
| Retrimitere imediată | refuzată: „poți cere din nou peste 114 secunde” |
| Scanare, apoi a doua scanare | „Bilet valid”, apoi „Deja utilizat” |
| Rambursarea comenzii #2 din panou | biletele „Rambursat”, 500 lei rambursați |
| Scanarea unui bilet rambursat | „Respins” |
| Retrimitere după 2 minute | al doilea email ajuns |

O a doua rulare, cu site-ul pe HTTPS (cum se folosește cu telefonul), a găsit două probleme pe care testele automate nu le pot prinde: adresa de întoarcere de pe Stripe (rezolvată în scriptul de pornire al demonstrației) și cheia de idempotență a rambursării după refacerea bazei de date (capitolul 13). După corecturi, a treia rulare, tot pe HTTPS, a trecut de toți cei 12 pași (211 s).

### 10.6 Integrarea continuă

`.github/workflows/ci.yml` rulează la fiecare push și Pull Request către `main` trei job-uri: **backend** (restore, build Release, teste cu acoperire, raport ReportGenerator în pagina rulării și ca artefact descărcabil), **frontend** (`npm ci`, teste, build) și **validarea `docker-compose.yml`**. Istoric: **40 de rulări, 39 reușite, 1 eșuată** (`gh run list`). Eșecul a fost la raportul de acoperire, reparat în #22.

## 11. Managementul proiectului

### 11.1 Cum am lucrat

- **Issues:** 24, create la început (scriptul `create-issues.sh`), fiecare cu criterii de acceptare și etichete (`backend`, `frontend`, `payments`, `qr`, `email`, `testing`, `security`, `devops`, `docs`).
- **Milestones:** „Raport 1 – Propunere de proiect” (#1–#5), „Raport 2 – Proiectare și implementare minimală” (#6–#15), „Raport final” (#16–#24).
- **Ramuri:** una pe funcționalitate, `feature/<nume>`, **20 de ramuri** pe GitHub.
- **Pull Requests:** **20**, toate integrate (#25–#44). Fiecare (de la #26 încoace) a trecut prin CI înainte de integrare.
- **Commit-uri pe `main`:** **43** (23 de lucru + 20 de integrare a PR-urilor), între 20 octombrie și 15 noiembrie 2026 (`git rev-list --count HEAD`).
- **Mesajele de commit** trimit la issue: „Gestionarea concurenței la stoc (#18)”.

### 11.2 Trasabilitate: cerință → issue → PR → teste

Legătura issue → PR e cea înregistrată de GitHub (`closingIssuesReferences`, din `gh pr list`).

| Cerință | Issue | PR | Teste care o verifică |
|---|---|---|---|
| CF-01 Cont și JWT | #7 | #28 | `AuthControllerTests` (7), `TokenServiceTests` (3) |
| CF-02 Evenimente | #8 | #29 | `OrganizerEventsControllerTests` (10), `EventImageTests` (10) |
| CF-03 Tipuri de bilete | #8 | #29 | `OrganizerTicketTypesControllerTests` (9) |
| CF-04 Listă și filtre | #9 | #30 | `EventsControllerTests` (12) |
| CF-05 Rezervare 15 min | #10 | #31 | `OrdersControllerTests` (27), `StockRaceTests` (5) |
| CF-06 Stripe Checkout | #11 | #32 | `OrdersControllerTests` (checkout) |
| CF-07 Webhook semnat | #12 | #33 | `StripeWebhookControllerTests` (17), `OrderPaymentServiceTests` (12) |
| CF-08 Cod QR | #13 | #34 | `QrCodeGeneratorTests` (15), `OrderTicketsControllerTests` (9) |
| CF-09 Email | #14 | #35 | `TicketEmailServiceTests` (17), `TicketEmailBuilderTests` (13) |
| CF-10 Scanner | #16 | #36 | frontend: `scan.spec.ts`, `qr-reader.spec.ts`, `scan-loop.spec.ts`, `camera.service.spec.ts` |
| CF-11 Validare | #17 | #37 | `TicketValidationServiceTests` (34) |
| CF-12 Expirare | #18 | #38 | `ReservationExpiryServiceTests` (14), `StockRaceTests` |
| CF-13 Rambursare | #19 | #39 | `OrderRefundTests` (24) |
| CF-14 Panou | #20 | #40 | `OrganizerDashboardTests` (26), frontend `organizer.spec.ts` |
| CF-15 Retrimitere | #21 | #41 | `TicketEmailResendTests` (14) |
| CF-16 Roluri | #7 | #28 | `UsersControllerTests` (5) |
| CNF-09 Reguli critice | #22 | #42 | `CriticalRulesGuardTests` (12) + etichetele `Regula=…` |
| CNF-10 Portabilitate | #23 | #44 | job-ul CI „Docker Compose (validare)” |
| Documentație | #15, #24 | #43, acest PR | — |

**Goluri de trasabilitate, spuse cinstit:** #1–#3 au fost documente (Raportul 1), fără PR. PR-urile #25 și #26 (#4 și #5) nu conțin „Closes #…”, deci GitHub nu le-a legat automat de issue-uri; legătura e doar în titlul lor. PR-ul #25 nu are verificare CI, pentru că CI-ul a fost creat abia în #26. Tabloul **GitHub Projects** există, dar nu a putut fi citit automat pentru acest raport (tokenului `gh` îi lipsește permisiunea `read:project`).

## 12. Rulare și instalare

Pașii completi, cu Docker și fără, sunt în [README](../README.md). Pe scurt, cu Docker:

1. `npm install` în `eventpass.client`; `copy .env.example .env`, apoi o parolă în `.env`.
2. `docker compose up -d` (PostgreSQL pe 5433, smtp4dev pe 2525 și http://localhost:5025).
3. User Secrets: `ConnectionStrings:DefaultConnection`, `Jwt:Key`, `SeedAdmin:*`, `Stripe:SecretKey`, `Stripe:WebhookSecret`.
4. `dotnet ef database update --project EventPass.Server`.
5. `stripe listen --events checkout.session.completed,checkout.session.async_payment_succeeded --forward-to http://localhost:5003/api/webhooks/stripe`.
6. `dotnet run --project EventPass.Server --launch-profile http` și `npm start`, apoi http://localhost:4200.

Pentru demonstrație există scripturi care pregătesc totul pe o bază separată (`eventpass_demo`), fără să atingă datele de lucru: `docs/tools/demo-reset.ps1`, `demo-api.ps1`, `seed-demo.mjs`. Scenariul pas cu pas e în [docs/demo.md](demo.md).

## 13. Limitări și dezvoltări viitoare

### 13.1 Limitări (ce nu face aplicația)

- **Evenimentele se creează și se editează doar din Swagger** (API complet, testat), nu din site: interfața are pentru organizator doar panoul de vânzări (CF-02, CF-03).
- **Fără atribuirea personalului pe evenimente** (RB-08): orice Staff poate valida bilete la orice eveniment. Un Organizer validează doar la evenimentele lui.
- **Cine a scanat** nu se salvează în baza de date (RB-03), doar în jurnalul serverului.
- **Adminul nu poate ascunde un eveniment** (CF-16 parțial); doar organizatorul îl poate retrage.
- **Filtrul după dată** lipsește (CF-04 parțial): evenimentele sunt doar ordonate după dată, iar cele trecute sunt ascunse.
- **Fără PDF atașat la email:** biletul e în corpul emailului (QR ca imagine) și în cont. Planul inițial menționa un bilet PDF.
- **Fără deploy în producție și fără plăți reale:** totul rulează local, cu Stripe în mod test.
- **Securitate:** fără limitarea încercărilor de login, JWT în `localStorage`, fără revocarea tokenului.
- **Cheia de idempotență a rambursării** e făcută doar din numărul comenzii (`eventpass-refund-order-<nr>`). Repetiția demonstrației a arătat că, dacă baza de date e refăcută de la zero în mai puțin de 24 de ore, o comandă nouă cu același număr primește cheia unei rambursări vechi și Stripe o refuză (comanda rămâne „Rambursare în curs”). Într-o bază care nu se reface nu apare; pentru demo, scriptul de reset ocolește problema. Corectura: cheia să conțină și id-ul sesiunii Stripe.
- **Codul scurt** se caută citind toate biletele (fără index); e suficient pentru câteva mii de bilete, nu pentru milioane.
- **Performanța pe telefon prin Wi-Fi** (CNF-07) nu a fost măsurată; doar cea a serverului.

### 13.2 Dezvoltări viitoare

1. Pagini în Angular pentru crearea și editarea evenimentelor și a tipurilor de bilete.
2. Tabel `StaffAssignment` (Raport 1, 7.1) și coloana `ValidatedBy` pe bilet.
3. Deploy: containere pentru API și site, HTTPS real, webhook Stripe configurat în contul Stripe (nu prin `stripe listen`), secrete într-un seif (Azure Key Vault / variabile de mediu ale platformei).
4. Limitarea cererilor la login (`AddRateLimiter` din ASP.NET Core) și tokenuri de reîmprospătare.
5. Bilet PDF atașat la email și adăugarea în Apple / Google Wallet.
6. Un mod de scanare fără internet la intrare (lista biletelor descărcată dinainte), cu sincronizare ulterioară.

## 14. Concluzii

EventPass își atinge obiectivul din Raportul 1: un prototip complet, demonstrabil în mod test, cu fluxul „publicare → cumpărare → email → scanare”, plus rambursările și panoul organizatorului. Din cele 16 cerințe funcționale, 13 sunt realizate complet și 3 parțial; abaterile sunt documentate mai sus.

**Ce am învățat:**

- **Corectitudinea vine din baza de date, nu din `if`-uri.** Aproape toate problemele grele (stocul, biletul unic, plata dublă, rambursarea) s-au rezolvat cu același tipar: o singură comandă `UPDATE … WHERE <condiția>`, plus o regulă în baza de date ca plasă de siguranță.
- **Nu ai încredere în client.** Prețul, plata și validitatea biletului le decide serverul; browserul doar afișează.
- **Sistemele externe pică.** Webhook-ul se poate pierde, Stripe poate să nu răspundă, SMTP-ul poate fi căzut: fiecare caz are un comportament gândit (reîncercare, amânare, verificare) și un test.
- **Testele de concurență se pot scrie**, dacă baza de date din teste e una reală. Fără SQLite real, testele de cursă ar fi trecut și pe codul greșit.
- **Trasabilitatea ajută și la scris:** fiecare issue cu criterii de acceptare a devenit o listă de verificat, iar fiecare PR cu CI verde a însemnat că nimic vechi nu s-a stricat.

Ce aș face diferit: paginile de administrare a evenimentelor ar fi meritat prioritate înaintea unor detalii din panou, iar `Closes #…` ar fi trebuit folosit de la primul PR.

## 15. Bibliografie

Documentația oficială folosită în proiect (consultată în octombrie–noiembrie 2026):

1. Stripe — Checkout: https://docs.stripe.com/payments/checkout
2. Stripe — Webhooks și verificarea semnăturii: https://docs.stripe.com/webhooks
3. Stripe — Cereri idempotente: https://docs.stripe.com/api/idempotent_requests
4. Stripe — Rambursări: https://docs.stripe.com/refunds
5. Stripe — Carduri de test: https://docs.stripe.com/testing
6. Stripe CLI — `stripe listen`: https://docs.stripe.com/stripe-cli
7. Microsoft — ASP.NET Core Web API: https://learn.microsoft.com/aspnet/core/web-api/
8. Microsoft — Autentificare JWT Bearer în ASP.NET Core: https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication
9. Microsoft — Safe storage of app secrets (User Secrets): https://learn.microsoft.com/aspnet/core/security/app-secrets
10. Microsoft — Background tasks with hosted services: https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services
11. Microsoft — EF Core: ExecuteUpdate și ExecuteDelete: https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete
12. Microsoft — EF Core: migrări: https://learn.microsoft.com/ef/core/managing-schemas/migrations/
13. Microsoft — OpenAPI în ASP.NET Core: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview
14. Angular — Documentație: https://angular.dev
15. PostgreSQL 18 — Constraints (CHECK, UNIQUE): https://www.postgresql.org/docs/18/ddl-constraints.html
16. PostgreSQL 18 — Concurrency control: https://www.postgresql.org/docs/18/mvcc.html
17. QRCoder: https://github.com/Shane32/QRCoder
18. jsQR: https://github.com/cozmo/jsQR
19. MailKit: https://github.com/jstedfast/MailKit
20. smtp4dev: https://github.com/rnwood/smtp4dev
21. xUnit: https://xunit.net · Vitest: https://vitest.dev
22. GitHub Actions: https://docs.github.com/actions
23. Docker Compose: https://docs.docker.com/compose/

## 16. Anexe

### Anexa A. API-ul complet

<!-- API:START -->
Generat automat din `/openapi/v1.json` (EventPass.Server | v1, 33 endpoint-uri) cu `docs/tools/api-tabel.mjs`; coloana „Acces” vine din atributele `[Authorize]` ale controllerelor.

| Grup | Metodă | Rută | Acces | Primește | Întoarce |
|---|---|---|---|---|---|
| Auth | POST | `/api/auth/register` | public | RegisterRequest | AuthResponse |
| Auth | POST | `/api/auth/login` | public | LoginRequest | AuthResponse |
| Auth | GET | `/api/auth/me` | logat | — | UserDto |
| Events | GET | `/api/events` | public | — | listă de PublicEventCardDto |
| Events | GET | `/api/events/filters` | public | — | EventFiltersDto |
| Events | GET | `/api/events/{id}` | public | — | PublicEventDetailDto |
| ManageOrders | POST | `/api/manage/orders/{id}/refund` | Organizer, Admin | — | — |
| OrderEmails | POST | `/api/orders/{orderId}/resend-tickets` | logat | — | — |
| Orders | POST | `/api/orders` | logat | CreateOrderRequest | OrderDto |
| Orders | GET | `/api/orders/mine` | logat | — | listă de OrderDto |
| Orders | GET | `/api/orders/{id}` | logat | — | OrderDto |
| Orders | POST | `/api/orders/{id}/cancel` | logat | — | OrderDto |
| Orders | POST | `/api/orders/{id}/refund` | logat | — | OrderDto |
| Orders | POST | `/api/orders/{id}/checkout` | logat | — | CheckoutResponse |
| OrderTickets | GET | `/api/orders/{orderId}/tickets/{ticketId}/qr` | logat | — | imagine PNG |
| OrganizerDashboard | GET | `/api/organizer/dashboard` | Organizer | — | listă de DashboardEventDto |
| OrganizerDashboard | GET | `/api/organizer/events/{id}/sales` | Organizer | — | EventSalesDto |
| OrganizerDashboard | GET | `/api/organizer/events/{id}/attendees` | Organizer | — | AttendeesPageDto |
| OrganizerEvents | GET | `/api/organizer/events` | Organizer | — | listă de EventSummaryDto |
| OrganizerEvents | POST | `/api/organizer/events` | Organizer | SaveEventRequest | EventDetailDto |
| OrganizerEvents | GET | `/api/organizer/events/{id}` | Organizer | — | EventDetailDto |
| OrganizerEvents | PUT | `/api/organizer/events/{id}` | Organizer | SaveEventRequest | EventDetailDto |
| OrganizerEvents | DELETE | `/api/organizer/events/{id}` | Organizer | — | — |
| OrganizerEvents | POST | `/api/organizer/events/{id}/image` | Organizer | fișier (form-data) | EventDetailDto |
| OrganizerEvents | DELETE | `/api/organizer/events/{id}/image` | Organizer | — | EventDetailDto |
| OrganizerEvents | POST | `/api/organizer/events/{id}/publish` | Organizer | — | EventDetailDto |
| OrganizerEvents | POST | `/api/organizer/events/{id}/unpublish` | Organizer | — | EventDetailDto |
| OrganizerTicketTypes | POST | `/api/organizer/events/{eventId}/ticket-types` | Organizer | SaveTicketTypeRequest | TicketTypeDto |
| OrganizerTicketTypes | PUT | `/api/organizer/events/{eventId}/ticket-types/{id}` | Organizer | SaveTicketTypeRequest | TicketTypeDto |
| OrganizerTicketTypes | DELETE | `/api/organizer/events/{eventId}/ticket-types/{id}` | Organizer | — | — |
| StripeWebhook | POST | `/api/webhooks/stripe` | semnătura Stripe | — | — |
| Tickets | POST | `/api/tickets/validate` | Staff, Organizer, Admin | ValidateTicketRequest | TicketValidationDto |
| Users | PUT | `/api/users/{id}/role` | Admin | ChangeRoleRequest | UserDto |
<!-- API:END -->

### Anexa B. Glosar

| Termen | Explicație |
|---|---|
| API REST | Interfața prin care frontend-ul vorbește cu serverul: adrese (rute) și metode HTTP (GET, POST, PUT, DELETE), cu date JSON |
| Atomic | O operație care se face complet sau deloc; nimeni nu poate „prinde” o stare intermediară |
| Cheie de idempotență | Un identificator trimis cu o cerere; dacă cererea se repetă cu aceeași cheie, Stripe o execută o singură dată |
| `CHECK` | O regulă scrisă în baza de date, verificată la fiecare scriere (ex. `Sold <= Capacity`) |
| Cod scurt | Primele 8 caractere ale tokenului, tipărite sub QR, pentru introducere manuală |
| CI (integrare continuă) | Rularea automată a build-ului și a testelor la fiecare modificare (GitHub Actions) |
| DTO | Obiect care descrie exact ce intră sau iese prin API, separat de entitățile din baza de date |
| EF Core / migrare | Biblioteca prin care C# lucrează cu baza de date; o migrare e o modificare versionată a schemei |
| Gardă (test) | Un test care citește codul sursă și verifică o regulă de structură (ex. „biletele se creează într-un singur loc”) |
| Guard (Angular) | Funcție care decide dacă o pagină poate fi deschisă (ex. doar pentru Staff) |
| HMAC | Semnătură calculată cu un secret comun; dovedește că mesajul vine de la cine are secretul și nu a fost modificat |
| Idempotent | O operație care, repetată, are același efect ca făcută o dată |
| Index unic | Regulă a bazei de date: o valoare (ex. tokenul) nu poate apărea de două ori |
| JWT | JSON Web Token: un text semnat de server, care spune cine ești și ce rol ai |
| Mod test (Stripe) | Mediu Stripe în care plățile sunt simulate, cu carduri de test (`4242 4242 4242 4242`) |
| PBKDF2 | Algoritm de hash pentru parole, lent intenționat, cu „sare” aleatoare |
| Proces de fundal | Cod care rulează în server independent de cereri (ex. la fiecare 30 de secunde) |
| Sesiune Stripe Checkout | Pagina de plată creată de Stripe pentru o anumită comandă și sumă |
| smtp4dev | Server de email de test: prinde emailurile și le arată într-o pagină web, fără să le trimită |
| Swagger / OpenAPI | Descrierea automată a API-ului și pagina din care poate fi încercat |
| Token (bilet) | GUID aleatoriu, unic, conținutul codului QR |
| User Secrets | Fișier cu secrete pe calculatorul dezvoltatorului, în afara repository-ului |
| Webhook | Cerere HTTP trimisă de Stripe către serverul nostru când se întâmplă ceva (ex. plata a reușit) |
