# Server test checklist

Tick each line when it behaves as described. One click step per line; the expected result follows the arrow.
If something differs, leave it unticked and add a short note.

Already verified on 2026-11-24: ownership marker shown in Edit Metadata and kept after saving; *Delete ALL Collections & Rebuild* (110 deleted, 110 recreated, all marked); per-collection display order kept across a sync; activity log titles; display order choices in Edit Metadata.

## 0. Preparation

- [ ] Deploy the current build (`deploy.bat`) → Jellyfin restarts, settings page opens

## 1. Settings page

- [ ] Untick *Delete orphaned collections*, then tick it again → a confirmation dialog appears
- [ ] Cancel that dialog → the box stays unticked
- [ ] Tick it once more and confirm → the box stays ticked (your original setting is back)
- [ ] *Default Collection Sort By* → *Apply to all plugin collections* → confirmation, then "Sort order applied to N collection(s)"

## 2. Sort order

- [ ] Edit Metadata of one collection → Display order "Date modified" → save → **Sync Now** → still "Date modified"

## 3. Set folder offline (#4)

- [ ] Set *Movie Set Data Folder* to a path that does not exist, leave the field → **Preview Changes** → banner "Movie Set Data Folder is not reachable"
- [ ] **Sync Now** → posters of the collections unchanged; activity log entry is a warning about the set folder
- [ ] Restore the correct path → **Sync Now** → no warning

## 4. Failures shown as failures (#6)

- [ ] Optional, needs an empty test library with Mount Guard on: **Sync Now** → Dashboard → Scheduled Tasks shows *Sync Local Movie Sets* as *Failed* with the Mount Guard message
- [ ] Optional, same state: **Force Rebuild** → activity log "Force Rebuild Aborted … No collections were deleted"

Waiting for a running sync and cancelling are hard to time by hand (a sync takes ~3 s); they are covered by unit tests.

## 5. Manual collections (#1, #3)

- [ ] Create a manual collection (e.g. "Test Manual") with one movie that belongs to a set → **Sync Now** → it is not deleted (orphan deletion is on) and shows no set poster from the movie folder
- [ ] **Preview Changes** → "Test Manual" listed under kept orphans with "Not created by this plugin"
- [ ] **Force Rebuild** (plugin collections only, takes ~15 s) → "Test Manual" still exists; delete it afterwards

## 6. Movie NFO refresh

- [ ] Change the plot of one set member in tinyMediaManager (saves its NFO) → **Sync Now** → activity log: "Re-read 1 newer movie NFO(s), 1 with changes"; new plot visible in Jellyfin
- [ ] **Sync Now** again → no "Re-read" text in the activity log entry

## Not testable on this server

- #2 (movie NFOs rewritten by Jellyfin): no library has a metadata saver enabled. Would need a test library with the NFO saver turned on.
