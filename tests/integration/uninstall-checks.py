#!/usr/bin/env python3
"""Uninstalling JellySearch from Jellyfin leaves nothing behind.

    uninstall-checks.py <instance> <media>

Run last (run-all.sh does): it uninstalls the plugin from the test instance through Jellyfin's API
(the "Désinstaller" button), checks that JellySearch's files are gone, restarts the instance without
the plugin and checks that the search, the web page and the TV views are Jellyfin's own again.
"""
import glob
import importlib.util
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("ts", os.path.join(HERE, "test-server.py"))
ts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ts)

ATV = 'MediaBrowser Client="Jellyfin for Android TV", Device="test-tv", DeviceId="jellysearch-tv", Version="0.20.0"'
TASK_FILE = "14d3e7c0-e3c7-bd75-7e56-8be363d47c0c.js"  # "Télécharger les notes IMDb": MD5 of the task type name
failures = []


def check(label, ok, detail=""):
    print(("ok    " if ok else "FAIL  ") + label + ("" if ok else f"  [{detail}]"))
    if not ok:
        failures.append(label)


def main():
    instance, media = os.path.abspath(sys.argv[1]), sys.argv[2]
    token = ts.creds(instance)["token"]
    data = os.path.join(instance, "data")
    configurations = os.path.join(data, "plugins", "configurations")

    # something to clean: a saved search and the IMDb ratings
    status, _ = ts.call("POST", "/JellySearch/Saved", {"Name": "À nettoyer", "Query": "genre:western"}, token=token, raw=True)
    check("a saved search to clean", status == 200, status)
    own = os.path.join(configurations, "JellySearch")
    check("JellySearch's folder exists before", os.path.isdir(own), os.listdir(configurations))

    # the file of the IMDb task (written when it ran in search-checks.py)
    had_task_file = bool(glob.glob(os.path.join(instance, "**", "ScheduledTasks", TASK_FILE), recursive=True))

    # the repository install.sh declares
    repos = ts.call("GET", "/Repositories", token=token) or []
    repos.append({"Name": "JellySearch", "Url": "https://raw.githubusercontent.com/webbackuper/jellysearch/main/manifest.json", "Enabled": True})
    ts.call("POST", "/Repositories", repos, token=token)

    plugin = next(p for p in ts.call("GET", "/Plugins", token=token) if p["Id"].replace("-", "") == ts.PLUGIN_ID.replace("-", ""))
    status, _ = ts.call("DELETE", f"/Plugins/{plugin['Id']}/{plugin['Version']}", token=token, raw=True)
    check("uninstall accepted", status in (200, 204), status)
    time.sleep(2)

    check("settings file removed", not os.path.exists(os.path.join(configurations, "Jellyfin.Plugin.JellySearch.xml")))
    check("saved searches and IMDb ratings removed", not os.path.exists(own))
    check("cache folder removed", not os.path.exists(os.path.join(instance, "cache", "jellysearch")))
    task_files = glob.glob(os.path.join(instance, "**", "ScheduledTasks", TASK_FILE), recursive=True)
    check("task file removed", had_task_file and not task_files, (had_task_file, task_files))
    left = [r["Url"] for r in ts.call("GET", "/Repositories", token=token) or [] if "jellysearch" in r.get("Url", "")]
    check("plugin repository removed", not left, left)

    # restart without the plugin: Jellyfin as it was
    ts.stop(instance)
    os.environ["JELLYSEARCH_NO_PLUGIN"] = "1"
    for old in glob.glob(os.path.join(data, "plugins", "JellySearch_*")):
        subprocess.run(["rm", "-rf", old], check=False)
    with open(os.devnull, "w") as quiet:
        sys.stdout, real = quiet, sys.stdout
        try:
            ts.start(instance, media)
        finally:
            sys.stdout = real
    status, html = ts.call("GET", "/web/index.html", raw=True)
    check("web page without the JellySearch script", status == 200 and b"JellySearch/web.js" not in html)
    status, views = ts.call("GET", "/UserViews", client=ATV + f', Token="{token}"', raw=True)
    check("TV views are Jellyfin's", status == 200 and all(v.get("Name") != "À nettoyer" for v in json.loads(views)["Items"]))
    status, r = ts.call("GET", "/Items?searchTerm=amelie&includeItemTypes=Movie&recursive=true", token=token, raw=True)
    check("search answered by Jellyfin", status == 200)
    status, _ = ts.call("GET", "/JellySearch/Facets", token=token, raw=True)
    check("JellySearch endpoints gone", status == 404, status)

    print("ALL UNINSTALL CHECKS PASSED" if not failures else f"{len(failures)} FAILED")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
