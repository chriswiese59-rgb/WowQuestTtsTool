# WoW Quest TTS Tool

Ein WPF-Tool zum Reviewen und Generieren von Text-to-Speech Audio für World of Warcraft Quests.

## Features

- **Quest-Browser**: Durchsuche und filtere Quests nach Titel, Beschreibung, Zone
- **TTS-Einzelvorschau**: Generiere Audio für einzelne Quests direkt in der App (ElevenLabs API)
- **Batch-Export**: Exportiere gefilterte Quests für die Python-Pipeline
- **Audio-Player**: Integrierter Player zum Abspielen generierter Audios
- **Review-Workflow**: Markiere Quests als "geprüft" für den Workflow

## Projektstruktur

```
WowQuestTtsTool/
├── MainWindow.xaml          # WPF UI
├── MainWindow.xaml.cs       # UI-Logik
├── Quest.cs                 # Quest-Modell
├── Services/
│   ├── ElevenLabsService.cs # ElevenLabs API Client
│   └── TtsConfigService.cs  # Konfiguration laden/speichern
├── config/
│   └── tts_config.json      # ElevenLabs API-Key & Einstellungen
├── data/
│   └── quests_deDE.json     # Quest-Daten
├── audio/
│   └── deDE/                # Generierte Audio-Dateien
└── scripts/
    ├── tts_batch_generator.py  # Python Batch-Pipeline
    └── requirements.txt        # Python-Abhängigkeiten
```

## Setup

### 1. WPF-App

```powershell
cd WowQuestTtsTool
dotnet build
dotnet run
```

### 2. ElevenLabs API-Key konfigurieren

Bearbeite `config/tts_config.json`:

```json
{
  "elevenlabs": {
    "api_key": "DEIN_ELEVENLABS_API_KEY"
  }
}
```

### 3. Python-Pipeline (optional, für Batch-Generierung)

```powershell
cd scripts
pip install -r requirements.txt
```

## Workflow

### Einzelne Quests reviewen (WPF)

1. Starte die App mit `dotnet run`
2. Wähle eine Quest aus der Liste
3. Konfiguriere Voice-Profil und Einstellungen
4. Klicke "TTS generieren & abspielen"
5. Markiere die Quest als "geprüft"

### Batch-Generierung (Python)

1. Filtere Quests in der WPF-App (z.B. nach Zone)
2. Klicke "Batch-Export für Python"
3. Führe das Python-Skript aus:

```powershell
# Alle Quests aus dem Export
python scripts/tts_batch_generator.py --from-export

# Nur Dry-Run (zeigt was generiert würde)
python scripts/tts_batch_generator.py --from-export --dry-run

# Spezifische Quest-IDs
python scripts/tts_batch_generator.py --quest-id 176 54 783

# Mit anderem Voice-Profil
python scripts/tts_batch_generator.py --from-export --voice epic_narrator
```

## Voice-Profile

| Profil | Beschreibung |
|--------|--------------|
| `neutral_male` | Adam - Neutrale männliche Stimme |
| `neutral_female` | Rachel - Neutrale weibliche Stimme |
| `epic_narrator` | Arnold - Epischer Erzähler |

## Konfiguration (tts_config.json)

```json
{
  "elevenlabs": {
    "api_key": "YOUR_API_KEY",
    "voice_id": "pNInz6obpgDQGcFmaJgB",
    "model_id": "eleven_multilingual_v2",
    "voice_settings": {
      "stability": 0.5,
      "similarity_boost": 0.75
    }
  },
  "paths": {
    "quests_json": "data/quests_deDE.json",
    "audio_output": "audio/deDE",
    "batch_export": "data/batch_export.json"
  }
}
```

## Export-Formate

### Batch-Export (JSON)

```json
[
  {
    "quest_id": 176,
    "title": "GESUCHT: \"Hogger\"",
    "tts_text": "GESUCHT: \"Hogger\". Ein riesiger Gnoll...",
    "voice_profile": "neutral_male",
    "tts_reviewed": false
  }
]
```

### CSV-Export

```csv
quest_id,title,zone,tts_text,tts_reviewed
176,"GESUCHT: ""Hogger""","Wald von Elwynn","GESUCHT: ""Hogger"". Ein riesiger Gnoll...",false
```

## Tastenkürzel

- Filtern: Einfach ins Suchfeld tippen
- Nächste Quest: "Nächste Quest" Button

## Hinweise

- Audio-Dateien werden als `quest_{id}.mp3` im Ordner `audio/deDE/` gespeichert
- Die Python-Pipeline überspringt bereits generierte Audios (nutze `--no-skip-existing` zum Überschreiben)
- Bei Rate-Limiting durch ElevenLabs: `--delay 1.0` erhöhen
