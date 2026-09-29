# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
