# Pending E2E checks

## 2026-11-24 – Collection ownership marker

Feature: plugin-owned collections (external ID `Local Movie Sets`), orphan cleanup limit, two Force Rebuild variants.

Affected flows:

1. After the first sync, a plugin collection shows the external ID *Local Movie Sets* with its set name in Edit Metadata. Saving the collection in Edit Metadata keeps the ID.
2. A manual collection without artwork gets no set poster from a member's movie folder.
3. Turning on *Delete orphaned collections* asks for confirmation; cancelling leaves it off and unsaved. The Preview lists manual collections under "Orphaned Collections Kept" with "Not created by this plugin".
4. Orphan limit: with more than 25 % of plugin collections orphaned (at least 5), the sync deletes nothing and the activity log shows the warning.
5. *Force Rebuild* keeps manual collections. *Delete ALL Collections & Rebuild* removes them and rebuilds the plugin collections.
