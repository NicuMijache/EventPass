# EventPass — scenariul demonstrației

Demonstrația durează **5–7 minute** și arată tot drumul unui bilet: client cumpără → plătește pe Stripe → primește QR pe email → biletul e scanat cu telefonul (valid) → scanat din nou (deja utilizat) → panoul organizatorului se actualizează → o rambursare → biletul rambursat e respins.

Demo-ul folosește o **bază de date separată**, `eventpass_demo` (în containerul Docker), pe care o poți reface oricând. Baza ta de lucru nu e atinsă.

---

## 1. Cu o zi înainte

- [ ] `git pull` pe `main`, apoi `npm install` în `eventpass.client` și `npm install` în `docs/tools`.
- [ ] Docker Desktop pornește și arată „Engine running”.
- [ ] `stripe login` e valabil: `stripe --project-name default listen --print-secret` afișează un `whsec_...`.
- [ ] Telefonul și laptopul sunt pe **aceeași rețea Wi-Fi**. La facultate, verifică dacă rețeaua permite conexiuni între dispozitive; dacă nu, folosește hotspot-ul telefonului pentru laptop.
- [ ] O repetiție completă: secțiunea 6 (automat) și o dată de mână, cu telefonul.

## 2. Pregătirea (cu 15 minute înainte)

Toate comenzile se dau din rădăcina repository-ului (`EventPass`), în PowerShell. Fiecare proces de lungă durată stă în **fereastra lui** de terminal.

**Fereastra 1, baza de date curată** (reface `eventpass_demo` de la zero):

```
powershell -ExecutionPolicy Bypass -File docs/tools/demo-reset.ps1
```

Trebuie să vezi la final `Done.`, „Comenzile noi vor avea numere de la #… în sus” și „Gata”. Dacă apare `failed to connect to the docker API`, Docker Desktop nu e pornit.

> De ce numere de comandă mari (ex. #422880)? Rambursarea trimite la Stripe o cheie de idempotență făcută din numărul comenzii, iar Stripe ține minte cheile 24 de ore. Dacă după un reset comenzile ar reîncepe de la #1, o rambursare din demo ar folosi cheia unei rambursări din repetiție și Stripe ar refuza-o. Scriptul pornește deci numerotarea de la o valoare nouă la fiecare reset (vezi secțiunea 7).

**Fereastra 2, webhook-urile Stripe** (lasă fereastra deschisă tot timpul):

```
stripe --project-name default listen --events checkout.session.completed,checkout.session.async_payment_succeeded --forward-to http://localhost:5003/api/webhooks/stripe
```

Trebuie să apară `Ready! ... Your webhook signing secret is whsec_...`.

**Fereastra 3, API-ul pentru demo** (`-Telefon` = site-ul rulează pe HTTPS, cu `npm run start:phone`):

```
powershell -ExecutionPolicy Bypass -File docs/tools/demo-api.ps1 -Telefon
```

Fără `-Telefon`, Stripe ar trimite clientul înapoi pe `http://localhost:4200`, care nu răspunde când site-ul rulează pe HTTPS. Scriptul citește secretul webhook-ului **chiar din Stripe CLI**, deci e sigur identic cu cel din fereastra 2. Asta evită capcana „plata trece, dar comanda rămâne Pending” (webhook respins cu 400). Așteaptă linia `Now listening on: http://localhost:5003`.

**Fereastra 4, datele de demo** (conturi, 7 evenimente cu postere):

```
node docs/tools/seed-demo.mjs
```

**Fereastra 5, site-ul, accesibil și de pe telefon** (HTTPS în rețeaua locală):

```
cd eventpass.client
npm run start:phone
```

**Verificări rapide:**

| Ce | Unde | Trebuie să vezi |
|---|---|---|
| Containerele | `docker compose ps` | `db` „healthy”, `smtp` „Up” |
| Site-ul | https://localhost:4200 (acceptă certificatul de test) | 7 evenimente în 4 orașe |
| Emailurile | http://localhost:5025 | interfața smtp4dev |
| Swagger | http://localhost:5003/swagger | lista endpoint-urilor |
| Telefonul | `ipconfig` → IPv4 Wi-Fi (ex. `192.168.0.127`), pe telefon `https://<IP>:4200/scaneaza` | pagina de login, apoi scannerul |

> **Atenție:** dacă ai pornit vreodată `smtp4dev` local (`dotnet tool run smtp4dev`), oprește-l: ocupă porturile 2525 și 5025 ale containerului.

## 3. Conturile de demo

Toate au parola **`Demo2026!`** (doar pentru demo-ul local; Stripe în mod test).

| Rol | Email | Unde îl folosești |
|---|---|---|
| Client | `client@eventpass.demo` (Maria Ionescu) | fereastra **Incognito** a browserului |
| Organizer | `organizator@eventpass.demo` (Andrei Popescu) | fereastra **normală** a browserului |
| Staff | `staff@eventpass.demo` (Ioana Marin) | **telefonul** |
| Admin | `admin@eventpass.demo` | Swagger, dacă te întreabă cineva de roluri |
| Client 2 | `client2@eventpass.demo` (Radu Stan) | rezervă: o comandă de rambursat (vezi pasul 0) |

Cardul de test Stripe: **`4242 4242 4242 4242`**, orice dată din viitor (ex. `12/30`), orice CVC (ex. `123`), orice nume.

## 4. Așezarea ecranului

- **Fereastra normală**, logat ca **organizator**, pe **Panou** → „Nocturne Jazz Session”.
- **Fereastra Incognito**, logat ca **client**, pe pagina principală.
- **Tab-ul smtp4dev** (http://localhost:5025) deschis lângă.
- **Telefonul**, logat ca **staff**, pe `/scaneaza`, cu permisiunea pentru cameră deja dată. Pune luminozitatea ecranului laptopului la maximum: codul QR se citește mai ușor.
- **Pasul 0, înainte să înceapă prezentarea:** din Incognito, logat ca `client2@eventpass.demo`, cumpără 2 × VIP la „Nocturne Jazz Session” (plătește cu cardul de test). Asta e comanda pe care o vei rambursa live, ca să nu aștepți după o plată în plus. Apoi deloghează-te și intră ca `client@eventpass.demo`.

## 5. Scenariul, minut cu minut

| Timp | Ce apeși | Ce spui | Ce trebuie să apară |
|---|---|---|---|
| 0:00 | Incognito: pagina principală, filtrul „Cluj-Napoca”, apoi „Nocturne Jazz Session” | „Clientul vede evenimentele publicate, cu filtre. Prețul și locurile libere vin de la server.” | lista filtrată; pagina evenimentului cu Standard și VIP |
| 0:30 | „+” de două ori la Standard → **Rezervă biletele** | „Locurile se scad atomic, cu un singur UPDATE. Sunt ținute 15 minute.” | „Biletele sunt rezervate” și numărătoarea inversă |
| 1:00 | **Plătește 240 lei** → pe Stripe: Card, `4242…`, `12/30`, `123`, nume → **Plătiți** | „Cardul îl introduc pe pagina Stripe; serverul nostru nu-l vede niciodată.” | pagina Stripe (Sandbox), apoi înapoi pe site |
| 1:45 | (nimic, așteaptă 1–2 s) | „Întoarcerea pe site nu e dovada plății. Biletele apar abia când Stripe trimite webhook-ul semnat.” Arată în fereastra 2 linia `checkout.session.completed ... [200]`. | „Comanda e plătită”, 2 bilete cu QR și cod scurt |
| 2:15 | Tab-ul smtp4dev → mesajul „Biletele tale: Nocturne Jazz Session” | „Emailul l-a trimis un proces de fundal; dacă SMTP-ul pica, reîncerca.” | emailul cu 2 coduri QR |
| 2:45 | Telefonul: **Pornește camera**, îndreaptă-l spre QR-ul primului bilet (pe ecranul laptopului) | „Personalul scanează din browserul telefonului, fără aplicație.” | **verde, „✓ Bilet valid”**, cu numele Mariei Ionescu |
| 3:15 | Scanează **același** QR încă o dată | „A doua scanare e respinsă, chiar și în aceeași secundă, de pe alt telefon: un singur UPDATE cu condiția «nefolosit».” | **galben, „Deja utilizat”**, cu ora primei scanări |
| 3:45 | Fereastra normală (organizator): **Actualizează** | „Organizatorul vede vânzările, încasările și cine a intrat.” | „Au intrat: 1”; în listă, Maria: „A intrat …” |
| 4:15 | În lista participanților, la comanda lui Radu Stan (VIP): **Rambursează** → **Da** | „Biletele se anulează în aceeași tranzacție; banii se cer la Stripe cu o cheie de idempotență.” | mesajul „Comanda #… a fost rambursată”; biletele: „Rambursat” |
| 4:45 | Telefonul: în **Cod de mână**, scrie codul scurt al unui bilet VIP al lui Radu (din lista participanților) → **Folosește codul** | „Un bilet rambursat nu mai deschide ușa, chiar dacă cineva are încă poza QR-ului.” | **roșu, „✕ Respins”**: „Bilet anulat: comanda a fost rambursată” |
| 5:15 | (opțional) Incognito: pe comanda Mariei, **Retrimite biletele pe email** | „Retrimiterea e limitată la o dată la 2 minute.” | dacă au trecut 2 minute de la plată: „Trimitem biletele la …”; altfel: „poți cere din nou peste … secunde” (și asta e o funcționalitate) |
| 5:45 | Înapoi la slide-uri | | |

## 6. Plan B, pentru fiecare pas care poate pica

| Ce pică | Ce faci | Ce spui |
|---|---|---|
| **Internetul / Stripe** (pagina de plată nu se încarcă) | Arată capturile din `docs/img/final`: `04-plata-stripe.png`, `05-comanda-platita.png`, sau capitolul 7.4 din `docs/raport-final.pdf`. Continuă cu comanda lui Radu (pasul 0), care e deja plătită. | „Stripe e un serviciu extern; exact de aceea serverul nu se bazează pe pagina de întoarcere.” |
| **Comanda rămâne „Pending”** după plată | Verifică fereastra 2: dacă webhook-ul primește `400`, secretul diferă. Oprește API-ul (Ctrl+C) și pornește-l din nou cu `demo-api.ps1`. Între timp, arată „Confirmarea durează mai mult” de pe pagină. | „Fără confirmarea Stripe nu emitem bilete. Procesul de fundal verifică oricum plata la Stripe și emite biletele în câteva zeci de secunde.” |
| **Camera telefonului** nu pornește sau nu citește | Scrie **codul scurt** (cele 8 caractere de sub QR) în „Cod de mână”. Merge identic. | „Am prevăzut introducerea manuală tocmai pentru lumină slabă sau o cameră defectă.” |
| **Telefonul nu deschide site-ul** (rețea, firewall) | Deschide `/scaneaza` pe laptop, în fereastra normală (organizatorul are voie să scaneze biletele evenimentelor lui) și folosește codul de mână. | „Camera cere HTTPS; pe laptop, `localhost` e considerat sigur.” |
| **Emailul** nu apare în smtp4dev | Arată biletele direct pe pagina comenzii (sunt aceleași coduri) și captura `06-email-bilete.png`. | „Emailul se trimite în fundal, cu reîncercări; biletele sunt oricum în cont.” |
| **Totul** (laptopul nu pornește demo-ul) | Prezintă capturile în ordine, din raportul final (capitolul 7) sau din `docs/img/final` (01 → 19): este exact aceeași poveste. | „Vă arăt aceeași demonstrație, înregistrată automat pe date reale.” |

## 7. Repetiția automată

`docs/tools/demo-capturi.mjs` parcurge **automat** tot scenariul de mai sus, într-un Chrome real, și face capturile din raportul final. E și cea mai rapidă verificare că mediul e pregătit. După pașii 1–4 din secțiunea 2 (fără `npm run start:phone`; în locul lui `npm start` în `eventpass.client`):

```
node docs/tools/demo-capturi.mjs
```

Cu site-ul pornit cu `npm run start:phone` (HTTPS), dă întâi adresa: în PowerShell `$env:EP_SITE = "https://localhost:4200"`, apoi aceeași comandă. Durează ~4 minute (din care 2 minute așteaptă limita de retrimitere). Apoi rulează din nou `demo-reset.ps1`, repornește API-ul și `seed-demo.mjs`, ca demo-ul live să înceapă pe date curate.

**Repetițiile automate din 15.11.2026, pe date curate:**

| Rulare | Site | Rezultat |
|---|---|---|
| 1 (capturile din raport) | `http://localhost:4200` (`npm start`) | 12 din 12 pași, 215 s |
| 2 | `https://localhost:4200` (`npm run start:phone`), fără `-Telefon` și fără numerotarea nouă | plata a mers; **rambursarea a eșuat** (vezi mai jos) |
| 3 | `https://localhost:4200`, API cu `-Telefon`, după `demo-reset.ps1` cu numerotarea nouă | **12 din 12 pași, 211 s** (comenzile #422880–#422883) |

Pașii rulării 1:

| Pas | Rezultat |
|---|---|
| Pagina principală | 7 evenimente în 4 orașe |
| 3 cumpărături de fundal (client 2), plătite pe Stripe | comenzile #1–#3 plătite |
| Clientul cumpără 2 × Standard și plătește | comanda #4 „Paid”; biletele pe pagină la 0,3 s după întoarcerea de pe Stripe |
| Emailul | primit în smtp4dev, cu 2 coduri QR |
| Retrimitere imediat după plată | refuzată: „Poți cere din nou peste 114 de secunde” |
| Staff: scanare, apoi a doua scanare | „Bilet valid”, apoi „Deja utilizat” |
| Organizer: rambursarea comenzii #2 | biletele „Rambursat”; panoul: 600 lei încasați, 500 lei rambursați |
| Scanarea unui bilet rambursat | „Respins” |
| Vederi de telefon (390 × 844) | pagina principală, biletul, scanner valid / deja utilizat |
| Retrimitere după 2 minute | al doilea email ajuns |
| Swagger | încărcat |

**Ce a ieșit la iveală la repetiții** (și e deja rezolvat în scripturi sau în scenariu):

- **Întoarcerea de pe Stripe în modul telefon:** cu site-ul pe HTTPS, adresa implicită de întoarcere (`http://localhost:4200`) nu se încarcă. Rezolvat: `demo-api.ps1 -Telefon`.
- **Rambursarea după un reset al bazei:** Stripe a răspuns „Keys for idempotent requests can only be used with the same parameters they were first used with” pentru cheia `eventpass-refund-order-2`, deja folosită în repetiția anterioară pentru altă plată. Comanda a rămas „Rambursare în curs”. Cauza e în cod (`OrderRefundService.RefundKey` folosește doar numărul comenzii, nu și sesiunea Stripe); pentru demo, `demo-reset.ps1` evită problema pornind numerotarea comenzilor de la o valoare nouă. **Corectura în cod nu e făcută** (cheia ar trebui să conțină id-ul sesiunii Stripe), pentru că nu ține de documentație. Într-o bază de producție, care nu se reface niciodată, problema nu apare.

- Retrimiterea **imediat după plată** e refuzată (limita de 2 minute se aplică și primului email). În demo, arat-o ca pe o protecție sau la final.
- Pagina Stripe are formularul într-un iframe: automatizarea apasă la coordonate. Pentru demo-ul de mână nu contează.
- **Nu a putut fi automatizată** scanarea cu o cameră reală: în repetiție, codul e introdus de mână. Camera telefonului trebuie probată de mână, cu o zi înainte.

## 8. După prezentare

- `Ctrl+C` în ferestrele 2, 3 și 5.
- `docker compose down` oprește containerele și **păstrează** datele. (`docker compose down -v` ar șterge și baza ta de lucru.)
