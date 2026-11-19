# Reface de la zero baza de date a demonstrației (eventpass_demo, în containerul Docker).
# NU atinge baza ta de lucru (eventpass): demo-ul are baza lui.
# Utilizare (din rădăcina repository-ului, cu Docker Desktop pornit):
#   powershell -ExecutionPolicy Bypass -File docs/tools/demo-reset.ps1
# După reset: oprește API-ul (dacă rula), pornește-l din nou cu demo-api.ps1, apoi rulează seed-demo.mjs.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $root

$envFile = @{}
Get-Content .env | Where-Object { $_ -match '^\s*([A-Z_]+)=(.*)$' } | ForEach-Object { $envFile[$Matches[1]] = $Matches[2].Trim() }
$user = $envFile['POSTGRES_USER']; $pass = $envFile['POSTGRES_PASSWORD']

Write-Host '1/3 Pornesc containerele (PostgreSQL + smtp4dev)...'
docker compose up -d
for ($i = 0; $i -lt 30; $i++) {
  if ((docker compose ps db --format '{{.Health}}') -eq 'healthy') { break }
  Start-Sleep -Seconds 2
}

Write-Host '2/3 Șterg și recreez baza eventpass_demo...'
docker compose exec -T db psql -U $user -d postgres -c 'DROP DATABASE IF EXISTS eventpass_demo WITH (FORCE)'
docker compose exec -T db psql -U $user -d postgres -c 'CREATE DATABASE eventpass_demo'

Write-Host '3/3 Creez tabelele (migrările EF Core)...'
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5433;Database=eventpass_demo;Username=$user;Password=$pass"
dotnet ef database update --project EventPass.Server

# Numerele comenzilor pornesc de la o valoare nouă la fiecare reset.
# Motiv: cheia de idempotență a rambursării e „eventpass-refund-order-<nr. comenzii>”, iar Stripe o ține minte 24 de ore.
# După un reset, comanda #2 ar primi cheia unei rambursări vechi (alt pachet de bani) și Stripe ar refuza-o.
$start = [int](((Get-Date).ToUniversalTime() - [datetime]'2026-11-01').TotalMinutes) * 20 + 1000
# (comanda SQL merge prin intrarea standard: Windows PowerShell 5.1 ar pierde ghilimelele dintr-un argument)
"ALTER TABLE ""Orders"" ALTER COLUMN ""Id"" RESTART WITH $start;" | docker compose exec -T db psql -U $user -d eventpass_demo
Write-Host "Comenzile noi vor avea numere de la #$start în sus."
Write-Host 'Gata. Acum: demo-api.ps1 (într-o fereastră separată), apoi: node docs/tools/seed-demo.mjs'
