#!/usr/bin/env python3
"""Adds (or replaces) a version in the Jellyfin plugin repository manifest.

    update-manifest.py manifest.json <version> <targetAbi> <timestamp> <md5> <sourceUrl> <changelog>

The manifest is the file Jellyfin reads when the repository URL
https://raw.githubusercontent.com/webbackuper/jellysearch/main/manifest.json is added in
Dashboard > Plugins > Repositories. Newest version first.
"""
import json
import os
import sys

GUID = "8b8ecb0d-b645-48ad-a58a-2e79c932f0c7"


def main(path, version, abi, timestamp, md5, url, changelog):
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            manifest = json.load(f)
    else:
        manifest = []

    package = next((p for p in manifest if p.get("guid") == GUID), None)
    if package is None:
        package = {"guid": GUID, "versions": []}
        manifest.append(package)

    package.update({
        "name": "JellySearch",
        "description": (
            "Remplace la recherche de Jellyfin pour les films, séries, épisodes et collections : accents et "
            "fautes de frappe ignorés, recherche dans les résumés, acteurs, réalisateurs, genres et tags, filtres "
            "combinables (note IMDb ou TMDB, année, durée, genre, MediaID, vu, favori…) avec ET, OU, NON, >, <, =. "
            "Sur la TV, filtres tapés ou dictés en phrases ; les recherches enregistrées deviennent des tuiles "
            "dans « Mes médias »."
        ),
        "overview": "Recherche tolérante, filtres combinables et tuiles, sur le web et la TV",
        "owner": "webbackuper",
        "category": "General",
        "imageUrl": "https://raw.githubusercontent.com/webbackuper/jellysearch/main/assets/jellysearch.png",
    })

    versions = [v for v in package.get("versions", []) if v.get("version") != version]
    versions.insert(0, {
        "version": version,
        "changelog": changelog,
        "targetAbi": abi,
        "sourceUrl": url,
        "checksum": md5,
        "timestamp": timestamp,
    })
    package["versions"] = versions

    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"manifest: {len(versions)} version(s), newest {version}")


if __name__ == "__main__":
    main(*sys.argv[1:8])
