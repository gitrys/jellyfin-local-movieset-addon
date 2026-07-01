# Jellyfin Local Movie Sets Plugin

A Jellyfin plugin that creates and manages movie **collections (box sets)** from **local metadata only** — no TMDB or external API calls.

Data is read from:
1. **Movie `.nfo` files** — the standard Kodi `<set><name>` tag inside each movie's NFO
2. **TMM Set Data Folder** — the dedicated folder configured in tinyMediaManager for set-level NFO files, posters, and fanart

---

## Requirements

| Component | Version |
|---|---|
| Jellyfin Server | 10.10.x |
| .NET SDK | 9.0+ |
| tinyMediaManager | 4.x (optional, for set folder) |

---

## Building

```powershell
cd Jellyfin.Plugin.LocalMovieSets

# Restore dependencies
dotnet restore

# Build (Debug)
dotnet build

# Build (Release)
dotnet publish -c Release -o ../dist
```

The compiled plugin is a single DLL: `Jellyfin.Plugin.LocalMovieSets.dll`

---

## Installation

1. Build the plugin (see above)
2. Copy `Jellyfin.Plugin.LocalMovieSets.dll` to your Jellyfin plugins directory:
   - **Linux:** `~/.local/share/jellyfin/plugins/LocalMovieSets_1.0.0.0/`
   - **Windows:** `%APPDATA%\Jellyfin\plugins\LocalMovieSets_1.0.0.0\`
   - **Docker:** mount into `/config/data/plugins/LocalMovieSets_1.0.0.0/`
3. Restart Jellyfin

---

## Configuration

Navigate to **Dashboard → Plugins → Local Movie Sets** to configure:

| Setting | Description |
|---|---|
| **Movie Set Data Folder** | Path to tinyMediaManager's "Movie Set Data Folder". Leave blank to only use `<set>` tags from individual movie NFOs. |
| **NFO File Naming** | Must match your TMM setting: `SetSubfolder` (default), `FlatFile`, or `collection.nfo` |
| **Minimum Movies** | Minimum number of movies in your library needed to create a collection (default: 1) |
| **Delete Orphaned Sets** | Remove collections that no longer have any matching NFO data (only deletes collections without TMDB/IMDB IDs) |
| **Overwrite Existing Artwork** | Re-copy artwork from the TMM set folder on every sync |

> **Note on renaming sets:** if you rename a set in your NFOs, a new collection is created under the new name, but the old collection is only removed automatically when **Delete Orphaned Sets** is enabled. Otherwise the movies remain members of both collections until you delete the old one manually.

---

## How It Works

### Movie NFO format (required)

Each movie that belongs to a set must have a `<set>` tag in its `.nfo` file:

```xml
<!-- Iron Man (2008)/Iron Man (2008).nfo -->
<movie>
  <title>Iron Man</title>
  <set>
    <name>Iron Man Collection</name>
    <overview>The Iron Man trilogy...</overview>
  </set>
</movie>
```

tinyMediaManager writes this automatically when you assign a movie to a set.

### Dedicated Set Folder (optional, recommended)

If you configure a Movie Set Data Folder in both TMM and this plugin, richer metadata and artwork is read from it:

```
/moviesets/
  Iron Man Collection/
    Iron Man Collection.nfo     ← set overview, title
    poster.jpg                   ← collection poster
    fanart.jpg                   ← collection backdrop
    logo.png                     ← optional logo
    landscape.jpg                ← optional landscape/thumb
```

The NFO file format:
```xml
<set>
  <title>Iron Man Collection</title>
  <originaltitle>Iron Man Collection</originaltitle>
  <overview>The complete Iron Man film series.</overview>
</set>
```

### Supported artwork filenames

| Image Type | Filenames tried (in order) |
|---|---|
| Poster (Primary) | `poster.jpg`, `poster.png`, `folder.jpg`, `folder.png` |
| Backdrop (Fanart) | `fanart.jpg`, `fanart.png`, `backdrop.jpg`, `backdrop.png` |
| Logo | `logo.png`, `logo.jpg`, `clearlogo.png` |
| Thumb/Landscape | `landscape.jpg`, `landscape.png`, `thumb.jpg` |
| ClearArt | `clearart.png`, `clearart.jpg` |
| Banner | `banner.jpg`, `banner.png` |
| Disc | `disc.png`, `disc.jpg`, `discart.png` |

---

## Important: Disable TMDB Auto-Collections

To avoid conflicts with Jellyfin's built-in TMDB collection feature:

1. Go to **Dashboard → Libraries → [Your Movie Library] → Manage Library**
2. **Disable** "Automatically add to collection"
3. Save

---

## Syncing

Collections are synced:
- **Automatically** — 30 seconds after a movie is added or updated in the library
- **On schedule** — every 24 hours (configurable in Dashboard → Scheduled Tasks)
- **Manually** — click "Sync Now" on the plugin config page, or trigger the "Sync Local Movie Sets" scheduled task

---

## NFO Naming Conventions

Match this setting to your tinyMediaManager configuration:

| Plugin Setting | TMM Setting | File Path |
|---|---|---|
| `Set Subfolder` *(default)* | `<movieset>/<movieset>.nfo` | `MovieSets/Iron Man Collection/Iron Man Collection.nfo` |
| `Flat File` | `<movieset>.nfo` | `MovieSets/Iron Man Collection.nfo` |
| `collection.nfo` | `<movieset>/collection.nfo` | `MovieSets/Iron Man Collection/collection.nfo` |

---

## Upgrading Jellyfin

If you upgrade Jellyfin, update the `Version` attribute in `Jellyfin.Plugin.LocalMovieSets.csproj` to match your new server version and rebuild.

---

## License

MIT License — see [LICENSE](LICENSE) for details.
