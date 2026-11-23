#!/usr/bin/env python3
"""JellySearch on a real Jellyfin: the searches of the TV app and of the web page, the IMDb ratings,
the saved searches as TV tiles, the ratings line of the TV details screen.

    search-checks.py <instance> <media>

Needs a running test instance (test-server.py start) with the media of make-media.sh, scanned, and
JELLYSEARCH_IMDB_URL pointing at the small IMDb file make-media.sh writes (run-all.sh does all this).
MOVIES03 is "unplugged" for a moment (renamed, then restored); "Amélie" is marked watched, then not.
"""
import importlib.util
import json
import os
import sys
import time
import urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("ts", os.path.join(HERE, "test-server.py"))
ts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ts)

ATV = 'MediaBrowser Client="Jellyfin for Android TV", Device="test-tv", DeviceId="jellysearch-tv", Version="0.20.0"'
WEB = 'MediaBrowser Client="Jellyfin Web", Device="test-web", DeviceId="jellysearch-web", Version="12.4.0"'
WEB_TYPES = "Movie,Series,Episode,Playlist,MusicAlbum,Audio,TvChannel,PhotoAlbum,Photo,AudioBook,Book,BoxSet"

failures = []


def check(label, ok, detail=""):
    print(("ok    " if ok else "FAIL  ") + label + ("" if ok else f"  [{detail}]"))
    if not ok:
        failures.append(label)


def get(path, token, client=ATV):
    status, body = ts.call("GET", path, client=client + f', Token="{token}"', raw=True)
    try:
        return status, (json.loads(body) if body else None)
    except ValueError:
        return status, body


def names(token, text, kind="Movie", client=ATV, extra=""):
    q = urllib.parse.quote(text)
    status, r = get(f"/Items?searchTerm={q}&limit=25&includeItemTypes={kind}&recursive=true&fields=Overview{extra}", token, client)
    return [i["Name"] for i in r["Items"]] if status == 200 else [f"HTTP {status}"]


def main():
    instance, media = sys.argv[1], sys.argv[2]
    token = ts.creds(instance)["token"]
    user_id = ts.call("GET", "/Users/Me", token=token)["Id"]
    movies = {i["Name"]: i for i in ts.call("GET", "/Items?Recursive=true&IncludeItemTypes=Movie&Fields=Overview,ProviderIds", token=token)["Items"]}

    # the index is rebuilt a few seconds after the scan, then again once the metadata (people, tags…) is in
    for _ in range(120):
        if names(token, "amelie") and names(token, "sigourney") and names(token, "nostromo") and names(token, "requin"):
            break
        time.sleep(1)
    time.sleep(1)

    # --- the web page gets the script (next to JellyUSB's when it is installed too)
    status, html = ts.call("GET", "/web/index.html", raw=True)
    check("web page: JellySearch script added", status == 200 and b"JellySearch/web.js" in html)
    if os.environ.get("JELLYSEARCH_WITH_JELLYUSB"):
        check("web page: JellyUSB script kept", b"JellyUsb/web.js" in html)

    # --- free text (TV app: one request per row)
    check("accents: « amelie »", names(token, "amelie") == ["Le Fabuleux Destin d'Amélie Poulain"], names(token, "amelie"))
    check("typo: « ailen »", names(token, "ailen")[:1] == ["Alien, le huitième passager"], names(token, "ailen"))
    check("typo in a person: « spilberg »", sorted(names(token, "spilberg")) == ["Jurassic Park", "Les Dents de la mer"], names(token, "spilberg"))
    check("actor: « sigourney »", sorted(names(token, "sigourney")) == ["Alien, le huitième passager", "Aliens, le retour"], names(token, "sigourney"))
    check("tag: « requin »", names(token, "requin") == ["Les Dents de la mer"], names(token, "requin"))
    check("overview: « nostromo »", names(token, "nostromo") == ["Alien, le huitième passager"], names(token, "nostromo"))
    check("original title: « jaws »", names(token, "jaws")[:1] == ["Les Dents de la mer"], names(token, "jaws"))
    check("exact title first: « deja vu »", names(token, "deja vu")[:1] == ["Déjà vu"], names(token, "deja vu"))
    check("series row", names(token, "breaking", "Series") == ["Breaking Bad"], names(token, "breaking", "Series"))
    check("episode row: « chute libre »", names(token, "chute libre", "Episode") == ["Chute libre"], names(token, "chute libre", "Episode"))

    # --- filters and phrases
    check("filter: genre:comedie annee:90s", sorted(names(token, "genre:comedie annee:90s")) == ["Dumb & Dumber", "The Mask"], names(token, "genre:comedie annee:90s"))
    check("filter: note>7.8 (7.8 excluded)", "Danse avec les loups" not in names(token, "note>7.8") and "Alien, le huitième passager" in names(token, "note>7.8"), names(token, "note>7.8"))
    check("OU / NON", sorted(names(token, "(genre:western OU genre:comedie) -acteur:carrey")) == ["Danse avec les loups", "Le Fabuleux Destin d'Amélie Poulain"],
          names(token, "(genre:western OU genre:comedie) -acteur:carrey"))
    check("phrase: films d'horreur des années 70", sorted(names(token, "films d'horreur des années 70")) == ["Alien, le huitième passager", "Les Dents de la mer"],
          names(token, "films d'horreur des années 70"))
    check("phrase: comédies des années 90 avec Jim Carrey", sorted(names(token, "comédies des années 90 avec Jim Carrey")) == ["Dumb & Dumber", "The Mask"],
          names(token, "comédies des années 90 avec Jim Carrey"))
    check("phrase: films de spielberg après 1990", names(token, "films de spielberg après 1990") == ["Jurassic Park"], names(token, "films de spielberg après 1990"))
    check("title kept: danse avec les loups", names(token, "danse avec les loups")[:1] == ["Danse avec les loups"], names(token, "danse avec les loups"))
    check("filter only: no episodes", names(token, "genre:drame", "Episode") == [], names(token, "genre:drame", "Episode"))
    check("media:MOVIES03", sorted(names(token, "media:MOVIES03")) == ["Alien, le huitième passager", "Aliens, le retour"], names(token, "media:MOVIES03"))

    # --- the web page asks for every kind at once
    status, r = get(f"/Items?userId={user_id}&isMissing=false&limit=800&recursive=true&searchTerm=sigourney&includeItemTypes={WEB_TYPES}&fields=PrimaryImageAspectRatio&imageTypeLimit=1&enableTotalRecordCount=false",
                    token, WEB)
    check("web: one request for every kind", status == 200 and sorted(i["Name"] for i in r["Items"]) == ["Alien, le huitième passager", "Aliens, le retour"],
          (status, r if status != 200 else [i["Name"] for i in r["Items"]]))
    status, body = ts.call("GET", f"/Items?searchTerm=alien&includeItemTypes=Movie&recursive=true", client=WEB + f', Token="{token}"', raw=True)
    check("web: answer is Jellyfin's item format", status == 200 and all("ServerId" in i and "Type" in i for i in json.loads(body)["Items"]))

    # --- parent, paging, kinds left to Jellyfin
    views = ts.call("GET", f"/UserViews?userId={user_id}", token=token)["Items"]
    films = next(v["Id"] for v in views if v.get("CollectionType") == "movies")
    status, r = get(f"/Items?searchTerm=drame&recursive=true&parentId={films}&includeItemTypes=Movie,Series", token)
    check("parentId: only the movies library", status == 200 and [i["Name"] for i in r["Items"]] == ["Danse avec les loups"], [i["Name"] for i in r["Items"]] if status == 200 else status)
    status, page1 = get("/Items?searchTerm=sigourney&includeItemTypes=Movie&recursive=true&limit=1", token)
    status2, page2 = get("/Items?searchTerm=sigourney&includeItemTypes=Movie&recursive=true&limit=1&startIndex=1", token)
    check("paging", status == 200 and status2 == 200 and page1["TotalRecordCount"] == 2 and len(page1["Items"]) == 1 and len(page2["Items"]) == 1
          and page1["Items"][0]["Id"] != page2["Items"][0]["Id"], (page1, page2))
    check("people row left to Jellyfin", "Sigourney Weaver" in names(token, "sigourney", "Person"), names(token, "sigourney", "Person"))

    # --- IMDb ratings (the task downloads the small file of the test)
    tasks = ts.call("GET", "/ScheduledTasks?isHidden=false", token=token)
    task = next((t for t in tasks if t["Key"] == "JellySearchImdbRatings"), None)
    check("IMDb task listed", task is not None)
    if task:
        ts.call("POST", f"/ScheduledTasks/Running/{task['Id']}", token=token)
        for _ in range(30):
            t = next(t for t in ts.call("GET", "/ScheduledTasks?isHidden=false", token=token) if t["Key"] == "JellySearchImdbRatings")
            if t["State"] == "Idle" and (t.get("LastExecutionResult") or {}).get("Status"):
                break
            time.sleep(1)
        check("IMDb task completed", (t.get("LastExecutionResult") or {}).get("Status") == "Completed", t.get("LastExecutionResult"))
    alien = movies["Alien, le huitième passager"]
    status, r = get(f"/JellySearch/Items/{alien['Id']}/Ratings", token, WEB)
    check("ratings endpoint", status == 200 and r["Imdb"] == 8.5 and r["ImdbVotes"] == 950000 and abs(r["Tmdb"] - 8.1) < 0.01, r)
    check("filter imdb>8.3", sorted(names(token, "imdb>8.3")) == ["Alien, le huitième passager", "Aliens, le retour"], names(token, "imdb>8.3"))
    check("phrase: notés plus de 8.2 sur imdb", sorted(names(token, "films notés plus de 8.2 sur imdb")) == ["Alien, le huitième passager", "Aliens, le retour", "Le Fabuleux Destin d'Amélie Poulain"],
          names(token, "films notés plus de 8.2 sur imdb"))

    # --- the TV details screen shows both ratings; nothing stored
    status, dto = get(f"/Items/{alien['Id']}?userId={user_id}", token, ATV)
    ov = (dto or {}).get("Overview") or ""
    check("TV details: IMDb and TMDB line first", ov.startswith("⭐ IMDb **8,5** (950 k votes) · TMDB **8,1**"), ov[:80])
    check("TV details: overview kept", (alien.get("Overview") or "") in ov)
    status, dto = get(f"/Items/{alien['Id']}?userId={user_id}", token, WEB)
    check("web details: overview unchanged", (dto or {}).get("Overview") == alien.get("Overview"))

    # --- understood
    status, r = get("/JellySearch/Explain?q=" + urllib.parse.quote("films d'horreur des années 80 avec Sigourney Weaver"), token, WEB)
    check("explain", status == 200 and r["Description"] == "films · genre Horreur · années 80 · avec Sigourney Weaver" and r["Syntax"] == 'type:film genre=Horreur annee:1980..1989 acteur:"Sigourney Weaver"', r)

    # --- saved search: a tile on the TV
    status, body = ts.call("POST", "/JellySearch/Saved", {"Name": "Horreur 70", "Query": "films d'horreur des années 70"}, client=WEB + f', Token="{token}"', raw=True)
    saved = json.loads(body) if status == 200 else {}
    check("saved: created with the syntax", status == 200 and saved.get("Query") == "type:film genre=Horreur annee:1970..1979", (status, body[:200]))
    sid = saved.get("Id", "00000000000000000000000000000000")
    status, tv_views = get("/UserViews", token, ATV)
    tile = next((v for v in (tv_views or {}).get("Items", []) if v["Id"].replace("-", "") == sid.replace("-", "")), None)
    check("TV: tile in My media", tile is not None and tile.get("CollectionType") == "boxsets" and tile.get("Name") == "Horreur 70", tv_views)
    status, web_views = get("/UserViews", token, WEB)
    check("web: no virtual library (the script adds the tile)", all(v["Id"].replace("-", "") != sid.replace("-", "") for v in web_views["Items"]))
    status, view = get(f"/Items/{sid}?userId={user_id}", token, ATV)
    check("TV: the tile opens", status == 200 and view.get("Type") == "CollectionFolder", (status, view))
    status, grid = get(f"/Items?parentId={sid}&fields=Overview&sortBy=SortName&startIndex=0&limit=50", token, ATV)
    check("TV: the grid lists the results", status == 200 and sorted(i["Name"] for i in grid["Items"]) == ["Alien, le huitième passager", "Les Dents de la mer"] and grid["TotalRecordCount"] == 2,
          (status, grid))
    status, grid = get(f"/Items?parentId={sid}&sortBy=PremiereDate&sortOrder=Descending", token, ATV)
    check("TV: the grid follows the order asked", status == 200 and [i["Name"] for i in grid["Items"]] == ["Alien, le huitième passager", "Les Dents de la mer"], grid)
    status, png = ts.call("GET", f"/Items/{sid}/Images/Primary?maxWidth=480", raw=True)
    check("TV: tile image (PNG, no authentication)", status == 200 and png[:4] == b"\x89PNG", status)
    status, png = ts.call("GET", f"/JellySearch/Saved/{sid}/Image", raw=True)
    check("web: tile image", status == 200 and png[:4] == b"\x89PNG", status)
    status, _ = ts.call("DELETE", f"/JellySearch/Saved/{sid}", client=WEB + f', Token="{token}"', raw=True)
    status2, tv_views = get("/UserViews", token, ATV)
    check("saved: deleted, tile gone", status == 204 and all(v["Id"].replace("-", "") != sid.replace("-", "") for v in tv_views["Items"]), status)

    # --- watched, then not
    amelie = movies["Le Fabuleux Destin d'Amélie Poulain"]["Id"]
    ts.call("POST", f"/UserPlayedItems/{amelie}?userId={user_id}", token=token)
    time.sleep(11)
    check("vu:non leaves out a watched movie", "Le Fabuleux Destin d'Amélie Poulain" not in names(token, "vu:non"), names(token, "vu:non"))
    check("vu:oui", names(token, "vu:oui") == ["Le Fabuleux Destin d'Amélie Poulain"], names(token, "vu:oui"))
    ts.call("DELETE", f"/UserPlayedItems/{amelie}?userId={user_id}", token=token)

    # --- a drive unplugged
    m3 = os.path.join(os.path.abspath(media), "MOVIES03")
    os.rename(m3, m3 + ".unplugged")
    os.makedirs(m3)
    try:
        time.sleep(11)
        check("dispo:non: the movies of the unplugged drive", sorted(names(token, "dispo:non")) == ["Alien, le huitième passager", "Aliens, le retour"], names(token, "dispo:non"))
        check("phrase: films disponibles", "Alien, le huitième passager" not in names(token, "films disponibles") and "Jurassic Park" in names(token, "films disponibles"),
              names(token, "films disponibles"))
        check("an unplugged drive is still searched", names(token, "nostromo") == ["Alien, le huitième passager"], names(token, "nostromo"))
    finally:
        os.rmdir(m3)
        os.rename(m3 + ".unplugged", m3)

    # --- nothing stored in the database
    again = ts.call("GET", f"/Items?Ids={alien['Id']}&Fields=Overview", token=token)["Items"][0]
    check("nothing stored in the database", again.get("Overview") == alien.get("Overview"))

    print("ALL SEARCH CHECKS PASSED" if not failures else f"{len(failures)} FAILED")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
