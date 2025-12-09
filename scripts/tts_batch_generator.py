#!/usr/bin/env python3
"""
WoW Quest TTS Batch Generator
Generiert Audio-Dateien für WoW-Quests via ElevenLabs API.

Usage:
    python tts_batch_generator.py                    # Alle unverarbeiteten Quests
    python tts_batch_generator.py --quest-id 12345   # Einzelne Quest
    python tts_batch_generator.py --from-export      # Aus batch_export.json
    python tts_batch_generator.py --dry-run          # Nur Vorschau, keine API-Calls
"""

import json
import os
import sys
import argparse
import time
from pathlib import Path
from typing import Optional
from dataclasses import dataclass

try:
    from elevenlabs import ElevenLabs
    from tqdm import tqdm
except ImportError as e:
    print(f"Fehler: {e}")
    print("Bitte installiere die Abhängigkeiten: pip install -r requirements.txt")
    sys.exit(1)


@dataclass
class Quest:
    quest_id: int
    title: str
    description: str
    objectives: str
    completion: str
    zone: str
    is_main_story: bool

    @property
    def tts_text(self) -> str:
        """Generiert den TTS-Text (Titel + Beschreibung)."""
        if self.title:
            return f"{self.title}. {self.description or ''}"
        return self.description or ""

    @classmethod
    def from_dict(cls, data: dict) -> "Quest":
        return cls(
            quest_id=data.get("quest_id", 0),
            title=data.get("title", ""),
            description=data.get("description", ""),
            objectives=data.get("objectives", ""),
            completion=data.get("completion", ""),
            zone=data.get("zone", ""),
            is_main_story=data.get("is_main_story", False),
        )


class TTSBatchGenerator:
    def __init__(self, config_path: str = None):
        self.project_root = Path(__file__).parent.parent
        self.config_path = config_path or self.project_root / "config" / "tts_config.json"
        self.config = self._load_config()
        self.client: Optional[ElevenLabs] = None
        self._init_client()

    def _load_config(self) -> dict:
        """Lädt die Konfigurationsdatei."""
        if not Path(self.config_path).exists():
            raise FileNotFoundError(f"Config nicht gefunden: {self.config_path}")

        with open(self.config_path, "r", encoding="utf-8") as f:
            return json.load(f)

    def _init_client(self):
        """Initialisiert den ElevenLabs-Client."""
        api_key = self.config["elevenlabs"]["api_key"]

        if api_key == "YOUR_ELEVENLABS_API_KEY_HERE":
            print("WARNUNG: Kein API-Key konfiguriert!")
            print(f"Bitte trage deinen Key in {self.config_path} ein.")
            return

        self.client = ElevenLabs(api_key=api_key)

    def load_quests(self, quest_file: str = None) -> list[Quest]:
        """Lädt Quests aus der JSON-Datei."""
        quest_path = quest_file or self.project_root / self.config["paths"]["quests_json"]

        if not Path(quest_path).exists():
            raise FileNotFoundError(f"Quest-Datei nicht gefunden: {quest_path}")

        with open(quest_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        return [Quest.from_dict(q) for q in data]

    def load_batch_export(self) -> list[dict]:
        """Lädt den Batch-Export aus der WPF-App."""
        export_path = self.project_root / self.config["paths"]["batch_export"]

        if not Path(export_path).exists():
            raise FileNotFoundError(
                f"Batch-Export nicht gefunden: {export_path}\n"
                "Bitte exportiere zuerst Quests aus der WPF-App."
            )

        with open(export_path, "r", encoding="utf-8") as f:
            return json.load(f)

    def get_existing_audio_files(self) -> set[int]:
        """Gibt die Quest-IDs zurück, für die bereits Audio existiert."""
        audio_dir = self.project_root / self.config["paths"]["audio_output"]
        audio_dir.mkdir(parents=True, exist_ok=True)

        existing = set()
        for file in audio_dir.glob("quest_*.mp3"):
            try:
                quest_id = int(file.stem.replace("quest_", ""))
                existing.add(quest_id)
            except ValueError:
                pass

        return existing

    def generate_audio(self, quest: Quest, voice_profile: str = None) -> Optional[Path]:
        """Generiert Audio für eine Quest via ElevenLabs."""
        if not self.client:
            print("Fehler: ElevenLabs-Client nicht initialisiert.")
            return None

        text = quest.tts_text
        if not text.strip():
            print(f"  Überspringe Quest {quest.quest_id}: Kein Text vorhanden")
            return None

        # Voice-ID aus Profil oder Default
        if voice_profile and voice_profile in self.config["voice_profiles"]:
            voice_id = self.config["voice_profiles"][voice_profile]["voice_id"]
        else:
            voice_id = self.config["elevenlabs"]["voice_id"]

        # Output-Pfad
        audio_dir = self.project_root / self.config["paths"]["audio_output"]
        audio_dir.mkdir(parents=True, exist_ok=True)
        output_path = audio_dir / f"quest_{quest.quest_id}.mp3"

        try:
            # Audio generieren
            audio = self.client.text_to_speech.convert(
                voice_id=voice_id,
                text=text,
                model_id=self.config["elevenlabs"]["model_id"],
                voice_settings=self.config["elevenlabs"]["voice_settings"],
            )

            # Als Datei speichern
            with open(output_path, "wb") as f:
                for chunk in audio:
                    f.write(chunk)

            return output_path

        except Exception as e:
            print(f"  Fehler bei Quest {quest.quest_id}: {e}")
            return None

    def run_batch(
        self,
        quest_ids: list[int] = None,
        from_export: bool = False,
        skip_existing: bool = True,
        dry_run: bool = False,
        voice_profile: str = None,
        delay: float = 0.5,
    ):
        """Führt die Batch-Generierung durch."""

        # Quests laden
        if from_export:
            export_data = self.load_batch_export()
            quests = [Quest.from_dict(q) for q in export_data]
            print(f"Geladene Quests aus Batch-Export: {len(quests)}")
        else:
            all_quests = self.load_quests()
            if quest_ids:
                quests = [q for q in all_quests if q.quest_id in quest_ids]
            else:
                quests = all_quests
            print(f"Geladene Quests: {len(quests)}")

        # Bereits vorhandene überspringen
        if skip_existing:
            existing = self.get_existing_audio_files()
            quests = [q for q in quests if q.quest_id not in existing]
            print(f"Nach Filterung (ohne bereits vorhandene): {len(quests)}")

        if not quests:
            print("Keine Quests zu verarbeiten.")
            return

        # Dry-Run: Nur anzeigen
        if dry_run:
            print("\n[DRY-RUN] Folgende Quests würden verarbeitet:")
            for q in quests[:20]:
                print(f"  - Quest {q.quest_id}: {q.title[:50]}...")
            if len(quests) > 20:
                print(f"  ... und {len(quests) - 20} weitere")
            return

        # Batch-Generierung
        print(f"\nStarte Generierung für {len(quests)} Quests...")

        successful = 0
        failed = 0

        for quest in tqdm(quests, desc="TTS-Generierung"):
            result = self.generate_audio(quest, voice_profile)

            if result:
                successful += 1
            else:
                failed += 1

            # Rate-Limiting
            time.sleep(delay)

        print(f"\nAbgeschlossen: {successful} erfolgreich, {failed} fehlgeschlagen")


def main():
    parser = argparse.ArgumentParser(
        description="WoW Quest TTS Batch Generator",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )

    parser.add_argument(
        "--quest-id",
        type=int,
        nargs="+",
        help="Spezifische Quest-ID(s) verarbeiten",
    )
    parser.add_argument(
        "--from-export",
        action="store_true",
        help="Quests aus WPF-Batch-Export laden",
    )
    parser.add_argument(
        "--skip-existing",
        action="store_true",
        default=True,
        help="Bereits vorhandene Audio-Dateien überspringen (default: True)",
    )
    parser.add_argument(
        "--no-skip-existing",
        action="store_true",
        help="Alle Quests neu generieren",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Nur Vorschau, keine API-Calls",
    )
    parser.add_argument(
        "--voice",
        choices=["neutral_female", "neutral_male", "epic_narrator"],
        default="neutral_male",
        help="Voice-Profil auswählen",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.5,
        help="Verzögerung zwischen API-Calls in Sekunden",
    )
    parser.add_argument(
        "--config",
        type=str,
        help="Pfad zur Konfigurationsdatei",
    )

    args = parser.parse_args()

    try:
        generator = TTSBatchGenerator(config_path=args.config)

        generator.run_batch(
            quest_ids=args.quest_id,
            from_export=args.from_export,
            skip_existing=not args.no_skip_existing,
            dry_run=args.dry_run,
            voice_profile=args.voice,
            delay=args.delay,
        )
    except FileNotFoundError as e:
        print(f"Fehler: {e}")
        sys.exit(1)
    except Exception as e:
        print(f"Unerwarteter Fehler: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
