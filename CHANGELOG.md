# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.0.35.0] - 2026-09-30
### Added
- **Interactive Dry-Run Sync Preview:** Comprehensive redesign of the preview tool in the Diagnostics tab:
  - Scanned movies and library summary metrics (`ScannedMoviesCount`, `ScannedLibrariesCount`, `TotalSetsCount`).
  - Color-coded KPI badges (`Up to Date`, `To Create`, `To Update`, `Below Min`, `To Delete`).
  - Movie-level diff display (`+ Added Title (Year)`, `− Removed Title (Year)`) for updating collections.
  - Collapsible detail list of all currently up-to-date collections with member movie counts.
  - One-click `[ ▶ Apply Changes Now (Sync) ]` trigger button directly beneath preview results when changes are pending.
- **Clarified NFO File Naming Conventions:** Replaced ambiguous dropdown options with explicit, self-explanatory labels distinguishing directory structure, filename format, and media manager compatibility (tinyMediaManager vs. MediaElch/Kodi vs. Flat).
- **Persistent Diagnostics Modal Settings:** Interactive toggles (Word Wrap, Mask Paths, Mask Titles, Include Errors) are now persisted in `localStorage` across page reloads and sessions.
- **Symmetrical 2x3 Metadata Aggregation Layout:** Clean, responsive grid layout for General Metadata (Tags, Genres, Studios) and Cast & Crew (Actors, Directors, Writers).
- **Pixel-Perfect Checkbox & Text Alignment:** Vertical baseline centering of Jellyfin native checkboxes and labels.
- **Unit Test Coverage Expansion:** Added unit test suite for `SyncPreviewResult` and `PreviewUpdateInfo` serialization (75 tests passing with 0 warnings).

### Changed
- **Streamlined Configuration Tabs:** Consolidated into three focused tabs: ⚙️ Settings, 🩺 Diagnostics & Tools, and 💡 Setup Guide.
- **Default Limit Values:** Set default limit inputs to `0` (Unbegrenzt / All) across all metadata aggregation fields.

---

## [1.0.34.0] - 2026-09-30
### Added
- **Collection Sort Title Support (`<sorttitle>`):** Dedicated set NFOs now support `<sorttitle>` (e.g. from tinyMediaManager, MediaElch, or custom XML), allowing precise, custom alphabetical positioning of collections in Jellyfin (`collection.SortName` / `collection.ForcedSortName`).
- **Jellyfin Server Activity Feed Integration (`IActivityManager`):** Sync completions, metrics (+created, ~updated, -deleted), duration, cancellations, and Mount Guard warnings are now logged directly to Jellyfin's server activity log.
- **Theme Music Synchronization (`theme.mp3`):** Automatic detection and synchronization of theme music (`theme.mp3`, `theme.m4a`, `theme.flac`, `theme.ogg`, `theme.wav`) from set data folders or movie folder fallbacks into Jellyfin's native collection directories, enabling immersive background theme song playback in Jellyfin Web and TV clients.
- **Theme Music Validator Support:** Local NFO & Artwork Validator now detects theme audio files and reports their status as informational diagnostics.
- **Collection Insights Dashboard Widget:** Modern interactive highlights card on the **Dashboard & History** tab showcasing the library's largest franchise, oldest franchise premiere, newest franchise release, highest-rated franchise, and average movies per set.
- **Unit Test Coverage:** Added unit test suites for sort title parsing, theme song path resolution, Jellyfin activity logging, and collection insights calculation (74 tests passing with 0 warnings).

---

## [1.0.33.0] - 2026-09-30
### Added
- **Universal Media Manager Compatibility:** Generalized all references from tinyMediaManager (TMM) to universal media managers (e.g. tinyMediaManager, MediaElch, Ember, Kodi, custom NFOs) across the entire plugin, UI, setup guide, and documentation.
- **Frequency-Ranked Metadata Aggregation:** Intelligent frequency counting across all member movies so franchise-defining elements always appear first:
  - **Tags:** Ranked by occurrences across movies, with configurable limit (`MaxTags`, default: 15, `0 = All`).
  - **Cast & Crew:** Actors ranked by franchise appearances across movies (`MaxActors`, default: 20, `0 = All`); directors and writers ranked by total films directed/written (`MaxDirectors`, `MaxWriters`, default: `0 = All`).
  - **Genres & Studios Fallback Inheritance:** When a set NFO has no genres or studios, dominant genres and recurring studios are inherited from member movies and ranked by frequency (`AggregateGenres`, `AggregateStudios`, `MaxGenres`, `MaxStudios`).
- **Tactile UI Save & Status Feedback:** Multi-channel visual feedback when saving settings — animated button states (`Saving…` → `✓ Saved!`), inline badge (`✅ Settings saved successfully.`), and a floating bottom-center toast visible regardless of scroll position.
- **Unit Test Suite Expansion:** Added comprehensive tests for string/person frequency ranking, tie-breaking, unlimited limits, and cast weighting.

---

## [1.0.32.0] - 2026-09-30
### Added
- **Modern Tabbed Interface:** Redesigned plugin configuration page with fixed layout navigation (`Dashboard & History`, `Settings`, `Diagnostics & Tools`, `Setup Guide`) eliminating layout shifts.
- **Local NFO & Artwork Validator:** Built-in scanner for set folders with an isolated scrollable report showing XML issues and missing artwork.
- **Selective Movie Library Scanning:** Configurable library filter with live path previews to choose which movie libraries are scanned.
- **Automatic Library Change Sync:** Background event hook to automatically resync movie sets after library scans or updates (with 30s debounce).
- **Persistent Metrics & Sync History:** Sync metrics, run duration, counters, and execution logs now reliably survive Jellyfin server restarts and reboots.
- **Privacy-Safe Diagnostics Export:** Single-click bug report modal with interactive anonymization toggles (mask file paths, mask titles) and direct GitHub Issues link.
- **Docker & Volume Permissions Validator:** In-page path validator with proactive guidance for Docker volume mounts and PUID/PGID access permissions.
- **Sponsorship Support:** Added Buy Me a Coffee support badge and links in README and configuration footer.

### Changed
- **Artwork Diagnostics:** Downgraded missing fanart from Warning to Info so movie sets without backdrops are counted as 100% healthy.
- **Complete English Localization:** All UI cards, buttons, tabs, diagnostics, and setup guides unified to professional English.

---

## [1.0.31.0] - 2026-09-29
### Added
- **Jellyfin 12 & .NET 10 Support:** Upgraded target framework to `net10.0` and dependencies to Jellyfin 12.0.0 (`targetAbi: 12.0.0.0`).
- **Relational Database Fallback (`LinkedChildren`):** Implemented `GetBoxSetMovies` to handle Jellyfin 12 lazy loading by querying collection members directly from the relational SQLite database when needed.

### Changed
- **Scheduled Tasks:** Migrated interval trigger from legacy string `TriggerInterval` to enum `TaskTriggerInfoType.IntervalTrigger`.
- **CI/CD:** Upgraded GitHub Actions workflows (`ci.yml` and `release.yml`) to .NET 10 SDK (`10.0.x`).

### Removed
- **Cleaned Up Reflection:** Removed all reflection workarounds for `UpdatePeopleAsync`, `UpdatePeople`, and `GetPeople` (now using native `ILibraryManager` methods).

---

## [1.0.30.0] - 2026-07-02
### Fixed
- **Provider Pipeline Refactoring:** Applied collection metadata and artwork via Jellyfin's provider pipeline (`BoxSetMetadataProvider` & `BoxSetImageProvider`) to serialize writes and avoid `collection.xml` race conditions.
- **Artwork Stream & Folder Matching:** Artwork is now returned as a stream and matches alternative tinyMediaManager folder naming conventions.
- **Stability:** Prevented `NullReferenceException` on nameless dummy items and improved library root enumeration.
- **Cross-Platform:** Sanitized set folder names with a cross-platform invalid character set for Linux and Windows compatibility.

---

## [1.0.23.0] - 2026-07-02
### Added
- **Dry-Run / Preview Mode:** Compute the next sync outcome without modifying any files or database records directly in the dashboard UI.
- **Sync Status & Metrics:** Added status tracking and execution metrics to the plugin configuration page.

### Fixed
- **Orphaned Folders:** Removed physical on-disk `[boxset]` folders when collections are deleted to prevent stale folder collisions.
- **NFO Parser:** Improved XML error handling and resilience against incomplete tinyMediaManager NFO files.

---

## [1.0.19.0] - 2026-06-24
### Added
- **Metadata Aggregation:** Aggregate community ratings, tags, directors, writers, and cast from member movies onto the collection.
- **API Compatibility:** Dynamic method resolution for `UpdatePeopleAsync` and `GetPeople` across Jellyfin 10.x versions.

---

## [1.0.18.0] - 2026-06-24
### Added
- **Mount Guard:** Protects collections against accidental deletion when network shares or media drives are offline or unmounted.
- **Force Rebuild:** Option to fully rebuild all collections on demand.
- **Sharing Violation Retries:** Added exponential backoff retry logic for temporary file locks.

---

## [1.0.16.0] - 2026-06-24
### Added
- **Chronological Release Date:** Automatically calculate collection `PremiereDate` and `ProductionYear` based on the oldest member movie for chronological sorting.
- **Security Hardening:** Path validation and traversal protection when resolving NFO and artwork files.
