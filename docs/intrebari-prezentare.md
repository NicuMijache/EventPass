# Întrebări posibile la prezentare, cu răspunsuri

Răspunsuri scurte (2–4 propoziții), de spus din memorie, plus locul din cod unde se vede. Întrebările sunt ordonate de la ușoare la grele.

---

## Ușoare

**1. Ce face aplicația, într-o frază?**
Vinde bilete la evenimente: clientul plătește online prin Stripe, primește un cod QR pe email, iar la intrare personalul scanează codul cu telefonul și serverul îl acceptă o singură dată.
*Unde:* `README.md`, fluxul din `docs/raport-final.md` cap. 7.

**2. Ce tehnologii ați folosit și de ce?**
.NET 10 cu EF Core pentru API, Angular 22 pentru site, PostgreSQL pentru date, Stripe Checkout pentru plăți. PostgreSQL pentru că regulile critice se bazează pe tranzacții, pe UPDATE-uri atomice și pe constrângeri `CHECK`; Stripe Checkout ca să nu atingem niciodată datele cardului.
*Unde:* `EventPass.Server.csproj`, `eventpass.client/package.json`, raport cap. 5.

**3. Ce roluri există și ce poate face fiecare?**
Client (cumpără, își vede biletele, cere rambursarea), Organizer (evenimentele lui, panoul de vânzări, rambursări), Staff (scanează) și Admin (schimbă roluri, rambursează orice). Contul nou e Client; rolurile le schimbă Adminul.
*Unde:* `Models/User.cs` (`UserRole`), `[Authorize(Roles = …)]` pe controllere, `UsersController.ChangeRole`.

**4. Ce conține codul QR?**
Doar tokenul biletului, un GUID aleatoriu, scris cu majuscule ca să încapă în modul alfanumeric al QR-ului. Nu conține nume, preț sau alte date: validitatea o decide mereu baza de date.
*Unde:* `Services/Qr/QrCodeGenerator.cs`, `Models/Ticket.cs`.

**5. Unde sunt datele cardului?**
Doar la Stripe. Clientul le scrie pe pagina Stripe Checkout, găzduită de Stripe; serverul nostru primește doar confirmarea plății. În baza noastră nu există niciun câmp pentru card.
*Unde:* `StripePaymentGateway.CreateCheckoutSessionAsync`, `Models/Order.cs`.

**6. Cum rulez proiectul pe alt calculator?**
`docker compose up -d` pornește PostgreSQL și serverul de email de test; apoi secretele în User Secrets, `dotnet ef database update`, `stripe listen`, API-ul și `npm start`. Pașii exacți sunt în README.
*Unde:* `README.md` („Rulare”), `docker-compose.yml`.

**7. Ce e Swagger și la ce v-a folosit?**
E pagina generată automat din descrierea OpenAPI a API-ului, din care pot încerca orice endpoint, inclusiv cu tokenul JWT. Am folosit-o pentru teste manuale și pentru crearea evenimentelor, care nu au încă pagini în site.
*Unde:* `Program.cs` (`MapOpenApi`, `UseSwaggerUI`), http://localhost:5003/swagger.

## Medii

**8. De ce webhook și nu pagina de întoarcere de la Stripe?**
Pagina de întoarcere e doar o adresă în browser, pe care oricine o poate scrie de mână, deci nu dovedește nimic. Webhook-ul e un mesaj trimis de Stripe direct serverului, semnat cu un secret comun; îl verific și abia apoi comanda devine plătită. În plus, ajunge chiar dacă clientul închide pagina după plată.
*Unde:* `Controllers/StripeWebhookController.cs`, garda `ClientulIntorsDePeStripe_NuPrimesteBileteFaraConfirmareaStripe`.

**9. Cum verificați că webhook-ul vine chiar de la Stripe?**
Cu `EventUtility.ConstructEvent`: calculează semnătura HMAC-SHA256 a textului exact primit, cu secretul `whsec_…`, și o compară cu antetul `Stripe-Signature`; respinge și mesajele mai vechi de 5 minute. Apoi verific suma, moneda și id-ul sesiunii față de comandă.
*Unde:* `StripeWebhookController.Receive`, `OrderPaymentService.ConfirmAsync`; testele `Receive_…_Returneaza400`.

**10. Ce înseamnă idempotență?**
O operație e idempotentă dacă, repetată, are același efect ca făcută o dată. La noi: dacă Stripe trimite același webhook de trei ori, biletele se emit o singură dată, iar o rambursare reîncercată întoarce banii o singură dată.
*Unde:* `OrderPaymentService` (`UPDATE … WHERE Status = Pending`), `OrderRefundService` (cheia de idempotență); testul `Receive_AcelasiMesajDeDouaOri_NuDuplicaBiletele`.

**11. Cum împiedicați vânzarea aceluiași loc de două ori?**
Verificarea și scăderea stocului sunt o singură comandă: `UPDATE … SET Sold = Sold + n WHERE Sold + n <= Capacity`. Baza de date blochează rândul, deci cererile simultane se execută pe rând; dacă se modifică 0 rânduri, s-au epuizat. Ca plasă de siguranță, `CHECK (Sold <= Capacity)` în baza de date.
*Unde:* `Services/SeatReservationService.cs`, `AppDbContext` (`CK_TicketType_Sold`); testul `TreizeciDeClientiSimultan_PentruCinciLocuri_DoarCinciPrimescBilete`.

**12. Dacă cineva face o poză QR-ului, ce se întâmplă?**
Poza conține același token, deci prima persoană scanată intră, iar orice altă scanare primește „Deja utilizat”, cu ora primei scanări. Marcarea e atomică, deci nici două telefoane în aceeași secundă nu lasă să intre doi oameni.
*Unde:* `TicketValidationService.ValidateAsync`; testul `DouaScanariSimultane_DoarUnaEsteValida`.

**13. De ce JWT?**
Tokenul semnat conține id-ul și rolul, deci serverul nu ține sesiuni și nu interoghează baza la fiecare cerere. Merge la fel din site, de pe telefon și din Swagger. Dezavantajul: nu se poate revoca înainte să expire, de aceea durează doar 60 de minute.
*Unde:* `Services/TokenService.cs`, `Program.cs` (`AddJwtBearer`), `appsettings.json` (`ExpiresMinutes`).

**14. Cum sunt salvate parolele?**
Doar ca hash, cu `PasswordHasher` din ASP.NET Core Identity (PBKDF2 cu sare aleatoare și multe iterații). La login se compară hash-ul, parola nu e stocată și nu poate fi citită nici de noi.
*Unde:* `Controllers/AuthController.cs`, `Data/AdminSeeder.cs`.

**15. De ce trimiteți emailul dintr-un proces de fundal?**
Stripe așteaptă un răspuns rapid la webhook, iar un server de email căzut nu trebuie să anuleze o plată reușită. Webhook-ul doar marchează comanda și „trezește” procesul; starea emailului e în baza de date, deci nu se pierde la repornire, iar eșecurile se reîncearcă după 2, 4, 8… minute.
*Unde:* `Services/Email/TicketEmailWorker.cs`, `TicketEmailService.cs` (`BackoffAfter`, `MaxAttempts = 6`).

**16. Ce se întâmplă cu o rezervare pe care clientul n-o plătește?**
Locurile sunt ținute 15 minute. Un proces de fundal rulează la 30 de secunde și anulează comenzile expirate, punând locurile înapoi o singură dată. Dacă clientul a ajuns pe pagina de plată, termenul e cât e valabilă sesiunea Stripe (31 de minute).
*Unde:* `Services/ReservationExpiryWorker.cs`, `ReservationExpiryService.cs`.

**17. Cum ați testat?**
300 de teste xUnit pe backend și 186 Vitest pe frontend, toate rulate în CI la fiecare Pull Request. Testele backend folosesc o bază SQLite reală, ca tranzacțiile și constrângerile să fie adevărate; testele de cursă pornesc cereri în paralel. Plus o repetiție automată cap-coadă, într-un Chrome real, cu Stripe în mod test.
*Unde:* `EventPass.Tests/`, `SqliteTestDatabase.cs`, `StockRaceTests.cs`, `docs/tools/demo-capturi.mjs`, `.github/workflows/ci.yml`.

**18. Ce sunt „gărzile” din teste?**
Teste care citesc codul sursă și verifică reguli de structură: biletele se creează într-un singur loc, comanda devine plătită într-un singur loc, stocul se modifică doar din serviciul de rezervări, iar orice endpoint care modifică date cere login. Dacă cineva ocolește regula, testul pică.
*Unde:* `EventPass.Tests/CriticalRulesGuardTests.cs`.

**19. Ce acoperire aveți și ce nu e acoperit?**
79,4% din liniile backend-ului, măsurat cu coverlet și ReportGenerator, fără migrări. Serviciile critice au 92–100%. Neacoperite sunt clasele care vorbesc cu Stripe și SMTP și cele două procese de fundal; ele sunt verificate de repetiția cap-coadă.
*Unde:* job-ul „Coverage report” din `ci.yml`, raport cap. 10.2.

**20. Cum ați organizat lucrul?**
Fiecare cerință are un issue în GitHub, grupat într-un milestone pe raport. Fiecare issue s-a lucrat pe o ramură `feature/…`, integrată printr-un Pull Request cu „Closes #…” și CI verde. În total 24 de issue-uri, 20 de PR-uri, 43 de commit-uri pe `main`.
*Unde:* GitHub Issues / Pull Requests, raport cap. 11.

## Grele

**21. Ce se întâmplă dacă webhook-ul nu ajunge niciodată?**
Clientul a plătit, dar comanda e încă „Pending”. Procesul de fundal, înainte să anuleze o rezervare expirată, întreabă Stripe despre sesiunea ei: dacă e plătită, emite biletele, ca și cum ar fi venit webhook-ul. Dacă Stripe nu răspunde, amână; nu anulează niciodată „pe ghicite”. Dacă webhook-ul vine totuși mai târziu, nu mai emite bilete în plus.
*Unde:* `ReservationExpiryService`; testele `PlatitaLaStripeDarFaraWebhook_NuSeAnuleaza_CiSeEmitBiletele`, `PlataRecuperata_WebhookulCareVineDupa_NuMaiEmiteBileteInPlus`.

**22. Ce se întâmplă dacă organizatorul rambursează exact când clientul e scanat?**
Rambursarea anulează biletele nefolosite într-o tranzacție, cu un UPDATE care blochează rândurile; scanarea are condiția „nefolosit și neanulat” în WHERE. Baza de date le execută pe rând: ori scanarea câștigă și rambursarea se anulează („un bilet a fost folosit”), ori rambursarea câștigă și scanarea e respinsă. Niciodată amândouă.
*Unde:* `OrderRefundService.StartAsync`, `TicketValidationService`; testul `ScanareSiRambursareInAcelasiTimp_NiciodataAmandoua`.

**23. Ce se întâmplă dacă plata vine după ce rezervarea a expirat și locurile s-au eliberat?**
Comanda e deja „Cancelled”, deci webhook-ul nu emite bilete. Ca să nu păstrăm banii pentru nimic, serverul cere automat rambursarea la Stripe. În plus, anularea unei rezervări închide întâi sesiunea Stripe, ca nimeni să nu mai poată plăti după eliberarea locurilor.
*Unde:* testele `Receive_PlataPentruOComandaAnulata_DaBaniiInapoiAutomat`, `Anulare_CuPaginaDePlataDeschisa_OInchideIntaiLaStripe`.

**24. Testele de concurență pe SQLite garantează același lucru pe PostgreSQL?**
Nu identic: SQLite blochează toată baza la scriere, PostgreSQL blochează pe rând. Dar regula pe care mă bazez, un UPDATE cu condiția în WHERE executat atomic, e valabilă în ambele, iar constrângerile `CHECK` și indexurile unice sunt aceleași (create din același model EF). Pe PostgreSQL am verificat fluxul complet în repetiția cap-coadă, dar nu am rulat testele de cursă pe PostgreSQL; ar fi primul test pe care l-aș adăuga.
*Unde:* `SqliteTestDatabase.cs`, `StockRaceTests.cs`.

**25. Ce ați testa în plus?**
Testele de cursă rulate și pe PostgreSQL (cu Testcontainers), un test de încărcare pe validarea biletelor (sute de scanări pe secundă), teste end-to-end în CI cu Playwright și Stripe în mod test, plus o măsurare reală a timpului de răspuns pe telefon, în Wi-Fi.
*Unde:* raport cap. 10 și 13.

**26. Ce ați face diferit?**
Aș face paginile de administrare a evenimentelor înaintea unor detalii din panou, aș folosi „Closes #…” de la primul PR și aș construi cheia de idempotență a rambursării din sesiunea Stripe, nu din numărul comenzii: repetiția demonstrației a arătat că, după refacerea bazei, numerele se repetă și Stripe refuză cheia.
*Unde:* `OrderRefundService.RefundKey`, raport cap. 13–14.

**27. Ce bug ați găsit cel mai târziu și cum?**
La repetiția automată a demonstrației, pe o bază refăcută: cheia de idempotență a rambursării, `eventpass-refund-order-2`, fusese folosită cu o oră înainte pentru altă plată, iar Stripe a refuzat-o. Testele unitare nu-l puteau prinde, pentru că Stripe e înlocuit în teste. L-am ocolit în scriptul demonstrației, iar corectura în cod e documentată ca pas următor.
*Unde:* `docs/demo.md` secțiunea 7, `docs/tools/demo-reset.ps1`.

**28. Cum ați pune aplicația în producție?**
API-ul și site-ul în containere, pe un serviciu cloud cu HTTPS (de exemplu Azure App Service sau un VPS cu reverse proxy), PostgreSQL administrat, secretele ca variabile de mediu sau într-un seif de chei. La Stripe: cheile live și un endpoint de webhook configurat în contul Stripe, nu prin `stripe listen`; email prin un furnizor real (SendGrid, SES). Migrările rulate la deploy, din CI. Înainte de asta: limitarea încercărilor de login și paginile de administrare.
*Unde:* raport cap. 13.2.

**29. Ce se întâmplă dacă baza de date cade în timpul unei plăți?**
Webhook-ul nu poate salva și răspunde cu eroare (500), deci Stripe îl retrimite automat mai târziu (în mod live, timp de până la 3 zile). Când baza revine, primul mesaj reușit emite biletele, iar celelalte sunt ignorate datorită idempotenței. Iar procesul de fundal verifică oricum plățile rămase „Pending” la Stripe.
*Unde:* `StripeWebhookController` (500 → Stripe reîncearcă), `ReservationExpiryService`.

**30. De ce 404 și nu 403 pentru comanda altcuiva?**
Un 403 ar confirma că acea comandă există, deci cineva ar putea „număra” comenzile încercând id-uri la rând. Cu 404 serverul nu dezvăluie nimic: comanda altcuiva e tratată ca inexistentă. Interogarea filtrează direct după utilizatorul din token.
*Unde:* `OrdersController.Get`, testul `Client_ComandaAltcuiva_Returneaza404`.
