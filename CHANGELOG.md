# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]
### Added
- **Theme videos from the set folder:** `theme.mp4` (also `.mkv`, `.webm`, `.m4v`, `.mov`) or `{Folder}-theme.ext` / `{Folder}.theme.ext` is copied into the collection's `backdrops` folder, where Jellyfin plays it as the collection backdrop (enable *Theme videos* in the display settings). The setup guide warns when that setting is off in the current browser, and the validator reports found theme videos.

### Changed
- **Theme copies follow the set folder.** When a theme song or theme video is removed from the set folder, its copy in the Jellyfin collection folder is removed on the next sync. Before, a copied `theme.mp3` kept playing. Copies are kept while the Movie Set Data Folder is not configured or not reachable, and manual collections are never touched.
- **NFO/artwork validation follows the sync's rules.** It checks the set NFO name of the configured naming convention (`<folder>.nfo` or `collection.nfo`) and reports other NFO names as an error, reports NFOs in the root of the Movie Set Data Folder and nested set folders (the sync reads neither), checks artwork and theme files by the names the sync uses (`movieset-poster.jpg`, `cover.jpg` or a different letter case no longer count as a poster; prefixed theme files are found), and checks artwork in set folders without an NFO. It no longer walks folders below the first level. A poster that exists but is not used is now a warning instead of an info.
- **Bug report placeholders:** masked names now read `[Set n]` and `[Movie n]` (same name, same number across the whole report) instead of `[Collection #n]`; library folders read `[Library n]` and the set data folder `[Set Data Folder]`.
- **Mount Guard also checks mixed libraries** (type "mixed" or no type), because the sync reads movies from them too.
- **Duplicate collection names:** among collections with the same name, the plugin manages the marked one, then the oldest, then the one with the lowest ID, instead of whichever Jellyfin returned first.

### Fixed
- **Movies added or removed during a running sync are no longer missed.** Library events were ignored while a sync ran, so such movies waited for the next library event or the daily task. The automatic sync now runs again after the current one. Changes to existing movies during a running sync are still ignored, because most of them are the plugin's own saves.
- **Movies whose file is not found keep their collection.** When a share or a mount point below a library root was offline, the missing NFO looked like "not in a set" and the movie was removed from its collection. Such movies now stay, and the sync log reports how many files were not found.
- **Old theme copies are replaced.** When the theme in the set folder changed format (e.g. `theme.mp3` to `theme.flac`), the old copy stayed in the collection folder next to the new one.
- **Stacked movie files** (`Alien-cd1.mkv`) also find an NFO named without the part marker (`Alien.nfo`).
- **Bug report masking left paths and titles visible.** Paths with spaces, non-ASCII characters or UNC form were not (or only partly) masked; validator messages and suggestions, the reason of protected collections and movie titles in log lines were never masked; title masking only caught ASCII names ending in "Collection", "Movie" or "Set". The finished report is now masked by the plugin on your Jellyfin server, using every library path, collection, set folder and movie name it knows. While masking runs, or if it fails, the unmasked report is neither shown nor copyable.
- **A movie NFO that cannot be read no longer removes the movie from its collection.** Before, a file that was being written by the media manager during a sync dropped the movie from its collection until the next sync; a collection whose members all had unreadable NFOs could even count as orphaned. The movie now stays in its current plugin collection and the sync log names it. A *Force Rebuild* does not restore such a movie until its NFO is readable again.
- **Collections with a set sort title are no longer saved on every sync.** The sort title from the set NFO was compared with Jellyfin's normalized sort name, so it always looked changed.
- **Possible crash during server shutdown:** the automatic sync timer could throw on a background thread if it fired while the plugin was being disposed.
- **Console error when leaving the settings page:** the page called a status poll function that no longer exists.
- **Documentation:** the README now describes the collection release year correctly (oldest member movie only).

## [1.0.37.0] - 2026-11-24
### Added
- **Collection ownership marker:** Collections the plugin manages get the external ID *Local Movie Sets* (value: set name), shown in Edit Metadata. Existing plugin collections are marked on the first sync after the update.
- **Orphan cleanup limit:** New setting (default 25 %). If one sync would delete more than this share of the plugin's collections (and at least 5), it deletes nothing and logs a warning to the sync history and activity log. The Preview shows when the limit would apply.
- **Delete ALL Collections & Rebuild:** Separate button with the old Force Rebuild behavior (deletes every collection in Jellyfin).
- **Apply to all plugin collections:** Button under *Default Collection Sort By* that sets the chosen order for every collection the plugin created.
- **Confirmation for orphan deletion:** Turning on *Delete orphaned collections* asks for confirmation before it is saved.

### Changed
- **Orphan deletion only removes the plugin's own collections.** Before, it deleted every collection without a TMDB/IMDb ID whose name was not in a movie NFO, including manual collections, while the plugin's own collections were kept because they carry the set NFO's TMDB ID. Collections that were already orphaned before this update have no marker and are never deleted automatically.
- **Force Rebuild only deletes the plugin's own collections.** Manual and TMDB collections stay. Use *Delete ALL Collections & Rebuild* for the old behavior.
- **Set artwork only for the plugin's own collections.** The image provider no longer supplies set or movie-folder artwork to manual or TMDB collections.
- **Duplicate collection names:** when several collections share a name, the one with the ownership marker is managed.
- **Genres and studios without a readable set NFO are kept.** With aggregation off, they are only cleared when the set NFO exists and lists none. A missing or unreadable NFO, or an unreachable Movie Set Data Folder, no longer clears them. As a consequence, deleting a set NFO or turning aggregation off keeps the previous values.
- **Movie Set Data Folder path saves when you leave the field**, not while typing, so a half-typed path is not used by a sync.
- **Default Collection Sort By only applies to new collections.** An order you choose for a single collection in Edit Metadata is kept. The default is now *Release Date*; collections with an empty display order (written by the removed *Creation Order* setting) get the default order on the next sync.
- **Movie NFO refresh on sync:** Sync and Force Rebuild re-read a set member's movie NFO through Jellyfin when that file is newer than the stored library item, so later edits (age rating, plot, year, cast, genres, rating) reach the collection. Set membership was already read on every run. Movie images are left in place. A movie is only re-read when its NFO is newer than both Jellyfin's last save and last refresh, so an NFO that was touched without new content is not re-read on every sync. The sync log and activity log show how many re-read NFOs brought changes.

### Removed
- **"Also delete orphaned collections with provider IDs":** no longer needed, because orphan deletion now relies on the ownership marker instead of TMDB/IMDb IDs. A saved value of this option is ignored.
- **"Creation Order (Release Date)" and "Default Collection Sort Order":** The option wrote an empty display order, which Jellyfin sorts by release date, so it never produced a creation order, and *Sort Order* (ascending/descending) had no visible effect. The plugin now only offers Jellyfin's *Release Date* and *Sort Name* orders (always ascending). Saved values of the removed options are mapped to *Release Date* or ignored. An unsorted collection in insertion order is still possible per collection in Edit Metadata (Jellyfin labels it "Date modified").
- **Set-folder guess for managed collections:** a collection whose name only matched a set folder (without being a set in any movie NFO) is no longer treated as managed.

### Fixed
- **Sort order per collection is no longer reset by every sync.** Before, each sync set every plugin collection back to the global sort setting, although the help text suggested changing it per collection.
- **Failures are no longer reported as success.**
  - The scheduled task *Sync Local Movie Sets* (also used by **Sync Now**) now shows as failed when the sync fails or the Mount Guard aborts it, and as cancelled when it is cancelled. Before, it always showed "Completed".
  - If a sync is already running, the task waits for it and then runs its own sync instead of silently skipping.
  - Force Rebuild writes every outcome to the sync history and the activity log, including a Mount Guard abort before anything is deleted (before, this only appeared in the server log). Collections that cannot be deleted are counted and reported as a warning.
- **Unreachable Movie Set Data Folder no longer wipes collection data.** Before, a short share outage cleared genres and studios of every collection (with aggregation off), and the movie-folder artwork fallback could replace set artwork. Now set metadata and artwork are left unchanged; the sync still updates members and warns in the sync history, the activity log, and the Preview.
- **Movie NFO files are no longer rewritten by a sync.** After adding a movie to a collection or removing it, the plugin saved the movie as a manual metadata edit. With Jellyfin's NFO saver enabled (and in some cases even without it), Jellyfin then rewrote the movie's NFO in its own format and dropped `<set><overview>`. The plugin now saves these movies as a metadata import, which does not run Jellyfin's metadata savers.

## [1.0.36.0] - 2026-10-01
### Added
- **Settings autosave:** Settings-tab changes save after a short pause (about 250 ms for checkboxes and dropdowns, about 900 ms for text and number fields; leaving a field saves immediately). **Save Settings** still saves at once. Confirmation uses the floating toast so the page does not shift.
- **Live System Info & Bug Report:** Opening the dialog runs a dry-run Preview, NFO/artwork validation, and health checks (auto-add, image fetchers, grouping) instead of reusing only the last sync snapshot.
- **Theme songs link:** The Setup Guide link opens Display preferences for the current user (`userId`). If that id is unavailable, it falls back to the user settings menu.

### Changed
- **Settings help text:** Shortened field descriptions on the Settings tab.
- **Validate query parameter:** Diagnostics export passes `folderPath` so an unsaved Set Data Folder input is validated.
- **Bug report template:** Path masking is on by default; title masking is optional and pattern-limited.
- **README:** Screenshots match the current three-tab UI. Mount Guard and media-file FAQ match actual behavior (movie-library roots only; theme audio is copied into the Jellyfin collection folder).

### Removed
- **Inline status banner:** Save and sync messages no longer insert a bar that pushes the page down.
- **Dashboard & History tab / Insights widget:** Sync progress stays in Jellyfin's activity log and Scheduled Tasks. Diagnostics export still includes last-run metrics when available.
- **Dead status poll after Force Rebuild:** Removed a leftover poll call for a function that no longer exists.

---

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
