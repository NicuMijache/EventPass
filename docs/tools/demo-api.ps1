# Pornește API-ul pentru demonstrație, cu setările demo-ului puse ca variabile de mediu
# (au prioritate față de User Secrets, doar în această fereastră; secretele tale rămân neschimbate):
#   - baza de date: eventpass_demo din containerul Docker (portul 5433);
#   - Stripe:WebhookSecret: citit chiar acum din Stripe CLI, deci e sigur identic cu cel al lui `stripe listen`
#     (altfel plata trece, dar webhook-ul e respins cu 400 și comanda rămâne „Pending”);
#   - contul de Admin al demo-ului (admin@eventpass.demo / Demo2026!), creat la prima pornire.
# Cheia Stripe (Stripe:SecretKey, mod test) și cheia JWT rămân cele din User Secrets.
# Utilizare (din rădăcina repository-ului): powershell -ExecutionPolicy Bypass -File docs/tools/demo-api.ps1 [-Telefon]
#   -Telefon: când site-ul rulează cu `npm run start:phone` (HTTPS). Atunci Stripe trebuie să trimită clientul înapoi
#             pe https://localhost:4200, nu pe http:// (adresa implicită), altfel întoarcerea de pe pagina de plată nu se încarcă.
param([switch]$Telefon)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $root

$envFile = @{}
Get-Content .env | Where-Object { $_ -match '^\s*([A-Z_]+)=(.*)$' } | ForEach-Object { $envFile[$Matches[1]] = $Matches[2].Trim() }

$secret = (stripe --project-name default listen --print-secret).Trim()
if (-not $secret.StartsWith('whsec_')) { throw 'Stripe CLI nu a dat secretul webhook-ului. Rulează întâi: stripe login' }

$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5433;Database=eventpass_demo;Username=$($envFile['POSTGRES_USER']);Password=$($envFile['POSTGRES_PASSWORD'])"
$env:Stripe__WebhookSecret = $secret
$env:SeedAdmin__Email = 'admin@eventpass.demo'
$env:SeedAdmin__Password = 'Demo2026!'
if ($Telefon) {
  $env:Stripe__ClientBaseUrl = 'https://localhost:4200'
  $env:Client__BaseUrl = 'https://localhost:4200'
  Write-Host 'Mod telefon: întoarcerea de pe Stripe merge la https://localhost:4200'
}

Write-Host "API pentru demo: baza eventpass_demo, webhook secret $($secret.Substring(0, 10))... (din Stripe CLI)"
dotnet run --project EventPass.Server --launch-profile http
