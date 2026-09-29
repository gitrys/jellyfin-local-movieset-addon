# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.
Das Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).

---

## [1.0.31.0] - 2026-09-29
### Hinzugefügt
- **Jellyfin 12 & .NET 10 Unterstützung:** Aktualisierung des Target Frameworks auf `net10.0` und Abhängigkeiten auf Jellyfin 12.0.0 (`targetAbi: 12.0.0.0`).
- **Relationaler Datenbank-Fallback (`LinkedChildren`):** `GetBoxSetMovies` fängt Jellyfin 12 Lazy-Loading ab und fragt Sammlungsmitglieder bei Bedarf direkt aus der relationalen SQLite-Datenbank ab.

### Geändert
- **Scheduled Tasks:** Migration des Intervall-Triggers von veraltetem String `TriggerInterval` auf modernes Enum `TaskTriggerInfoType.IntervalTrigger`.
- **CI/CD:** GitHub Actions Workflows (`ci.yml` und `release.yml`) auf .NET 10 SDK (`10.0.x`) aktualisiert.

### Entfernt
- **Reflection bereinigt:** Sämtliche Reflection-Hacks für `UpdatePeopleAsync`, `UpdatePeople` und `GetPeople` entfernt (nutzt nun die nativen Methoden auf `ILibraryManager`).

---

## [1.0.30.0] - 2026-07-02
### Behoben
- **Provider-Pipeline-Refactoring:** Metadaten und Artwork werden nun über Jellyfins native Provider-Pipeline (`BoxSetMetadataProvider` & `BoxSetImageProvider`) serialisiert angewendet (behebt Race Conditions bei `collection.xml`).
- **Artwork-Stream & Ordner-Matching:** Sammlungs-Artwork wird als Stream zurückgegeben und alternative Ordnernamenskonventionen von tinyMediaManager werden zuverlässig aufgelöst.
- **Stabilität:** NullReferenceException bei namenslosen Dummy-Items behoben und Bibliotheks-Erkennung optimiert.
- **Cross-Platform:** Plattformübergreifende Pfad- und Ordnernamensbereinigung für Linux- und Windows-Kompatibilität.

---

## [1.0.23.0] - 2026-07-02
### Hinzugefügt
- **Vorschau-Modus (Dry-Run):** Berechnung des nächsten Sync-Laufs ohne Schreibzugriffe direkt auf der Plugin-Einstellungsseite.
- **Status & Statistik:** Anzeige der Statistiken des letzten Sync-Laufs im Dashboard.

### Behoben
- **Verwaiste Ordner:** Physische `[boxset]`-Ordner auf der Festplatte werden beim Löschen einer verwaisten Sammlung restlos entfernt.
- **NFO-Parser:** Robustere XML-Fehlerbehandlung bei unvollständigen tinyMediaManager-Dateien.

---

## [1.0.19.0] - 2026-06-24
### Hinzugefügt
- **Metadaten-Aggregation:** Optionale Aggregation von Community-Bewertungen, Tags sowie Regisseuren, Autoren und Hauptdarstellern der Filme auf die übergeordnete Sammlung.
- **API-Kompatibilität:** Dynamische Methodenauflösung für `UpdatePeopleAsync` und `GetPeople` über verschiedene Jellyfin 10.x Versionen.

---

## [1.0.18.0] - 2026-06-24
### Hinzugefügt
- **Mount-Guard:** Schutz vor versehentlichem Löschen von Sammlungen bei offline/ungemounteten Netzlaufwerken.
- **Force-Rebuild:** Vollständiger Neuaufbau aller Sammlungen per Knopfdruck.
- **Sharing-Violation-Retries:** Automatische Wiederholungsversuche mit Backoff bei gesperrten Dateien.

---

## [1.0.16.0] - 2026-06-24
### Hinzugefügt
- **Chronologisches Erscheinungsdatum:** Automatische Berechnung des `PremiereDate` und `ProductionYear` einer Sammlung basierend auf dem ältesten enthaltenen Film für chronologische Sortierung im Web-Client.
- **Sicherheitshärtung:** Pfadvalidierung und Schutz vor Path-Traversal beim Auflösen von NFO- und Bildpfaden.
