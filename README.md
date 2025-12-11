# WoW Quest TTS Tool

Ein Windows-Tool zur Generierung von Text-to-Speech (TTS) Audio für World of Warcraft Quests. Erstellt Hörbuch-artige Vertonungen von Quest-Texten für ein immersiveres Spielerlebnis.

## Features

### Quest-Verwaltung
- **Blizzard API Integration**: Lädt Quest-Daten direkt von der Blizzard API
- **JSON Import/Export**: Unterstützung für lokale Quest-Datenbanken
- **CSV Import**: Importiert Quest-Texte aus CSV-Dateien
- **AzerothCore MySQL**: Direkter Import aus AzerothCore-Datenbanken
- **Filter & Suche**: Umfangreiche Filter nach Zone, Kategorie, Sprache, TTS-Status
- **Quest-Klassifizierung**: Automatische Erkennung von Haupt-/Nebenquests

### TTS-Generierung
- **ElevenLabs Integration**: Hochwertige Sprachsynthese mit verschiedenen Stimmen
- **Dual-Voice Support**: Männliche und weibliche Erzählerstimmen
- **Batch-Verarbeitung**: Generiert TTS für mehrere Quests automatisch
- **Stimm-Profile**: Vorkonfigurierte Profile mit anpassbaren Parametern
- **Audio-Preview**: Integrierter Player zum Anhören der generierten Audio-Dateien

### Text-KI (Hörbuch-Optimierung)
- **API-Modus**: Automatische Text-Optimierung via OpenAI, Claude oder Gemini
- **Manueller Modus**: Kopiert Prompts für Browser-Premium-Accounts (ChatGPT+, Gemini Pro, Claude Pro) - keine API-Kosten
- **Hörbuch-Stil**: Wandelt knappe Quest-Texte in lebendige Erzählungen um

### WoW Addon Export
- **Automatischer Export**: Generiert ein komplettes WoW-Addon mit allen Audio-Dateien
- **Audio-Index**: Erstellt `WowTts_Data.lua` für das Addon
- **OGG-Konvertierung**: Konvertiert MP3 zu OGG für WoW-Kompatibilität

### Update & Sync
- **Snapshot-System**: Erkennt neue und geänderte Quests automatisch
- **Selektive TTS-Generierung**: Vertont nur geänderte Inhalte
- **Diff-Ansicht**: Zeigt Unterschiede zwischen Versionen

### End-to-End Test
- **Pipeline-Test**: Testet die komplette Kette: Quest → TTS → Audio → Addon
- **Detailliertes Logging**: Protokolliert jeden Schritt

## Installation

### Voraussetzungen
- Windows 10/11 (64-bit)
- Optional: .NET 8.0 Runtime (nur für Debug-Builds)

### Release-Version (Empfohlen)

1. Lade die neueste Release-Version herunter
2. Entpacke in einen Ordner deiner Wahl (z.B. `D:\Tools\WowQuestTtsTool`)
3. Starte `WowQuestTtsTool.exe`

### Aus Quellcode bauen

```bash
# Repository klonen
git clone https://github.com/dein-repo/WowQuestTtsTool.git
cd WowQuestTtsTool

# Debug-Build
dotnet build

# Release-Build (self-contained, ~165 MB)
dotnet publish -c Release -r win-x64 --self-contained true -o "./publish"

# Oder verwende das mitgelieferte Script:
.\publish.bat
# oder
.\publish.ps1 -OpenFolder
```

#### Publish-Optionen (PowerShell)

```powershell
# Standard (Ordner mit DLLs)
.\publish.ps1

# Mit angepasstem Zielordner
.\publish.ps1 -PublishDir "C:\MeinPfad\WowTts"

# Single-File EXE (eine große Datei)
.\publish.ps1 -SingleFile

# Ordner nach Build öffnen
.\publish.ps1 -OpenFolder
```

## Projektstruktur

```
WowQuestTtsTool/
├── WowQuestTtsTool.exe         # Hauptprogramm
├── publish.bat                 # Release-Build Script (Batch)
├── publish.ps1                 # Release-Build Script (PowerShell)
├── config/
│   └── tts_config.json         # API-Keys und Einstellungen
├── data/
│   ├── quests_cache.json       # Quest-Cache
│   ├── quest_overrides.json    # Text-Anpassungen
│   └── quests_deDE.json        # Quest-Daten (Import)
├── audio/
│   └── deDE/                   # Generierte Audio-Dateien
│       ├── male/               # Männliche Stimme
│       └── female/             # Weibliche Stimme
├── snapshots/                  # Update & Sync Snapshots
├── Services/                   # Backend-Services
│   ├── ElevenLabsService.cs    # ElevenLabs API Client
│   ├── ElevenLabsTtsService.cs # TTS-Generierung
│   ├── BlizzardQuestService.cs # Blizzard API Client
│   ├── LlmTextEnhancerService.cs # Text-KI (OpenAI/Claude/Gemini)
│   ├── AddonExportService.cs   # WoW Addon Export
│   ├── UpdateSyncService.cs    # Update & Sync
│   └── ...
└── Models/                     # Datenmodelle
```

## Konfiguration

### ElevenLabs API (erforderlich für TTS)

1. Erstelle einen Account bei [ElevenLabs](https://elevenlabs.io)
2. Kopiere deinen API-Key aus dem Dashboard
3. Im Tool: **Einstellungen** → **ElevenLabs** → API-Key einfügen

### Blizzard API (optional)

1. Erstelle eine App im [Blizzard Developer Portal](https://develop.battle.net)
2. Im Tool: **Einstellungen** → **Blizzard API** → Client ID und Secret einfügen

### Text-KI (optional)

#### API-Modus
1. Im Tool: **Einstellungen** → **LLM/Text-KI**
2. Wähle Provider: OpenAI, Claude oder Gemini
3. API-Key einfügen

#### Manueller Modus (kostenlos mit Premium-Account)
1. Wähle **Manuell (Browser-Premium)** im Text-KI Bereich
2. Klicke **Text glätten (LLM)**
3. Kopiere den Prompt ins Browser-Fenster (ChatGPT+, Gemini, Claude)
4. Füge das Ergebnis zurück ein

### Konfigurationsdatei (tts_config.json)

```json
{
  "elevenlabs": {
    "api_key": "YOUR_ELEVENLABS_API_KEY",
    "voice_id": "pNInz6obpgDQGcFmaJgB",
    "model_id": "eleven_multilingual_v2",
    "voice_settings": {
      "stability": 0.5,
      "similarity_boost": 0.75
    }
  },
  "blizzard": {
    "client_id": "YOUR_BLIZZARD_CLIENT_ID",
    "client_secret": "YOUR_BLIZZARD_CLIENT_SECRET",
    "region": "eu"
  },
  "llm": {
    "provider": "OpenAI",
    "api_key": "YOUR_OPENAI_API_KEY",
    "model_id": "gpt-4o-mini"
  }
}
```

## Verwendung

### Quests laden

| Methode | Beschreibung |
|---------|--------------|
| **Von Blizzard laden** | Lädt Quests direkt von der Blizzard API |
| **JSON laden** | Importiert eine lokale Quest-JSON-Datei |
| **CSV Import** | Importiert Quest-Texte aus CSV |
| **Zone laden** | Lädt Quests einer bestimmten Zone |

### TTS generieren

1. Quest in der Liste auswählen
2. Optionen:
   - **Einzelne Quest vertonen**: Generiert TTS für die ausgewählte Quest (M+W)
   - **Alle TTS (M+W) generieren**: Batch-Verarbeitung für alle gefilterten Quests

### Text optimieren (optional)

1. Quest auswählen
2. Im **Text-KI** Bereich Modus wählen:
   - **API (automatisch)**: Kostet API-Tokens
   - **Manuell (Browser-Premium)**: Kostenlos mit ChatGPT+/Gemini/Claude
3. **Text glätten (LLM)** klicken
4. Bei manuellem Modus:
   - Prompt kopieren → Im Browser einfügen → Ergebnis kopieren → Einfügen → Übernehmen

### Addon exportieren

1. **Addon Export** klicken
2. Zielordner wählen: `WoW/_retail_/Interface/AddOns/WowTts`
3. Export starten
4. WoW neu starten oder `/reload`

### Update & Sync

1. **Update & Sync** klicken
2. Quests scannen → Neue/geänderte Quests werden erkannt
3. Auswahl treffen → TTS nur für Änderungen generieren

## Voice-Profile

| Profil | Beschreibung | Einsatz |
|--------|--------------|---------|
| `male_narrator` | Männlicher Erzähler | Standard für männliche NPCs |
| `female_narrator` | Weibliche Erzählerin | Standard für weibliche NPCs |
| `epic_narrator` | Epischer Erzähler | Dramatische Quests |

## Tastenkürzel

| Taste | Funktion |
|-------|----------|
| Strg+S | Projekt speichern |
| M | Male Audio abspielen |
| W | Female Audio abspielen |

## Filter-Presets

| Preset | Beschreibung |
|--------|--------------|
| **Main -TTS** | Hauptstory-Quests ohne TTS |
| **Side -TTS** | Nebenquests ohne TTS |
| **Gruppen** | Alle Gruppenquests |
| **-TTS** | Alle Quests ohne TTS |
| **Reset** | Alle Filter zurücksetzen |

## Troubleshooting

### "API nicht konfiguriert"
→ Öffne **Einstellungen** und trage deinen ElevenLabs API-Key ein

### "Verbindung zu Blizzard fehlgeschlagen"
→ Prüfe Client ID und Secret in den Einstellungen
→ Stelle sicher, dass du eine aktive Internetverbindung hast

### Audio-Dateien werden nicht im Addon erkannt
→ Stelle sicher, dass die Dateien im OGG-Format vorliegen
→ Prüfe den Pfad in `WowTts_Data.lua`
→ Führe `/reload` in WoW aus

### TTS-Generierung schlägt fehl
→ Prüfe dein ElevenLabs-Guthaben
→ Prüfe die Voice-ID in den Einstellungen
→ Teste mit einer kürzeren Quest

### Build schlägt fehl
```bash
# NuGet-Pakete wiederherstellen
dotnet restore

# Clean Build
dotnet clean
dotnet build
```

## Kosten

### ElevenLabs
- Free Tier: ~10.000 Zeichen/Monat
- Starter: $5/Monat für ~30.000 Zeichen
- Creator: $22/Monat für ~100.000 Zeichen

### Text-KI (optional)
- **API-Modus**: Ca. $0.01-0.03 pro Quest (GPT-4o-mini)
- **Manueller Modus**: Kostenlos mit Premium-Account (ChatGPT+, Gemini, Claude)

## Lizenz

Dieses Projekt ist für den persönlichen Gebrauch bestimmt.

World of Warcraft und zugehörige Marken sind Eigentum von Blizzard Entertainment.

## Changelog

### Version 1.0.0
- Initiale Version
- ElevenLabs TTS Integration
- Blizzard API Support
- Text-KI Optimierung (API + Manueller Modus)
- WoW Addon Export
- Update & Sync System
- End-to-End Test
- Release-Build Scripts (publish.bat, publish.ps1)
