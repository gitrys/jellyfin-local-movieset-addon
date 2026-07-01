import os
import sys
import json
import hashlib
import re
from datetime import datetime, timezone

def get_md5(file_path):
    hash_md5 = hashlib.md5()
    with open(file_path, "rb") as f:
        for chunk in iter(lambda: f.read(4096), b""):
            hash_md5.update(chunk)
    return hash_md5.hexdigest()

def get_target_abi():
    # Try parsing Jellyfin.Controller version from csproj
    csproj_path = os.path.join("Jellyfin.Plugin.LocalMovieSets", "Jellyfin.Plugin.LocalMovieSets.csproj")
    if not os.path.exists(csproj_path):
        return "10.10.0.0"
    
    try:
        with open(csproj_path, "r", encoding="utf-8") as f:
            content = f.read()
        
        # Look for Jellyfin.Controller version
        match = re.search(r'PackageReference\s+Include="Jellyfin\.Controller"\s+Version="([^"]+)"', content)
        if match:
            version_str = match.group(1)
            parts = version_str.split('.')
            if len(parts) >= 2:
                # E.g. 10.10.3 -> 10.10.0.0
                return f"{parts[0]}.{parts[1]}.0.0"
    except Exception as e:
        print(f"Warning: Could not parse target ABI from csproj: {e}")
        
    return "10.10.0.0"

def main():
    if len(sys.argv) < 5:
        print("Usage: python update_manifest.py <version> <zip_path> <repo_fullname> <release_tag>")
        sys.exit(1)
        
    version = sys.argv[1].lstrip('v') # Remove leading 'v' if present
    zip_path = sys.argv[2]
    repo_fullname = sys.argv[3]
    release_tag = sys.argv[4]
    
    owner = repo_fullname.split('/')[0]
    
    if not os.path.exists(zip_path):
        print(f"Error: ZIP file not found at {zip_path}")
        sys.exit(1)
        
    checksum = get_md5(zip_path)
    target_abi = get_target_abi()
    timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    
    # URL to download the release asset
    zip_filename = os.path.basename(zip_path)
    source_url = f"https://github.com/{repo_fullname}/releases/download/{release_tag}/{zip_filename}"
    
    manifest_path = "manifest.json"
    if os.path.exists(manifest_path):
        with open(manifest_path, "r", encoding="utf-8") as f:
            try:
                manifest = json.load(f)
            except json.JSONDecodeError:
                manifest = []
    else:
        manifest = []
        
    plugin_guid = "d3e4f5a6-b7c8-9012-def0-123456789abc"
    plugin_entry = None
    
    for entry in manifest:
        if entry.get("guid") == plugin_guid:
            plugin_entry = entry
            break
            
    if not plugin_entry:
        plugin_entry = {
            "category": "Metadata",
            "guid": plugin_guid,
            "name": "Local Movie Sets",
            "description": "Creates movie collections (box sets) from local NFO metadata produced by tinyMediaManager.",
            "owner": owner,
            "imageUrl": f"https://raw.githubusercontent.com/{repo_fullname}/main/images/local_movie_sets_logo.png",
            "overview": "A Jellyfin plugin that creates and manages movie collections (box sets) from local metadata only — no TMDB or external API calls.",
            "versions": []
        }
        manifest.append(plugin_entry)
    else:
        plugin_entry["owner"] = owner
        plugin_entry["imageUrl"] = f"https://raw.githubusercontent.com/{repo_fullname}/main/images/local_movie_sets_logo.png"
        
    # Check if this version already exists, if so update it, otherwise insert it
    version_exists = False
    new_version_info = {
        "version": version,
        "changelog": f"Release {version}",
        "targetAbi": target_abi,
        "sourceUrl": source_url,
        "checksum": checksum,
        "timestamp": timestamp
    }
    
    versions_list = plugin_entry.get("versions", [])
    for i, v in enumerate(versions_list):
        if v.get("version") == version:
            versions_list[i] = new_version_info
            version_exists = True
            break
            
    if not version_exists:
        versions_list.insert(0, new_version_info)
        
    plugin_entry["versions"] = versions_list
    
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
        
    print(f"Successfully updated manifest.json for version {version} (ABI compatibility: {target_abi})")

if __name__ == "__main__":
    main()
