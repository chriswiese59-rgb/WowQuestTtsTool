# ============================================================
# WowQuestTtsTool - Release Publish Script (PowerShell)
# ============================================================
# Erstellt eine self-contained Windows-EXE mit allen DLLs
# ============================================================

param(
    # Zielverzeichnis - HIER ANPASSEN:
    [string]$PublishDir = "D:\Tools\WowQuestTtsTool",

    # Single-File EXE erstellen (groessere EXE, aber nur eine Datei)
    [switch]$SingleFile,

    # Ordner nach Build oeffnen
    [switch]$OpenFolder
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "WowQuestTtsTool.csproj"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  WowQuestTtsTool - Release Build" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Zielverzeichnis: $PublishDir" -ForegroundColor Yellow
Write-Host "Single-File:     $SingleFile" -ForegroundColor Yellow
Write-Host ""

# Pruefen ob dotnet verfuegbar ist
try {
    $dotnetVersion = dotnet --version
    Write-Host "dotnet Version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "FEHLER: dotnet wurde nicht gefunden!" -ForegroundColor Red
    Write-Host "Bitte installiere das .NET SDK von: https://dotnet.microsoft.com/download" -ForegroundColor Red
    exit 1
}

# Alte Publish-Dateien loeschen
if (Test-Path $PublishDir) {
    Write-Host "Loesche alte Version..." -ForegroundColor Gray
    Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Starte Build und Publish..." -ForegroundColor Cyan
Write-Host ""

# Publish-Befehl zusammenstellen
$publishArgs = @(
    "publish"
    $ProjectFile
    "-c", "Release"
    "-r", "win-x64"
    "--self-contained", "true"
    "-p:PublishReadyToRun=true"
    "-o", $PublishDir
)

if ($SingleFile) {
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
} else {
    $publishArgs += "-p:PublishSingleFile=false"
}

# Build ausfuehren
try {
    & dotnet $publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Build fehlgeschlagen mit Exit-Code: $LASTEXITCODE"
    }
} catch {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host "  FEHLER: Build fehlgeschlagen!" -ForegroundColor Red
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

# Erstelle notwendige Unterordner
Write-Host ""
Write-Host "Erstelle Unterordner..." -ForegroundColor Gray
$subfolders = @("data", "config", "audio\deDE")
foreach ($folder in $subfolders) {
    $fullPath = Join-Path $PublishDir $folder
    if (-not (Test-Path $fullPath)) {
        New-Item -Path $fullPath -ItemType Directory -Force | Out-Null
    }
}

# Erfolg anzeigen
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  BUILD ERFOLGREICH!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Die EXE befindet sich in: $PublishDir" -ForegroundColor White
Write-Host ""

# Dateigroesse anzeigen
$exePath = Join-Path $PublishDir "WowQuestTtsTool.exe"
if (Test-Path $exePath) {
    $fileSize = (Get-Item $exePath).Length / 1MB
    Write-Host "EXE-Groesse: $([math]::Round($fileSize, 2)) MB" -ForegroundColor Gray
}

# Alle Dateien zaehlen
$fileCount = (Get-ChildItem -Path $PublishDir -Recurse -File).Count
Write-Host "Dateien gesamt: $fileCount" -ForegroundColor Gray

Write-Host ""
Write-Host "Du kannst die App jetzt starten mit:" -ForegroundColor White
Write-Host "  $exePath" -ForegroundColor Yellow
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan

# Ordner oeffnen falls gewuenscht
if ($OpenFolder) {
    explorer $PublishDir
} else {
    $response = Read-Host "Ordner im Explorer oeffnen? (j/n)"
    if ($response -eq "j" -or $response -eq "J") {
        explorer $PublishDir
    }
}
