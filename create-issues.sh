#!/usr/bin/env bash
# Creează etichete, milestone-uri și issue-uri în repository-ul curent.
# Cerințe: GitHub CLI (gh) autentificat: `gh auth login`
# Rulare din folderul repo-ului: bash create-issues.sh
set -e
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
echo "Repo: $REPO"

for l in "backend:0e8a16" "frontend:1d76db" "payments:635bfc" "email:fbca04" "qr:5319e7" "testing:d93f0b" "docs:0075ca" "devops:6e7781" "security:b60205"; do
  gh label create "${l%%:*}" --color "${l##*:}" --force >/dev/null
done

for m in "Raport 1 - Propunere de proiect" "Raport 2 - Proiectare si implementare minimala" "Raport final"; do
  gh api "repos/$REPO/milestones" -f title="$m" >/dev/null 2>&1 || true
done

M1="Raport 1 - Propunere de proiect"
M2="Raport 2 - Proiectare si implementare minimala"
M3="Raport final"

issue() { gh issue create --title "$1" --body "$2" --label "$3" --milestone "$4" >/dev/null && echo "+ $1"; }

# --- Raport 1 ---
issue "Specificații: cerințe funcționale și nefuncționale" "Listă de cerințe, roluri (Client, Organizator, Staff, Admin) și reguli de business." "docs" "$M1"
issue "Diagrama cazurilor de utilizare" "Diagramă UML a cazurilor de utilizare pentru toate rolurile." "docs" "$M1"
issue "Alegerea și justificarea instrumentelor" "Documentează tehnologiile și instrumentele folosite (vezi raportul preliminar)." "docs" "$M1"
issue "Inițializare repository, structură soluție și README" "Soluție .NET + proiect Angular, .gitignore, README, GitHub Projects." "devops" "$M1"
issue "Configurare GitHub Actions (build + teste)" "Workflow care rulează dotnet build și dotnet test la fiecare push și PR." "devops" "$M1"

# --- Raport 2 ---
issue "Model de date și migrări EF Core" "Entități: User, Event, TicketType, Order, Ticket (Token, IsUsed, UsedAt)." "backend" "$M2"
issue "Autentificare și roluri (JWT)" "Înregistrare, login, roluri Client/Organizator/Staff/Admin." "backend,security" "$M2"
issue "CRUD evenimente și tipuri de bilete (organizator)" "Creare, editare, publicare evenimente, stoc per tip de bilet." "backend" "$M2"
issue "Listare și detalii evenimente (client)" "Pagină cu evenimente și detalii; căutare/filtrare simplă." "frontend" "$M2"
issue "Coș și creare comandă (Order)" "Selectare bilete, rezervare temporară a locurilor, creare comandă în stare Pending." "backend,frontend" "$M2"
issue "Integrare Stripe Checkout (mod test)" "Creare sesiune de plată și redirecționare către Stripe." "payments,backend" "$M2"
issue "Endpoint webhook Stripe cu verificarea semnăturii" "Procesează checkout.session.completed; emiterea biletelor se face doar aici." "payments,backend,security" "$M2"
issue "Generare cod QR cu QRCoder" "Token unic (GUID) pe bilet; generare imagine QR." "qr,backend" "$M2"
issue "Trimitere bilet cu QR pe email (MailKit)" "Email cu QR inline/atașat; SMTP de test în dezvoltare." "email,backend" "$M2"
issue "Document Raport 2: proiectare și implementare minimală" "Arhitectură, diagrama bazei de date, fluxul plății, capturi de ecran." "docs" "$M2"

# --- Raport final ---
issue "Scanner QR în browser (html5-qrcode)" "Pagină pentru Staff care scanează codul cu camera telefonului." "qr,frontend" "$M3"
issue "Validare bilet la intrare (existent, plătit, nefolosit)" "Endpoint de validare; al doilea scan returnează 'bilet deja utilizat'." "qr,backend" "$M3"
issue "Gestionarea concurenței la stoc" "Rezervare temporară și eliberare la expirarea sesiunii de plată; test pentru cursă." "backend" "$M3"
issue "Rambursări și anulare comandă" "Rambursare prin Stripe și invalidarea biletelor." "payments,backend" "$M3"
issue "Panou organizator: vânzări și participanți" "Statistici de vânzări, listă de bilete, număr de bilete validate." "frontend,backend" "$M3"
issue "Retrimitere bilet pe email" "Clientul poate cere din nou emailul cu QR." "email,backend" "$M3"
issue "Teste unitare xUnit pentru regulile critice" "Bilet de unică folosință, stoc, emitere doar din webhook." "testing" "$M3"
issue "Docker Compose (PostgreSQL + SMTP de test)" "Mediu de dezvoltare pornit cu o singură comandă." "devops" "$M3"
issue "Documentație finală și prezentare" "Raport final, demonstrație funcțională completă, slide-uri." "docs" "$M3"
echo "Gata. Creează acum un tablou GitHub Projects (Backlog / În lucru / Review / Gata) și adaugă issue-urile."
