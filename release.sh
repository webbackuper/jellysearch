#!/usr/bin/env bash
# Publie une version de JellySearch sur GitHub et met à jour le dépôt de plugins Jellyfin.
#
#   ./release.sh 1.0.0.0 "Première version."
#
# 1. compile (Release), lance les tests unitaires puis les tests d'intégration sur une instance Jellyfin
#    jetable (tests/integration/run-all.sh) : la release est annulée au moindre échec ;
# 2. crée artifacts/jellysearch_<version>.zip (dll + meta.json + image) et son MD5 ;
# 3. ajoute la version en tête de manifest.json (le dépôt que Jellyfin lit) ;
# 4. commite, pousse, et crée la release GitHub v<version> avec le zip.
# Nécessite gh (GitHub CLI) connecté : gh auth login.
set -euo pipefail

VERSION="${1:?usage: ./release.sh <version x.y.z.w> \"<changelog>\"}"
CHANGELOG="${2:?changelog manquant}"
REPO="webbackuper/jellysearch"
TARGET_ABI="12.4.0.0"
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"

[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "version attendue : x.y.z.w" >&2; exit 1; }
GH="$(command -v gh || echo "$HOME/.local/bin/gh")"
[ -x "$GH" ] || { echo "gh introuvable (GitHub CLI)" >&2; exit 1; }
[ -z "$(git status --porcelain)" ] || { echo "des modifications ne sont pas commitées" >&2; exit 1; }

echo "==> Version $VERSION"
# until the commit, any failure puts the version files back as they were
trap 'git checkout -- Directory.Build.props build.yaml; echo "release annulée" >&2' ERR
sed -i -E "s#<(Version|AssemblyVersion|FileVersion)>[^<]*<#<\1>$VERSION<#g" Directory.Build.props
sed -i -E "s|^version: \".*\"|version: \"$VERSION\"|" build.yaml

echo "==> Compilation et tests"
dotnet build -c Release --nologo -v q
dotnet test --nologo -v q

echo "==> Tests d'intégration (instance Jellyfin jetable)"
if [ "${JELLYSEARCH_SKIP_INTEGRATION:-0}" = 1 ]; then
    echo "    ignorés (JELLYSEARCH_SKIP_INTEGRATION=1)"
else
    tests/integration/run-all.sh
fi

echo "==> Paquet"
OUT="$HERE/artifacts"
STAGE="$OUT/stage"
ZIP="$OUT/jellysearch_$VERSION.zip"
rm -rf "$STAGE" && mkdir -p "$STAGE"
cp Jellyfin.Plugin.JellySearch/bin/Release/net10.0/Jellyfin.Plugin.JellySearch.dll "$STAGE/"
cp assets/jellysearch.png "$STAGE/"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
python3 -I - "$STAGE/meta.json" "$VERSION" "$TARGET_ABI" "$TIMESTAMP" "$CHANGELOG" <<'EOF'
import json, sys
out, version, abi, ts, changelog = sys.argv[1:6]
json.dump({
    "category": "General", "changelog": changelog,
    "description": "Recherche tolérante (accents, fautes de frappe), dans les résumés, acteurs et tags, avec des filtres combinables et des recherches enregistrées en tuiles, sur le web et la TV.",
    "guid": "8b8ecb0d-b645-48ad-a58a-2e79c932f0c7", "name": "JellySearch",
    "overview": "Recherche tolérante, filtres combinables et tuiles, sur le web et la TV",
    "owner": "webbackuper", "targetAbi": abi, "timestamp": ts, "version": version,
    "status": "Active", "autoUpdate": True, "imagePath": "jellysearch.png",
    "assemblies": ["Jellyfin.Plugin.JellySearch.dll"],
}, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
EOF
rm -f "$ZIP"
(cd "$STAGE" && python3 -I -c "import zipfile,sys; z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED); [z.write(f) for f in ('Jellyfin.Plugin.JellySearch.dll','meta.json','jellysearch.png')]; z.close()" "$ZIP")
MD5="$(md5sum "$ZIP" | cut -d' ' -f1)"
echo "    $ZIP ($MD5)"

echo "==> manifest.json"
python3 -I tools/update-manifest.py manifest.json "$VERSION" "$TARGET_ABI" "$TIMESTAMP" "$MD5" \
    "https://github.com/$REPO/releases/download/v$VERSION/jellysearch_$VERSION.zip" "$CHANGELOG"

echo "==> Commit et publication"
trap - ERR
git add Directory.Build.props build.yaml manifest.json
git commit -qm "Release $VERSION" -m "$CHANGELOG"
git tag -a "v$VERSION" -m "JellySearch $VERSION"
git push origin HEAD "v$VERSION"
"$GH" release create "v$VERSION" "$ZIP" --repo "$REPO" --title "JellySearch $VERSION" --notes "$CHANGELOG

Dépôt de plugins Jellyfin : https://raw.githubusercontent.com/$REPO/main/manifest.json"
echo "==> Terminé : https://github.com/$REPO/releases/tag/v$VERSION"
