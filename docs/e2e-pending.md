# Pending E2E checks

## 2026-11-24 – Collection ownership marker

Feature: plugin-owned collections (external ID `Local Movie Sets`), orphan cleanup limit, two Force Rebuild variants.

Affected flows:

1. After the first sync, a plugin collection shows the external ID *Local Movie Sets* with its set name in Edit Metadata. Saving the collection in Edit Metadata keeps the ID.
2. A manual collection without artwork gets no set poster from a member's movie folder.
3. Turning on *Delete orphaned collections* asks for confirmation; cancelling leaves it off and unsaved. The Preview lists manual collections under "Orphaned Collections Kept" with "Not created by this plugin".
4. Orphan limit: with more than 25 % of plugin collections orphaned (at least 5), the sync deletes nothing and the activity log shows the warning.
5. *Force Rebuild* keeps manual collections. *Delete ALL Collections & Rebuild* removes them and rebuilds the plugin collections.

## 2026-11-24 – No movie NFO writes on sync (#2)

Feature: movies are saved with `MetadataImport` after membership changes, so Jellyfin's NFO saver does not run.

Affected flow (library with Jellyfin's NFO saver enabled under Manage Library → Metadata savers):

1. Note modification time and content of a set member's NFO (for example `<set><overview>` present).
2. Remove the movie from its collection in Jellyfin, then run **Sync Now**.
3. Expected: the movie is back in the collection, grouped under it in the Movies view, and its NFO has the same modification time and content.

## 2026-11-24 – Unreachable set folder keeps collection data (#4)

Feature: set metadata and artwork stay unchanged when the Movie Set Data Folder cannot be reached or a set NFO is missing or broken.

Affected flows (genre/studio aggregation off):

1. Set the Movie Set Data Folder to a path that does not exist, leave the field, run **Sync Now**.
2. Expected: genres, studios, and images of the collections are unchanged; the activity log and the Preview show the "not reachable" warning; member changes still apply.
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

1. Set one plugin collection to "Sort name" in Edit Metadata → **Sync Now** → it stays "Sort name".
2. A collection that used Creation Order → after a sync it is sorted by release date; an older movie added later appears in the right place.
3. Check the display order choices in Jellyfin's Edit Metadata for a collection (expected: only sort name and release date; an empty choice would contradict an assumption of this change).
4. Settings: Default Collection Sort By = Sort Name → **Apply to all plugin collections** → confirmation, then a message with the number of changed collections; all plugin collections sorted by name; manual collections unchanged.
5. Same button while a sync is running → error message "try again".
