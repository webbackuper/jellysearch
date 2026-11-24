#!/usr/bin/env bash
# Builds a small test library with complete Kodi NFO files (genres, people, ratings, tags, IMDb ids):
#   <dir>/MOVIES03/MOVIES03/*.mkv   (a drive that the tests "unplug")
#   <dir>/MOVIES05/MOVIES05/*.mkv   (a drive that stays plugged in)
#   <dir>/SERIES/Breaking Bad/…     (a series with one episode)
# and <dir>/../imdb/title.ratings.tsv.gz, a small copy of IMDb's ratings file for the IMDb filters.
set -eu
DIR=${1:?usage: make-media.sh <dir>}
FFMPEG=${FFMPEG:-/usr/lib/jellyfin-ffmpeg/ffmpeg}
mkdir -p "$DIR/MOVIES03/MOVIES03" "$DIR/MOVIES05/MOVIES05" "$DIR/SERIES/Breaking Bad/Season 1"

video() { # <output>
    "$FFMPEG" -hide_banner -loglevel error -y \
        -f lavfi -i "testsrc2=size=320x180:rate=25:duration=6" \
        -f lavfi -i "sine=frequency=440:duration=6" \
        -map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest "$1"
}

# movie <folder> <file name> <title> <original> <year> <tmdb rating> <imdb id> <mpaa> <genres a|b> <tags a|b> <director> <actors a|b> <plot>
movie() {
    video "$1/$2.mkv"
    python3 -I - "$1/$2.nfo" "${@:3}" <<'EOF'
import sys
from xml.sax.saxutils import escape as e
out, title, orig, year, rating, imdb, mpaa, genres, tags, director, actors, plot = sys.argv[1:13]
lines = ['<?xml version="1.0" encoding="utf-8" standalone="yes"?>', "<movie>",
         f"  <title>{e(title)}</title>", f"  <originaltitle>{e(orig)}</originaltitle>",
         f"  <plot>{e(plot)}</plot>", f"  <year>{year}</year>", f"  <mpaa>{e(mpaa)}</mpaa>",
         "  <ratings>", f'    <rating name="themoviedb" max="10" default="true"><value>{rating}</value><votes>1000</votes></rating>', "  </ratings>",
         f'  <uniqueid type="imdb">{imdb}</uniqueid>', f"  <director>{e(director)}</director>"]
lines += [f"  <genre>{e(g)}</genre>" for g in genres.split("|") if g]
lines += [f"  <tag>{e(t)}</tag>" for t in tags.split("|") if t]
lines += [f"  <actor><name>{e(a)}</name><type>Actor</type></actor>" for a in actors.split("|") if a]
lines.append("</movie>")
open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
EOF
}

M3="$DIR/MOVIES03/MOVIES03"
M5="$DIR/MOVIES05/MOVIES05"
movie "$M3" "Alien (1979)" "Alien, le huitième passager" "Alien" 1979 8.1 tt0078748 "FR-12" "Horreur|Science-Fiction" "espace|extraterrestre" "Ridley Scott" "Sigourney Weaver|Tom Skerritt" "Le vaisseau spatial Nostromo recueille une créature hostile."
movie "$M3" "Aliens (1986)" "Aliens, le retour" "Aliens" 1986 7.9 tt0090605 "FR-12" "Action|Science-Fiction" "espace" "James Cameron" "Sigourney Weaver|Michael Biehn" "Ripley retourne sur la planète des aliens avec des marines."
movie "$M5" "Amelie (2001)" "Le Fabuleux Destin d'Amélie Poulain" "Le Fabuleux Destin d'Amélie Poulain" 2001 7.9 tt0211915 "FR-TP" "Comédie|Romance" "paris|montmartre" "Jean-Pierre Jeunet" "Audrey Tautou|Mathieu Kassovitz" "Amélie, une serveuse de Montmartre, décide de faire le bonheur des autres."
movie "$M5" "Danse avec les loups (1990)" "Danse avec les loups" "Dances with Wolves" 1990 7.8 tt0099348 "FR-TP" "Western|Drame" "" "Kevin Costner" "Kevin Costner|Mary McDonnell" "Un officier de l'armée nordiste se lie avec une tribu sioux."
movie "$M5" "The Mask (1994)" "The Mask" "The Mask" 1994 6.9 tt0110475 "FR-TP" "Comédie|Fantastique" "masque" "Chuck Russell" "Jim Carrey|Cameron Diaz" "Un employé de banque timide trouve un masque magique."
movie "$M5" "Dumb and Dumber (1994)" "Dumb & Dumber" "Dumb and Dumber" 1994 6.6 tt0109686 "FR-TP" "Comédie" "road movie" "Peter Farrelly" "Jim Carrey|Jeff Daniels" "Deux amis idiots traversent les États-Unis pour rendre une mallette."
movie "$M5" "Les Dents de la mer (1975)" "Les Dents de la mer" "Jaws" 1975 7.7 tt0073195 "FR-12" "Horreur|Thriller" "requin|plage" "Steven Spielberg" "Roy Scheider|Richard Dreyfuss" "Un grand requin blanc terrorise une station balnéaire."
movie "$M5" "Jurassic Park (1993)" "Jurassic Park" "Jurassic Park" 1993 7.9 tt0107290 "FR-TP" "Aventure|Science-Fiction" "dinosaures" "Steven Spielberg" "Sam Neill|Laura Dern" "Un parc d'attractions peuplé de dinosaures clonés."
movie "$M5" "Deja vu (2006)" "Déjà vu" "Déjà Vu" 2006 6.9 tt0453467 "FR-12" "Action|Thriller" "" "Tony Scott" "Denzel Washington" "Un agent remonte le temps pour empêcher un attentat."

S="$DIR/SERIES/Breaking Bad"
cat > "$S/tvshow.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<tvshow>
  <title>Breaking Bad</title>
  <plot>Un professeur de chimie atteint d'un cancer se met à fabriquer de la drogue.</plot>
  <year>2008</year>
  <genre>Drame</genre>
  <genre>Crime</genre>
  <ratings><rating name="themoviedb" max="10" default="true"><value>8.9</value></rating></ratings>
  <uniqueid type="imdb">tt0903747</uniqueid>
  <actor><name>Bryan Cranston</name><type>Actor</type></actor>
</tvshow>
EOF
video "$S/Season 1/Breaking Bad S01E01.mkv"
cat > "$S/Season 1/Breaking Bad S01E01.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<episodedetails>
  <title>Chute libre</title>
  <season>1</season>
  <episode>1</episode>
  <plot>Walter White apprend qu'il est malade.</plot>
  <aired>2008-01-20</aired>
</episodedetails>
EOF

# a small IMDb ratings file (same format as https://datasets.imdbws.com/title.ratings.tsv.gz)
IMDB="$(dirname "$DIR")/imdb"
mkdir -p "$IMDB"
printf 'tconst\taverageRating\tnumVotes\ntt0000001\t5.7\t2100\ntt0073195\t8.1\t680000\ntt0078748\t8.5\t950000\ntt0090605\t8.4\t760000\ntt0099348\t8.0\t290000\ntt0107290\t8.2\t1100000\ntt0109686\t7.3\t420000\ntt0110475\t6.9\t400000\ntt0211915\t8.3\t780000\ntt0453467\t7.0\t290000\ntt0903747\t9.5\t2200000\n' \
    > "$IMDB/title.ratings.tsv"

# JELLYSEARCH_EXTRA_MOVIES=<n>: n more documentaries on MOVIES07, for trying the result pages by hand
# (genre "Documentaire" and words "archive", absent from the other films: the checks are not changed)
EXTRA=${JELLYSEARCH_EXTRA_MOVIES:-0}
if [ "$EXTRA" -gt 0 ]; then
    M7="$DIR/MOVIES07/MOVIES07"
    mkdir -p "$M7"
    video "$M7/.base.mkv"
    for i in $(seq 1 "$EXTRA"); do
        n=$(printf '%03d' "$i")
        year=$((1960 + (i * 7) % 64))
        tmdb=$(LC_ALL=C awk -v i="$i" 'BEGIN { printf "%.1f", 4 + ((i * 37) % 50) / 10 }')
        imdb=$(LC_ALL=C awk -v i="$i" 'BEGIN { printf "%.1f", 4.5 + ((i * 53) % 45) / 10 }')
        ln "$M7/.base.mkv" "$M7/Archive $n ($year).mkv"
        cat > "$M7/Archive $n ($year).nfo" <<NFO
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Archive $n</title>
  <plot>Documentaire d'archive numéro $n.</plot>
  <year>$year</year>
  <ratings><rating name="themoviedb" max="10" default="true"><value>$tmdb</value></rating></ratings>
  <uniqueid type="imdb">tt99$(printf '%05d' "$i")</uniqueid>
  <genre>Documentaire</genre>
  <runtime>$((60 + (i * 13) % 90))</runtime>
</movie>
NFO
        printf 'tt99%05d\t%s\t%d\n' "$i" "$imdb" $((1000 + i * 917)) >> "$IMDB/title.ratings.tsv"
    done
    rm -f "$M7/.base.mkv"
fi
gzip -c "$IMDB/title.ratings.tsv" > "$IMDB/title.ratings.tsv.gz"
rm -f "$IMDB/title.ratings.tsv"

echo "Test media written to $DIR"
find "$DIR" -type f -name "*.mkv" | sort
