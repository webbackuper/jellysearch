#!/usr/bin/env bash
# Installe (ou désinstalle) le plugin JellySearch dans le Jellyfin de cette machine.
#
#   sudo ./install.sh              installe la version compilée (dotnet build -c Release)
#   sudo ./install.sh --uninstall  retire le plugin et tout ce qu'il a enregistré (réglages,
#                                  recherches enregistrées, notes IMDb, dépôt de plugins)
#
# Le plus simple reste l'installation depuis Jellyfin : Tableau de bord > Extensions > Dépôts >
# ajouter https://raw.githubusercontent.com/webbackuper/jellysearch/main/manifest.json, puis
# Catalogue > JellySearch > Installer. JellySearch n'écrit jamais dans la base de Jellyfin.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
VERSION="$(sed -n "s:.*<Version>\(.*\)</Version>.*:\1:p" "$HERE/Directory.Build.props")"
GUID="8b8ecb0d-b645-48ad-a58a-2e79c932f0c7"
DATA_DIR="${JELLYFIN_DATA_DIR:-/var/lib/jellyfin}"
PLUGINS_DIR="$DATA_DIR/plugins"
DEST="$PLUGINS_DIR/JellySearch_$VERSION"
LOG_DIR="${JELLYFIN_LOG_DIR:-/var/log/jellyfin}"
CONFIG_DIR="${JELLYFIN_CONFIG_DIR:-/etc/jellyfin}"
CACHE_DIR="${JELLYFIN_CACHE_DIR:-/var/cache/jellyfin}"
REPO_NAME="JellySearch"
REPO_URL="https://raw.githubusercontent.com/webbackuper/jellysearch/main/manifest.json"
URL="http://127.0.0.1:8096"
DLL="$HERE/Jellyfin.Plugin.JellySearch/bin/Release/net10.0/Jellyfin.Plugin.JellySearch.dll"
IMAGE="$HERE/assets/jellysearch.png"

die() { echo "ERREUR : $*" >&2; exit 1; }
info() { echo "==> $*"; }

[ "$(id -u)" -eq 0 ] || die "à lancer avec sudo"
command -v systemctl >/dev/null || die "systemctl introuvable"
[ -d "$PLUGINS_DIR" ] || die "$PLUGINS_DIR introuvable (Jellyfin est-il installé ?)"

wait_for_jellyfin() {
    info "Attente du redémarrage de Jellyfin..."
    for _ in $(seq 1 120); do
        if curl -fsS "$URL/System/Info/Public" >/dev/null 2>&1; then
            return 0
        fi
        sleep 1
    done
    die "Jellyfin ne répond pas sur $URL (voir journalctl -u jellyfin)"
}

# repository: add / remove the JellySearch entry of system.xml (text edit, the rest is kept as it is)
repository() { # add | remove
    [ -f "$CONFIG_DIR/system.xml" ] || return 0
    python3 -I - "$1" "$CONFIG_DIR/system.xml" "$REPO_NAME" "$REPO_URL" <<'PYEOF'
import re, sys
action, path, name, url = sys.argv[1:5]
text = open(path, encoding="utf-8").read()
if action == "remove":
    new = re.sub(r"[ \t]*<RepositoryInfo>(?:(?!</RepositoryInfo>).)*webbackuper/jellysearch(?:(?!</RepositoryInfo>).)*</RepositoryInfo>[ \t]*\r?\n", "", text, flags=re.S)
    if new != text:
        open(path, "w", encoding="utf-8").write(new)
        print("==> Dépôt de plugins JellySearch retiré de system.xml")
    sys.exit(0)
if "<Url>" + url + "</Url>" in text:
    print("==> Dépôt de plugins JellySearch déjà déclaré")
    sys.exit(0)
entry = ("    <RepositoryInfo>\n      <Name>%s</Name>\n      <Url>%s</Url>\n"
         "      <Enabled>true</Enabled>\n    </RepositoryInfo>\n" % (name, url))
if re.search(r"^\s*</PluginRepositories>", text, re.M):
    text = re.sub(r"^(\s*)</PluginRepositories>", lambda m: entry + m.group(1) + "</PluginRepositories>", text, count=1, flags=re.M)
elif re.search(r"<PluginRepositories\s*/>", text):
    text = re.sub(r"<PluginRepositories\s*/>", "<PluginRepositories>\n" + entry + "  </PluginRepositories>", text, count=1)
else:
    text = text.replace("</ServerConfiguration>", "  <PluginRepositories>\n" + entry + "  </PluginRepositories>\n</ServerConfiguration>", 1)
open(path, "w", encoding="utf-8").write(text)
print("==> Dépôt de plugins JellySearch ajouté : " + url)
PYEOF
}

if [ "${1:-}" = "--uninstall" ]; then
    # the same clean-up as Jellyfin's "Désinstaller" button (UninstallCleaner), for a plugin removed by hand
    info "Arrêt de Jellyfin"
    systemctl stop jellyfin
    rm -rf "$PLUGINS_DIR"/JellySearch_*
    rm -rf "$PLUGINS_DIR/configurations/JellySearch" "$CACHE_DIR/jellysearch"
    rm -f "$PLUGINS_DIR/configurations/Jellyfin.Plugin.JellySearch.xml"
    TASK_FILE="14d3e7c0-e3c7-bd75-7e56-8be363d47c0c.js"   # "Télécharger les notes IMDb"
    rm -f "$DATA_DIR/data/ScheduledTasks/$TASK_FILE" "$CONFIG_DIR/ScheduledTasks/$TASK_FILE"
    repository remove
    info "Plugin, réglages, recherches enregistrées, notes IMDb et tâche planifiée supprimés"
    systemctl start jellyfin
    wait_for_jellyfin
    info "Terminé : Jellyfin est revenu à son état sans JellySearch (rien n'avait été écrit dans sa base)."
    exit 0
fi

[ -f "$DLL" ] || die "$DLL introuvable : compilez d'abord (dotnet build -c Release)"

info "Arrêt de Jellyfin"
systemctl stop jellyfin
repository add

info "Installation du plugin dans $DEST"
rm -rf "$PLUGINS_DIR"/JellySearch_*
install -d -o jellyfin -g jellyfin -m 755 "$DEST"
install -o jellyfin -g jellyfin -m 644 "$DLL" "$DEST/"
[ -f "$IMAGE" ] && install -o jellyfin -g jellyfin -m 644 "$IMAGE" "$DEST/jellysearch.png"
cat > "$DEST/meta.json" <<EOF
{
  "category": "General",
  "changelog": "Voir https://github.com/webbackuper/jellysearch/releases",
  "description": "Recherche tolérante (accents, fautes de frappe), dans les résumés, acteurs et tags, avec des filtres combinables et des recherches enregistrées en tuiles, sur le web et la TV.",
  "guid": "$GUID",
  "name": "JellySearch",
  "overview": "Recherche tolérante, filtres combinables et tuiles, sur le web et la TV",
  "owner": "webbackuper",
  "targetAbi": "12.4.0.0",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "$VERSION",
  "status": "Active",
  "autoUpdate": false,
  "imagePath": "jellysearch.png",
  "assemblies": ["Jellyfin.Plugin.JellySearch.dll"]
}
EOF
chown jellyfin:jellyfin "$DEST/meta.json"

info "Démarrage de Jellyfin"
systemctl start jellyfin
wait_for_jellyfin

LOG="$(ls -t "$LOG_DIR"/jellyfin*.log 2>/dev/null | head -1 || true)"
if [ -n "$LOG" ] && grep -q 'Loaded plugin: "JellySearch"' "$LOG"; then
    info "JellySearch $VERSION est chargé."
else
    echo "Attention : le chargement de JellySearch n'apparaît pas dans $LOG. Vérifiez Tableau de bord > Extensions."
fi

cat <<EOF

Installation terminée. JellySearch construit son index quelques secondes après le démarrage, puis
télécharge les notes IMDb (Tableau de bord > Tâches planifiées > JellySearch).
Réglages : Tableau de bord > Extensions > JellySearch.
Pour le retirer : sudo ./install.sh --uninstall (ou le bouton « Désinstaller » de la page du plugin).
EOF
