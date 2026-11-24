# Pending E2E checks

Verified on the server on 2026-11-24 and removed from this list: ownership marker shown in Edit Metadata and kept after saving there; *Delete ALL Collections & Rebuild* (110 deleted, 110 recreated, all marked); per-collection display order kept across a sync; activity log titles for sync and rebuild; display order choices in Edit Metadata ("Date modified" = `Default`, "Sort name", "Release date").

## 2026-11-24 – Collection ownership marker

Feature: plugin-owned collections (external ID `Local Movie Sets`), orphan cleanup limit, two Force Rebuild variants.

Affected flows:

1. A manual collection without artwork gets no set poster from a member's movie folder.
2. Turning on *Delete orphaned collections* asks for confirmation; cancelling leaves it off and unsaved. The Preview lists manual collections under "Orphaned Collections Kept" with "Not created by this plugin".
3. Orphan limit: with more than 25 % of plugin collections orphaned (at least 5), the sync deletes nothing and the activity log shows the warning.
4. *Force Rebuild* (plugin collections only) keeps a manual collection.

## 2026-11-24 – No movie NFO writes on sync (#2)

Feature: movies are saved with `MetadataImport` after membership changes, so Jellyfin's NFO saver does not run.

Not reproducible on the current server: no library has a metadata saver enabled. Needs a test library with Jellyfin's NFO saver enabled under Manage Library → Metadata savers:

1. Note modification time and content of a set member's NFO (for example `<set><overview>` present).
2. Remove the movie from its collection in Jellyfin, then run **Sync Now**.
3. Expected: the movie is back in the collection, grouped under it in the Movies view, and its NFO has the same modification time and content.

## 2026-11-24 – Unreachable set folder keeps collection data (#4)

Feature: set metadata and artwork stay unchanged when the Movie Set Data Folder cannot be reached or a set NFO is missing or broken.

Affected flows:

1. Set the Movie Set Data Folder to a path that does not exist, leave the field, run **Sync Now**.
2. Expected: images of the collections are unchanged (genres/studios too, with aggregation off); the activity log and the Preview show the "not reachable" warning; member changes still apply.
3. Restore the path and sync again: no warning, everything as before.
4. Typing in the path field does not save; leaving the field saves it (status line shows "Saved.").

## 2026-11-24 – Failures reported correctly (#6)

Feature: scheduled task status and Force Rebuild outcomes.

Affected flows:

1. Mount Guard on, one movie library path offline (or an empty test folder as a library) → **Sync Now** → Dashboard → Scheduled Tasks shows *Sync Local Movie Sets* as failed with the Mount Guard message; the activity log shows "Local Movie Sets Sync Aborted".
2. Same state → **Force Rebuild** → activity log shows "Local Movie Sets Force Rebuild Aborted … No collections were deleted"; no collection is deleted.
3. While a sync is running (e.g. right after a library scan), press **Sync Now** → the task waits, then runs and shows *Completed*.
4. Cancel the task in Scheduled Tasks while it runs → shown as cancelled.

## 2026-11-24 – Collection sort order (#12)

Feature: per-collection order is kept; Creation Order removed; "Apply to all plugin collections" button.

Affected flows:

1. Settings: Default Collection Sort By = Sort Name → **Apply to all plugin collections** → confirmation, then a message with the number of changed collections; all plugin collections sorted by name; manual collections unchanged.
2. Same button while a sync is running → error message "try again".
3. A collection set to "Date modified" in Edit Metadata stays on it after a sync.

## 2026-11-24 – Movie NFO refresh: no repeated re-reads

Feature: a movie is only re-read when its NFO is newer than both Jellyfin's last save and last refresh; the log reports how many re-read NFOs brought changes.

Affected flows:

1. Change the plot of one set member in tinyMediaManager → **Sync Now** → activity log: "Re-read 1 newer movie NFO(s), 1 with changes"; new plot visible in Jellyfin.
2. **Sync Now** again → no "Re-read" text in the activity log entry.

## 2026-11-24 – Unreadable movie NFO keeps the movie in its collection

Feature: a movie whose NFO cannot be parsed stays in its current plugin collection; collections with a set `<sorttitle>` are not saved again on every sync.

Affected flows:

1. Break the XML of one set member's NFO (for example delete the closing `</movie>`), then run **Sync Now**.
2. Expected: the movie is still in its collection; the activity/sync log shows "Kept 1 movie(s) in their current collection because their NFO could not be read: <title>".
3. Repair the NFO and sync again: no "Kept" message, membership unchanged.
4. A collection whose set NFO has a `<sorttitle>`: after a sync, Edit Metadata shows that sort title, and changing it in the set NFO plus **Sync Now** applies the new value. (The removed extra save on every sync is not visible in the UI.)

## 2026-11-24 – Bug report masking on the server (#5)

Feature: the System Info / bug report dialog sends the finished report to `POST LocalMovieSets/MaskReport`; paths and names are masked with what the server knows.

Affected flows:

1. Diagnostics & Tools → bug report, *Mask File Paths* on: no library path, set folder path or user name visible; library folders appear as `[Library n]`, the set folder as `[Set Data Folder]`, other paths as `…/<last part>`.
2. Also turn on *Mask Set & Movie Titles*: collection names in the Preview list, validator lines and log lines appear as `[Set n]`, movie titles as `[Movie n]`; the same name has the same number everywhere.
3. While toggling, the text area briefly shows "Masking report…" and *Copy to Clipboard* is disabled; afterwards copy works and copies the masked text.
4. Both mask options off: the report appears unmasked right away, without the "Masking report…" step.
5. Optional (fail closed): stop the server or block the request in the browser devtools, then toggle a mask option → "Masking failed. The unmasked report is not shown." and copy stays disabled.
