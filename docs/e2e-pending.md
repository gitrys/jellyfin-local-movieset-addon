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
