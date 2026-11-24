# JellySearch

Plugin [Jellyfin](https://jellyfin.org) 12.4 qui améliore la recherche, sur la page web comme sur la TV
(application Jellyfin pour Android TV) :

1. **Accents et fautes de frappe ignorés.** « amelie » trouve *Le Fabuleux Destin d'Amélie Poulain*,
   « ailen » *Alien*, « harri poter » *Harry Potter*, « spilberg » les films de Steven Spielberg.
2. **Plus que les titres.** Le texte est aussi cherché dans le titre original, les acteurs, les
   réalisateurs et scénaristes, les genres, les tags, les studios et le résumé. Un titre tapé exactement
   reste en tête, et les titres passent avant le reste.
3. **Des filtres combinables** : note TMDB ou IMDb, votes, note critique, année, durée, âge minimum,
   genre, acteur, réalisateur, tag, studio, MediaID (disque USB), disque branché, type, vu, favori, date
   d'ajout, tri ; avec `ET`, `OU`, `NON`, `>`, `<`, `>=`, `<=`, `=`, `!=`, des intervalles et des
   parenthèses.
4. **Des phrases**, tapées ou dictées au micro de la télécommande : « films d'horreur des années 80 avec
   Sigourney Weaver », « comédies notées plus de 7 sur IMDb », « films de Spielberg après 2000
   disponibles », « séries non vues de moins de 45 minutes ».
5. **Des tuiles.** Une recherche enregistrée devient une tuile dans « Mes médias », sur le web et sur la
   TV, avec son image ; elle s'ouvre sur ses résultats, toujours à jour.
6. **Les deux notes.** Jellyfin ne garde qu'une note (TMDB avec les NFO Kodi) : JellySearch télécharge
   chaque semaine le fichier public des notes IMDb et les affiche sur la fiche des films, sans rien
   écrire dans la base. Une recherche par filtres est triée par note IMDb.
7. **Tous les résultats et leur nombre.** Sur le web, une grille de 4 lignes d'affiches par page (ou une
   liste), avec le nombre de résultats, des onglets par type, le tri et des flèches pour la page suivante.
   Sur la TV, une carte « N résultats » en tête de la recherche ouvre une grille verticale de tous les
   résultats.

La recherche est faite par le serveur : tous les clients en profitent (page web, application Android TV,
applications mobiles), sans rien installer sur la TV. La musique, la TV en direct et les personnes restent
cherchées par Jellyfin.

![JellySearch](assets/jellysearch.png)

## Installation

Dans Jellyfin : **Tableau de bord > Extensions > Dépôts > +**, puis

```
https://raw.githubusercontent.com/webbackuper/jellysearch/main/manifest.json
```

puis **Catalogue > JellySearch > Installer**, et redémarrez Jellyfin. Les mises à jour arrivent par le même
dépôt ; la page du plugin (menu **Extensions > JellySearch**) a un onglet « Historique des versions ».

Ou depuis ce dépôt, sur la machine de Jellyfin :

```bash
dotnet build -c Release
sudo ./install.sh
```

Au démarrage, JellySearch construit son index en quelques secondes (il le reconstruit après chaque scan),
puis télécharge les notes IMDb.

## Sur la TV

L'écran de recherche de l'application est natif : on ne peut pas y ajouter de boutons, mais tout ce qu'on
tape ou dicte passe par JellySearch.

- Tapez quelques lettres : les fautes et les accents sont pardonnés, et les rangées Films, Séries, Épisodes
  et Collections se remplissent avec les résultats de JellySearch.
- Tapez une syntaxe courte : `genre:horreur annee:80s`, `imdb>8 vu:non`.
- Ou appuyez sur le micro et dites une phrase : « comédies des années 90 avec Jim Carrey ».
- La rangée Films ne montre que 25 films. Quand la recherche filtre (ou trouve plus de 25 films), elle
  commence par une carte **« N résultats »** : le nombre de résultats, ce qui a été compris et l'ordre.
  Après la touche loupe du clavier, elle a le focus : OK ouvre une **grille verticale** de tous les résultats,
  films et séries, par note IMDb. Le bouton de tri de la grille change l'ordre (« Community rating » est la
  note IMDb, puis nom, date d'ajout, date de sortie, note critique) et l'application le garde pour les
  recherches suivantes ; elle filtre aussi les non vus et les favoris.
- Les recherches enregistrées depuis le web sont des tuiles dans **Mes médias**. Une tuile ouvre la grille
  de ses résultats, que la TV peut trier (nom, date d'ajout, note…).
- La fiche d'un film commence par ses notes : « ⭐ IMDb **8,5** (950 k votes) · TMDB **8,1** ».

## Sur le web

La page de recherche gagne trois boutons sous le champ :

- **Filtres** ouvre un panneau qui compose la recherche : des mots, puis une ligne par filtre (champ,
  opérateur, valeur), combinées par ET ou OU. Les genres et les MediaID sont proposés en liste, les tags et
  studios en suggestions. La recherche obtenue s'affiche en syntaxe, puis dans le champ.
- **Aide** rappelle la syntaxe et donne des exemples.
- **Créer une tuile** enregistre la recherche en cours sous le nom choisi (avec l'ordre choisi dans la
  grille). Le panneau Filtres liste vos tuiles, et permet de les ouvrir ou de les supprimer.

Sous le champ, « **Compris :** films · genre Horreur · années 80 · avec Sigourney Weaver » montre comment
la recherche a été lue.

Les résultats s'affichent en **grille** à la place des rangées Films, Séries, Épisodes et Collections de
Jellyfin (les personnes, la musique… restent en dessous) :

- en tête, le **nombre de résultats** et, quand il y a plusieurs types, des onglets « Tout (87) ·
  Films (64) · Séries (20) · Collections (3) » ;
- **Trier par** : note IMDb (par défaut pour une recherche par filtres), pertinence (par défaut pour une
  recherche par mots), note TMDB, note critique, année, date d'ajout, titre, durée, hasard ;
- l'affichage en **affiches** (4 lignes, autant de colonnes que la largeur en permet) ou en **liste**
  (titre, type, année, notes IMDb et TMDB, durée, genres) ;
- les **flèches** en haut à droite (et en bas) passent aux résultats suivants : « 1–32 sur 87 ».

En revenant d'une fiche, la grille retrouve sa page et son ordre.

Sur la page d'accueil, vos tuiles s'ajoutent à **Mes médias**. Sur la fiche d'un film, la note IMDb
s'affiche à côté de l'étoile de Jellyfin.

## Syntaxe

Les termes s'additionnent (ET). Les valeurs avec espaces s'écrivent entre guillemets.

| Champ | Exemples | Remarque |
|---|---|---|
| texte libre | `alien`, `"le parrain"` | titres, personnes, genres, tags, studios, résumé ; guillemets : mots qui se suivent |
| `titre`, `resume` | `titre:alien`, `resume:vaisseau` | le texte seulement dans ce champ |
| `genre` | `genre:horreur`, `genre:western\|comedie` | singulier ou pluriel, avec ou sans accents ; `genre=Drame` : exactement |
| `acteur` (`avec`), `realisateur` (`par`), `personne` | `acteur:"jim carrey"`, `realisateur:spielberg` | un nom ou un nom de famille, fautes tolérées |
| `tag`, `studio` | `tag:requin`, `studio:pixar` | |
| `annee` | `annee:1994`, `annee:1980..1989`, `annee:80s`, `annee>2000` | `annee:80` = les années 80 |
| `duree` | `duree<90`, `duree<1h45`, `duree:1h30..2h` | en minutes ; `duree:90` = de 85 à 95 min |
| `tmdb` (`note`), `imdb`, `critique` | `tmdb>7`, `imdb>=8`, `critique>80` | `note:7` = de 7 à 7,9 |
| `votes` | `votes>100k` | votes IMDb |
| `age` | `age<=12` | âge minimum de la classification (FR-12, PG-13, TP…) |
| `media`, `dispo` | `media:MOVIES03`, `dispo:oui` | MediaID : le dossier qui suit le chemin des disques (`/mnt/usb/MOVIES03/…`) ; `dispo` : disque branché |
| `type` | `type:film`, `type:serie\|episode`, `type!=collection` | une recherche sans texte n'affiche pas les épisodes, sauf `type:episode` |
| `vu`, `favori` | `vu:non`, `favori:oui` | pour l'utilisateur connecté |
| `ajout` | `ajout<30j`, `ajout<2sem`, `ajout>=2026-01-01` | ajouté il y a moins de 30 jours, depuis le 1er janvier… |
| `tri` | `tri:imdb`, `note`, `critique`, `annee`, `ancien`, `ajout`, `titre`, `duree`, `hasard`, `pertinence` | sans `tri` : pertinence pour des mots, note IMDb pour des filtres (réglable) |

Combinaisons : `OU`, `NON` (ou `-mot`, `-genre:romance`), parenthèses :
`(genre:western OU genre:comedie) annee<2000 -genre:romance`. Les champs ont aussi des noms anglais
(`year`, `rating`, `actor`, `director`, `watched`…), et les opérateurs peuvent être espacés (`note > 7,5`).

### Phrases comprises

| On dit | Compris |
|---|---|
| « films », « séries », « épisodes », « collections » | `type:` |
| « comédies », « d'horreur », « science-fiction », « dessins animés », « polar »… | le genre (s'il existe dans la médiathèque) ou le mot dans un titre |
| « des années 80 », « de 1995 », « après 2000 », « avant 1980 », « entre 1990 et 2000 » | `annee` |
| « avec Jim Carrey », « de Spielberg », « réalisé par Ridley Scott » | `acteur`, `realisateur` (seulement si la personne est dans la médiathèque) |
| « notés plus de 7 », « plus de 8 sur IMDb », « bien notés » | `tmdb`, `imdb` |
| « de moins de 2 heures », « de plus de 90 minutes » | `duree` |
| « non vus », « déjà vus », « mes favoris » | `vu`, `favori` |
| « disponibles », « non disponibles », « sur le disque MOVIES03 » | `dispo`, `media` |
| « récemment ajoutés », « cette semaine » | `ajout` |
| « les mieux notés », « les plus récents », « au hasard » | `tri` |

Ce qui n'est pas compris reste du texte. Si une phrase ne trouve rien, elle est cherchée mot à mot ; un
titre comme « Danse avec les loups » ou « Déjà vu » reste un titre.

## Notes IMDb

La tâche **Télécharger les notes IMDb** (Tableau de bord > Tâches planifiées > JellySearch, une fois par
semaine) télécharge [title.ratings.tsv.gz](https://datasets.imdbws.com/), environ 8 Mo, publié par IMDb
pour un usage personnel et non commercial. Seules les notes des films et séries de la médiathèque sont
gardées (par identifiant IMDb, celui des NFO), dans un petit fichier à côté des réglages du plugin. La base
de Jellyfin n'est pas modifiée. Le téléchargement se désactive dans les réglages.

## Réglages

**Tableau de bord > Extensions > JellySearch.**

| Réglage | Par défaut | Effet |
|---|---|---|
| Remplacer la recherche | oui | Films, séries, épisodes et collections sont cherchés par JellySearch |
| Comprendre les phrases | oui | Lit les phrases ; sinon, seulement le texte et la syntaxe |
| Tolérer les fautes de frappe | oui | Une faute pour 4 à 7 lettres, deux au-delà |
| Ordre des recherches par filtres | note IMDb | Ou note TMDB, note critique, année, date d'ajout, titre, durée, hasard |
| Télécharger les notes IMDb | oui | Tâche hebdomadaire, filtres `imdb` et `votes` |
| Dossier des disques USB | `/mnt/usb/` | Pour `media:` et `dispo:` (comme le chemin de base de JellyUSB) |
| Chercher aussi dans… | oui | Résumés ; acteurs, réalisateurs et scénaristes ; genres, tags et studios |
| TV : carte « N résultats », tuiles, notes sur la fiche | oui | |
| Web : script, grille des résultats, panneau Filtres, « Compris », tuiles, note IMDb | oui | |

## Avec JellyUSB

Les deux plugins s'installent ensemble : chacun ajoute son propre script à la page web et sa propre ligne à
la fiche TV. Sur la fiche d'un film, la TV affiche alors l'état du disque et les notes. Sans JellyUSB,
`media:` et `dispo:` fonctionnent aussi : ils lisent le dossier des disques et `/proc/self/mountinfo`, sans
jamais réveiller un point de montage automatique.

## Désinstallation

Le bouton **Désinstaller** de la page du plugin (ou `sudo ./install.sh --uninstall`) retire tout ce que
JellySearch a enregistré : ses réglages, les recherches enregistrées, les notes IMDb, le cache, le fichier
de sa tâche planifiée et son dépôt de plugins. JellySearch n'écrit jamais dans la base de Jellyfin ni dans
les fichiers de l'interface web : au redémarrage, Jellyfin est tel qu'avant.

## Avant de mettre à jour Jellyfin

JellySearch répond aux requêtes `/Items?searchTerm=…` des clients et ajoute ses tuiles aux vues de
l'application TV. Avant de passer à une nouvelle version de Jellyfin, lancez les tests d'intégration avec
cette version, sur une instance jetable :

```bash
JELLYSEARCH_JF_BIN=/chemin/jellyfin/jellyfin JELLYSEARCH_JF_WEBDIR=/chemin/jellyfin/jellyfin-web tests/integration/run-all.sh
```

En cas d'erreur, JellySearch laisse la recherche à Jellyfin : au pire, on retrouve la recherche d'origine.

## Tests

```bash
dotnet test                       # tests unitaires du moteur (accents, fautes, filtres, phrases)
dotnet build -c Release
tests/integration/run-all.sh      # instance Jellyfin jetable, médiathèque de test, puis tout est supprimé
```

- `search-checks.py` vérifie :
  - les recherches comme les envoie l'application TV (une requête par rangée) et comme les envoie la page
    web (une requête pour tous les types) ;
  - le filtre par bibliothèque, la pagination et les notes IMDb (servies par un petit fichier local) ;
  - l'ordre par défaut, la grille web (nombre, types, ordre, pages) ;
  - la carte « N résultats » de la TV, sa grille (ordre, filtres) et ses préférences d'affichage ;
  - les tuiles TV et la ligne des notes sur la fiche ;
  - un disque débranché et un film marqué vu.
- `uninstall-checks.py` désinstalle le plugin et vérifie qu'il ne reste rien.
- Avec `JELLYSEARCH_WITH_JELLYUSB=<dossier de Jellyfin.Plugin.JellyUsb.dll>`, JellyUSB est installé à côté.
- Pour essayer les pages de résultats à la main, `JELLYSEARCH_EXTRA_MOVIES=70 tests/integration/make-media.sh
  <dossier>` ajoute 70 documentaires (un disque MOVIES07) à la médiathèque de test.

`release.sh` lance ces tests et annule la publication au moindre échec.

## Comment ça marche

- **Index** (`LibraryIndex`, `SearchIndex`) : une copie en mémoire des films, séries, épisodes et
  collections, lue dans la base de Jellyfin :
  - le contenu : titres, résumé, personnes, genres, tags, studios ;
  - les chiffres : année, durée, notes, classification ;
  - le reste : identifiant IMDb, date d'ajout, MediaID.

  Les mots sont repliés : minuscules, sans accents, « œ » devient « oe ». L'index est un index inversé,
  avec un vocabulaire pour les préfixes et les fautes (distance d'édition avec inversions). Il est
  reconstruit 15 s après la dernière modification de la médiathèque.
- **Requête** (`QueryParser`, `NaturalQuery`) : un arbre ET / OU / NON de mots, de phrases entre
  guillemets et de filtres. Les suites de mots passent par les règles de phrases, qui ne reconnaissent un
  genre, une personne ou un MediaID que s'ils existent dans la médiathèque.
- **Score** (`SearchEngine`) : titre (10), titre original (8), série (6), acteurs et réalisateurs (6),
  genres et tags (4), studios (2), résumé (1,5). Un mot exact compte entièrement, un début de mot à 80 %,
  une faute à 65 % (ou 50 % pour deux fautes). Le titre exact passe en tête.
- **Réponse** (`SearchMiddleware`, `IdsForwarder`) : JellySearch choisit les éléments, en tenant compte des
  droits de l'utilisateur, du contrôle parental et de la bibliothèque demandée. La requête continue ensuite
  vers Jellyfin avec `ids=…` à la place de `searchTerm`. La réponse garde ainsi le format et les champs que
  le client attend ; JellySearch remet seulement les éléments dans son ordre. Pour la page web, qui demande
  tous les types à la fois, les types que JellySearch ne gère pas viennent de la recherche de Jellyfin.
- **Tuiles TV** (`TvSavedViewsMiddleware`) : chaque recherche enregistrée est une bibliothèque virtuelle
  de type « collections », ajoutée à `GET /UserViews` pour l'application Android TV ; sa grille est la
  recherche. Les images sont dessinées en SVG et converties en PNG avec la bibliothèque d'images livrée avec
  Jellyfin.
- **Carte « N résultats »** (`SearchMiddleware`, `ResultViews`) : pour l'application Android TV, la réponse
  de la rangée Films commence par une bibliothèque virtuelle de plus, gardée en mémoire un jour, dont la
  grille est la recherche. Ses préférences d'affichage (`GET /DisplayPreferences/jellysearch-results`)
  partent d'une grille verticale triée par note, tant que l'utilisateur n'a rien changé dans l'application.
- **Grille web** : `GET /JellySearch/Results?q=…&sort=…&types=…&start=…&limit=…` donne le nombre de
  résultats par type, l'ordre utilisé, les identifiants de la page et leurs notes IMDb ; les éléments
  viennent ensuite de Jellyfin (`/Items?ids=…`).
- **Web** : `WebClientInjectionMiddleware` ajoute `JellySearch/web.js` à `index.html` au moment où la page
  est servie.

## Licence

GPL-3.0 (voir [LICENSE](LICENSE)).
