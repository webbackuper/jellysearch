/* JellySearch — added to jellyfin-web by the JellySearch server plugin.
 * The search itself is done by the server (every client benefits, the TV app too). This script adds, in
 * the web client:
 * 1. on the search page: how the search was understood ("Compris : …"), a filter panel (field, operator,
 *    value; ET / OU), the syntax help, and "Créer une tuile" to save the search; the results in a grid of
 *    4 rows per page (or a list) with their number, by kind, in the order chosen (IMDb rating by default
 *    for filters), in place of Jellyfin's rows of movies, series, episodes and collections;
 * 2. the saved searches as tiles in "My media" on the home page;
 * 3. the IMDb rating next to Jellyfin's rating on the details page.
 */
(function () {
    'use strict';
    var SETTINGS = /*JELLYSEARCH_SETTINGS*/null || { search: true, filterPanel: true, explain: true, ratingsBadge: true, savedTiles: true, resultsGrid: true };
    var PREFIX = 'jsearch';

    function api() { return window.ApiClient; }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function getJson(path, params) {
        return api().getJSON(api().getUrl(path, params));
    }

    function send(type, path, body) {
        return api().ajax({ type: type, url: api().getUrl(path), data: body ? JSON.stringify(body) : undefined, contentType: 'application/json', dataType: type === 'DELETE' ? 'text' : 'json' });
    }

    function hashParams() {
        var h = location.hash || '';
        var q = h.indexOf('?');
        return { route: q < 0 ? h : h.slice(0, q), params: new URLSearchParams(q < 0 ? '' : h.slice(q + 1)) };
    }

    function toast(text) {
        var el = document.createElement('div');
        el.className = PREFIX + '-toast';
        el.textContent = text;
        document.body.appendChild(el);
        setTimeout(function () { el.classList.add('show'); }, 10);
        setTimeout(function () { el.classList.remove('show'); setTimeout(function () { el.remove(); }, 400); }, 4200);
    }

    function addStyle() {
        if (document.getElementById(PREFIX + 'Style')) { return; }
        var st = document.createElement('style');
        st.id = PREFIX + 'Style';
        st.textContent = ''
            + '.jsearch-bar{display:flex;flex-wrap:wrap;align-items:center;gap:.5em .8em;max-width:60em;margin:.6em auto 0;padding:0 1em;box-sizing:border-box}'
            + '.jsearch-btn{display:inline-flex;align-items:center;gap:.35em;background:rgba(128,128,128,.18);color:inherit;border:0;border-radius:2em;padding:.4em 1em;font:inherit;font-size:.92em;cursor:pointer}'
            + '.jsearch-btn:hover,.jsearch-btn:focus{background:rgba(128,128,128,.35);outline:none}'
            + '.jsearch-btn.on{background:#00a4dc;color:#fff}'
            + '.jsearch-btn[hidden]{display:none}'
            + '.jsearch-btn .material-icons{font-size:1.25em}'
            + '.jsearch-understood{flex:1 1 20em;opacity:.8;font-size:.92em;min-height:1.2em}'
            + '.jsearch-understood b{font-weight:600}'
            + '.jsearch-understood code{opacity:.75;font-size:.9em;margin-left:.5em}'
            + '.jsearch-panel{max-width:60em;margin:.8em auto 0;padding:1em 1.2em;box-sizing:border-box;border-radius:.5em;background:rgba(128,128,128,.12)}'
            + '.jsearch-panel[hidden],.jsearch-help[hidden],.jsearch-save[hidden]{display:none}'
            + '.jsearch-panel h3{margin:.2em 0 .7em;font-size:1.05em}'
            + '.jsearch-row{display:flex;flex-wrap:wrap;gap:.5em;align-items:center;margin-bottom:.55em}'
            + '.jsearch-panel select,.jsearch-panel input{font:inherit;color:inherit;background:rgba(0,0,0,.25);border:1px solid rgba(128,128,128,.45);border-radius:.3em;padding:.45em .6em;min-width:0}'
            + '.jsearch-panel select option{color:#000}'
            + '.jsearch-words{flex:1 1 18em}'
            + '.jsearch-vbox{display:inline-flex;gap:.4em;align-items:center;flex:1 1 12em}'
            + '.jsearch-vbox .jsearch-value{flex:1 1 6em}'
            + '.jsearch-x{background:none;border:0;color:inherit;opacity:.65;cursor:pointer;font-size:1.3em;padding:0 .3em}'
            + '.jsearch-x:hover{opacity:1}'
            + '.jsearch-actions{display:flex;flex-wrap:wrap;gap:.6em;margin-top:.8em;align-items:center}'
            + '.jsearch-primary{background:#00a4dc;color:#fff}'
            + '.jsearch-primary:hover,.jsearch-primary:focus{background:#0089ba}'
            + '.jsearch-preview{flex:1 1 100%;font-family:monospace;font-size:.88em;opacity:.75;word-break:break-word}'
            + '.jsearch-help{max-width:60em;margin:.8em auto 0;padding:1em 1.2em;box-sizing:border-box;border-radius:.5em;background:rgba(128,128,128,.12);line-height:1.6}'
            + '.jsearch-help code{background:rgba(0,0,0,.3);border-radius:.25em;padding:.05em .35em}'
            + '.jsearch-help ul{margin:.3em 0 .8em;padding-left:1.2em}'
            + '.jsearch-save{display:flex;flex-wrap:wrap;gap:.5em;align-items:center;max-width:60em;margin:.8em auto 0;padding:0 1em;box-sizing:border-box}'
            + '.jsearch-save input{flex:1 1 16em;font:inherit;color:inherit;background:rgba(0,0,0,.25);border:1px solid rgba(128,128,128,.45);border-radius:.3em;padding:.45em .6em}'
            + '.jsearch-saved{margin-top:1em;border-top:1px solid rgba(128,128,128,.3);padding-top:.8em}'
            + '.jsearch-saved h3{margin:0 0 .4em}'
            + '.jsearch-saved li{display:flex;gap:.6em;align-items:center;padding:.2em 0}'
            + '.jsearch-saved ul{list-style:none;margin:0;padding:0}'
            + '.jsearch-saved a{color:inherit}'
            + '.jsearch-saved .jsearch-q{opacity:.6;font-family:monospace;font-size:.85em;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1}'
            + '.jsearch-toast{position:fixed;left:50%;bottom:2em;transform:translate(-50%,1em);background:#333;color:#fff;padding:.8em 1.3em;border-radius:.4em;'
            + 'box-shadow:0 .3em 1em rgba(0,0,0,.4);opacity:0;transition:all .3s;z-index:99999;max-width:90vw}'
            + '.jsearch-toast.show{opacity:1;transform:translate(-50%,0)}'
            + '.jsearch-imdb{display:inline-flex;align-items:center;gap:.35em}'
            + '.jsearch-imdb-logo{background:#f5c518;color:#000;font-weight:800;font-size:.72em;border-radius:.2em;padding:.05em .3em;letter-spacing:.02em}'
            + '.jsearch-results{margin-top:1.2em}'
            + '.jsearch-results[hidden]{display:none}'
            + '.jsearch-rhead{display:flex;flex-wrap:wrap;align-items:center;gap:.6em 1.2em;margin-bottom:.9em}'
            + '.jsearch-count{font-size:1.5em;font-weight:600;white-space:nowrap}'
            + '.jsearch-count small{font-size:.62em;font-weight:400;opacity:.7;margin-left:.35em}'
            + '.jsearch-tabs{display:flex;flex-wrap:wrap;gap:.4em}'
            + '.jsearch-tab{background:rgba(128,128,128,.18);color:inherit;border:0;border-radius:2em;padding:.3em .9em;font:inherit;font-size:.88em;cursor:pointer}'
            + '.jsearch-tab:hover,.jsearch-tab:focus{background:rgba(128,128,128,.35);outline:none}'
            + '.jsearch-tab.on{background:#00a4dc;color:#fff}'
            + '.jsearch-rtools{margin-left:auto;display:flex;flex-wrap:wrap;align-items:center;gap:.5em .8em}'
            + '.jsearch-rtools label{display:inline-flex;align-items:center;gap:.4em;font-size:.9em;opacity:.9}'
            + '.jsearch-rtools select{font:inherit;color:inherit;background:rgba(0,0,0,.25);border:1px solid rgba(128,128,128,.45);border-radius:.3em;padding:.35em .5em}'
            + '.jsearch-rtools select option{color:#000}'
            + '.jsearch-views{display:inline-flex;border-radius:2em;overflow:hidden;background:rgba(128,128,128,.18)}'
            + '.jsearch-views button{background:none;border:0;color:inherit;padding:.3em .6em;cursor:pointer;display:inline-flex}'
            + '.jsearch-views button.on{background:#00a4dc;color:#fff}'
            + '.jsearch-pager{display:inline-flex;align-items:center;gap:.25em;white-space:nowrap}'
            + '.jsearch-pager span{font-size:.88em;opacity:.8;margin-right:.2em}'
            + '.jsearch-pager button{background:rgba(128,128,128,.18);color:inherit;border:0;border-radius:50%;width:2.3em;height:2.3em;display:inline-flex;align-items:center;justify-content:center;cursor:pointer}'
            + '.jsearch-pager button:hover:not(:disabled),.jsearch-pager button:focus:not(:disabled){background:rgba(128,128,128,.4);outline:none}'
            + '.jsearch-pager button:disabled{opacity:.3;cursor:default}'
            + '.jsearch-rfoot{display:flex;justify-content:flex-end;margin-top:1em}'
            + '.jsearch-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(9.5em,1fr));gap:1.3em .9em}'
            + '.jsearch-card{display:block;color:inherit;text-decoration:none;min-width:0;outline:none}'
            + '.jsearch-poster{position:relative;aspect-ratio:2/3;border-radius:.35em;overflow:hidden;background:#262626 center/cover no-repeat;box-shadow:0 .15em .5em rgba(0,0,0,.35);transition:transform .15s}'
            + '.jsearch-card:hover .jsearch-poster,.jsearch-card:focus .jsearch-poster{transform:scale(1.03);box-shadow:0 0 0 .2em #00a4dc}'
            + '.jsearch-noimg{display:flex;align-items:center;justify-content:center;text-align:center;padding:.6em;box-sizing:border-box;font-size:.95em;opacity:.8}'
            + '.jsearch-played{position:absolute;top:.35em;right:.35em;background:#00a4dc;color:#fff;border-radius:50%;width:1.5em;height:1.5em;display:flex;align-items:center;justify-content:center}'
            + '.jsearch-played .material-icons{font-size:1.05em}'
            + '.jsearch-kind{position:absolute;left:.35em;bottom:.35em;background:rgba(0,0,0,.7);color:#fff;border-radius:.25em;padding:.05em .4em;font-size:.72em}'
            + '.jsearch-title{margin-top:.45em;font-size:.95em;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}'
            + '.jsearch-meta{display:flex;flex-wrap:wrap;align-items:center;gap:.2em .5em;font-size:.8em;opacity:.78;min-height:1.3em}'
            + '.jsearch-star{color:#f5c518}'
            + '.jsearch-list{width:100%;border-collapse:collapse}'
            + '.jsearch-list td,.jsearch-list th{padding:.35em .6em;text-align:left;vertical-align:middle}'
            + '.jsearch-list th{font-weight:600;font-size:.85em;opacity:.7;border-bottom:1px solid rgba(128,128,128,.35)}'
            + '.jsearch-list tr+tr td{border-top:1px solid rgba(128,128,128,.15)}'
            + '.jsearch-list tbody tr:hover{background:rgba(128,128,128,.12)}'
            + '.jsearch-list a{color:inherit;text-decoration:none;font-weight:500}'
            + '.jsearch-list a:hover,.jsearch-list a:focus{text-decoration:underline}'
            + '.jsearch-list .jsearch-thumb{width:2.6em;height:3.9em;border-radius:.2em;background:#262626 center/cover no-repeat;display:block}'
            + '.jsearch-list .jsearch-num{text-align:right;white-space:nowrap}'
            + '.jsearch-list .jsearch-genres{opacity:.75;font-size:.88em}'
            + '.jsearch-empty{opacity:.7;padding:.5em 0}'
            // the JellySearch grid replaces Jellyfin's rows of movies, series, episodes and collections
            + 'body.jsearch-grid-on #searchPage .searchResults .verticalSection:has(.card[data-type="Movie"]),'
            + 'body.jsearch-grid-on #searchPage .searchResults .verticalSection:has(.card[data-type="Series"]),'
            + 'body.jsearch-grid-on #searchPage .searchResults .verticalSection:has(.card[data-type="Episode"]),'
            + 'body.jsearch-grid-on #searchPage .searchResults .verticalSection:has(.card[data-type="BoxSet"]){display:none}'
            + '@media (max-width:700px){.jsearch-grid{grid-template-columns:repeat(auto-fill,minmax(6.8em,1fr));gap:1em .6em}.jsearch-rtools{margin-left:0;width:100%}'
            + '.jsearch-pager{margin-left:auto}.jsearch-list .jsearch-hide-s{display:none}}'
            + '@media (max-width:600px){.jsearch-understood code{display:none}}';
        document.head.appendChild(st);
    }

    /* ---------- 1. search page ---------- */
    var facets = null;
    var facetsLoading = null;
    var explainTimer = null;
    var lastExplained = null;

    function loadFacets() {
        if (facets) { return Promise.resolve(facets); }
        if (!facetsLoading) {
            facetsLoading = getJson('JellySearch/Facets').then(function (f) { facets = f; return f; }, function () { facetsLoading = null; return null; });
        }
        return facetsLoading;
    }

    function searchPage() {
        var page = document.getElementById('searchPage');
        return page && !page.classList.contains('hide') ? page : null;
    }

    function searchInput() {
        var page = searchPage();
        return page ? page.querySelector('#searchTextInput') : null;
    }

    // the search box is driven by jellyfin-web (React): set its value as a user would
    function setQuery(text) {
        var input = searchInput();
        if (!input) {
            location.hash = '#/search?query=' + encodeURIComponent(text);
            return;
        }
        var setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        setter.call(input, text);
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        input.focus();
        explainSoon(true);
    }

    function currentQuery() {
        var input = searchInput();
        return input ? input.value : '';
    }

    // the builder: one row per filter
    var FIELDS = [
        { s: 'genre', l: 'Genre', k: 'text', v: 'Genres', select: true },
        { s: 'acteur', l: 'Acteur', k: 'text' },
        { s: 'realisateur', l: 'Réalisateur', k: 'text' },
        { s: 'personne', l: 'Personne (acteur, réalisateur…)', k: 'text' },
        { s: 'annee', l: 'Année', k: 'number' },
        { s: 'duree', l: 'Durée (minutes ou 1h30)', k: 'number' },
        { s: 'tmdb', l: 'Note TMDB', k: 'number' },
        { s: 'imdb', l: 'Note IMDb', k: 'number', imdb: true },
        { s: 'votes', l: 'Votes IMDb', k: 'number', imdb: true },
        { s: 'critique', l: 'Note critique (%)', k: 'number' },
        { s: 'age', l: 'Âge minimum', k: 'number' },
        { s: 'media', l: 'MediaID (disque)', k: 'text', v: 'MediaIds', select: true },
        { s: 'dispo', l: 'Disque branché', k: 'bool' },
        { s: 'type', l: 'Type', k: 'kind' },
        { s: 'vu', l: 'Déjà vu', k: 'bool' },
        { s: 'favori', l: 'Favori', k: 'bool' },
        { s: 'ajout', l: 'Ajouté il y a', k: 'added' },
        { s: 'tag', l: 'Tag', k: 'text', v: 'Tags' },
        { s: 'studio', l: 'Studio', k: 'text', v: 'Studios' },
        { s: 'titre', l: 'Titre', k: 'text' },
        { s: 'resume', l: 'Résumé', k: 'text' },
        { s: 'tri', l: 'Trier par', k: 'sort' }
    ];
    var OPS = {
        text: [[':', 'contient'], ['=', 'est'], ['!=', "n'est pas"]],
        number: [['>=', '≥'], ['>', '>'], ['<=', '≤'], ['<', '<'], [':', '='], ['..', 'entre']],
        added: [['<', 'moins de'], ['>', 'plus de']],
        bool: [[':', '']],
        kind: [[':', '='], ['!=', '≠']],
        sort: [[':', '']]
    };
    var SORTS = [['note', 'note TMDB'], ['imdb', 'note IMDb'], ['annee', 'année (récents d\'abord)'], ['ancien', 'année (anciens d\'abord)'],
        ['ajout', 'date d\'ajout'], ['titre', 'titre'], ['duree', 'durée'], ['hasard', 'hasard']];

    function field(s) { return FIELDS.filter(function (x) { return x.s === s; })[0]; }

    function quote(v) {
        v = String(v).trim();
        return /[\s()|,:"]/.test(v) ? '"' + v.replace(/"/g, '') + '"' : v;
    }

    function options(pairs) {
        return pairs.map(function (p) { return '<option value="' + esc(p[0]) + '">' + esc(p[1]) + '</option>'; }).join('');
    }

    function valueHtml(name, op) {
        var f = field(name);
        if (f.k === 'bool') { return '<select class="jsearch-value">' + options([['oui', 'oui'], ['non', 'non']]) + '</select>'; }
        if (f.k === 'kind') { return '<select class="jsearch-value">' + options([['film', 'film'], ['serie', 'série'], ['episode', 'épisode'], ['collection', 'collection']]) + '</select>'; }
        if (f.k === 'sort') { return '<select class="jsearch-value">' + options(SORTS) + '</select>'; }
        if (f.k === 'added') { return '<input class="jsearch-value" type="number" min="1" value="30" aria-label="jours"><span>jours</span>'; }
        if (op === '..') {
            return '<input class="jsearch-value" type="text" inputmode="decimal" aria-label="de"><span>et</span><input class="jsearch-value jsearch-high" type="text" inputmode="decimal" aria-label="à">';
        }
        var list = f.v && facets ? facets[f.v] || [] : [];
        if (f.select && list.length) {
            return '<select class="jsearch-value">' + options(list.map(function (g) { return [g, g]; })) + '</select>';
        }
        var dl = list.length ? ' list="' + PREFIX + '-dl-' + f.s + '"' : '';
        return '<input class="jsearch-value" type="text"' + dl + (f.k === 'number' ? ' inputmode="decimal"' : '') + ' aria-label="Valeur">';
    }

    function datalists() {
        if (!facets) { return ''; }
        return [['tag', facets.Tags], ['studio', facets.Studios]].map(function (p) {
            return '<datalist id="' + PREFIX + '-dl-' + p[0] + '">' + (p[1] || []).map(function (v) { return '<option value="' + esc(v) + '">'; }).join('') + '</datalist>';
        }).join('');
    }

    function rowHtml(name) {
        var f = field(name);
        var fields = FIELDS.filter(function (x) { return !x.imdb || (facets && facets.ImdbRatings); });
        var ops = OPS[f.k];
        var op = ops.length > 1 || ops[0][1]
            ? '<select class="jsearch-op" aria-label="Opérateur">' + options(ops) + '</select>'
            : '<input type="hidden" class="jsearch-op" value=":">';
        return '<div class="jsearch-row" data-row>'
            + '<select class="jsearch-field" aria-label="Champ">' + fields.map(function (x) {
                return '<option value="' + x.s + '"' + (x.s === name ? ' selected' : '') + '>' + esc(x.l) + '</option>';
            }).join('') + '</select>'
            + op + '<span class="jsearch-vbox">' + valueHtml(name, ops[0][0]) + '</span>'
            + '<button type="button" class="jsearch-x" title="Retirer ce filtre" aria-label="Retirer ce filtre">×</button></div>';
    }

    function rowSyntax(row) {
        var name = row.querySelector('.jsearch-field').value;
        var op = row.querySelector('.jsearch-op').value;
        var first = row.querySelector('.jsearch-value');
        var v = first ? first.value.trim() : '';
        if (!v) { return ''; }
        if (op === '..') {
            var high = row.querySelector('.jsearch-high');
            return high && high.value.trim() ? name + ':' + v + '..' + high.value.trim() : name + '>=' + v;
        }
        if (name === 'ajout') { return name + op + v + 'j'; }
        return name + op + quote(v);
    }

    function panelSyntax(panel) {
        var words = panel.querySelector('.jsearch-words').value.trim();
        var join = panel.querySelector('.jsearch-join').value;
        var parts = Array.prototype.map.call(panel.querySelectorAll('[data-row]'), rowSyntax).filter(Boolean);
        var sorts = parts.filter(function (p) { return /^tri:/.test(p); });
        parts = parts.filter(function (p) { return !/^tri:/.test(p); });
        var filters = join === 'OU' && parts.length > 1 ? '(' + parts.join(' OU ') + ')' : parts.join(' ');
        return [words, filters].concat(sorts).filter(Boolean).join(' ');
    }

    function preview(panel) {
        panel.querySelector('.jsearch-preview').textContent = panelSyntax(panel);
    }

    function addRow(panel, name) {
        var box = panel.querySelector('.jsearch-rows');
        var tmp = document.createElement('div');
        tmp.innerHTML = rowHtml(name);
        var row = tmp.firstChild;
        box.appendChild(row);
        bindRow(panel, row);
        preview(panel);
        return row;
    }

    function bindRow(panel, row) {
        row.querySelector('.jsearch-field').addEventListener('change', function (e) {
            var tmp = document.createElement('div');
            tmp.innerHTML = rowHtml(e.target.value);
            var fresh = tmp.firstChild;
            row.replaceWith(fresh);
            bindRow(panel, fresh);
            preview(panel);
            var v = fresh.querySelector('.jsearch-value');
            if (v) { v.focus(); }
        });
        var op = row.querySelector('select.jsearch-op');
        if (op) {
            op.addEventListener('change', function () {
                row.querySelector('.jsearch-vbox').innerHTML = valueHtml(row.querySelector('.jsearch-field').value, op.value);
                preview(panel);
            });
        }
        row.querySelector('.jsearch-x').addEventListener('click', function () { row.remove(); preview(panel); });
        row.addEventListener('input', function () { preview(panel); });
        row.addEventListener('change', function () { preview(panel); });
        row.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && e.target.tagName === 'INPUT') { panel.querySelector('[data-act="apply"]').click(); }
        });
    }

    function renderSaved(box) {
        getJson('JellySearch/Saved').then(function (list) {
            if (!list || !list.length) {
                box.innerHTML = '<p style="opacity:.7;margin:.2em 0">Aucune tuile pour l\'instant : lancez une recherche puis « Créer une tuile ».</p>';
                return;
            }
            box.innerHTML = '<h3>Mes tuiles (accueil web et TV)</h3><ul>' + list.map(function (s) {
                return '<li><a href="#/search?query=' + encodeURIComponent(s.Query) + '">' + esc(s.Name) + '</a>'
                    + '<span class="jsearch-q" title="' + esc(s.Description) + '">' + esc(s.Query) + '</span>'
                    + '<button type="button" class="jsearch-x" data-delete="' + esc(s.Id) + '" title="Supprimer la tuile" aria-label="Supprimer la tuile ' + esc(s.Name) + '">×</button></li>';
            }).join('') + '</ul>';
            box.querySelectorAll('[data-delete]').forEach(function (b) {
                b.addEventListener('click', function () {
                    var s = list.filter(function (x) { return x.Id === b.getAttribute('data-delete'); })[0];
                    if (!s || !window.confirm('Supprimer la tuile « ' + s.Name + ' » ?')) { return; }
                    send('DELETE', 'JellySearch/Saved/' + s.Id).then(function () {
                        savedCache = null;
                        toast('Tuile « ' + s.Name + ' » supprimée');
                        renderSaved(box);
                    });
                });
            });
        }, function () { box.innerHTML = ''; });
    }

    var HELP = '<b>Texte libre</b> : titre, titre original, acteurs, réalisateurs, genres, tags, studios et résumé ; accents et fautes de frappe ignorés.'
        + '<ul><li><code>alien</code>, <code>harri poter</code>, <code>spielberg</code>, <code>requin</code></li>'
        + '<li><code>"le parrain"</code> : des mots qui se suivent</li></ul>'
        + '<b>Filtres</b> <code>champ opérateur valeur</code> — opérateurs <code>:</code> <code>=</code> <code>!=</code> <code>&gt;</code> <code>&gt;=</code> <code>&lt;</code> <code>&lt;=</code>, intervalle <code>a..b</code>, plusieurs valeurs <code>a|b</code>'
        + '<ul><li><code>genre:horreur annee:1980..1989</code>, <code>annee:90s</code></li>'
        + '<li><code>tmdb&gt;7</code> (ou <code>note</code>), <code>imdb&gt;=8</code>, <code>votes&gt;100k</code>, <code>critique&gt;80</code>, <code>duree&lt;1h45</code>, <code>age&lt;=12</code></li>'
        + '<li><code>acteur:"jim carrey"</code>, <code>realisateur:spielberg</code>, <code>tag:requin</code>, <code>studio:pixar</code>, <code>titre:alien</code>, <code>resume:vaisseau</code></li>'
        + '<li><code>media:MOVIES03</code>, <code>dispo:oui</code> (disque branché), <code>type:serie</code>, <code>vu:non</code>, <code>favori:oui</code>, <code>ajout&lt;30j</code></li>'
        + '<li><code>tri:note</code>, <code>imdb</code>, <code>annee</code>, <code>ancien</code>, <code>ajout</code>, <code>titre</code>, <code>duree</code>, <code>hasard</code></li></ul>'
        + '<b>Combiner</b> : les termes s\'additionnent (ET) ; <code>OU</code>, <code>NON</code> ou <code>-mot</code>, parenthèses <code>( )</code>.'
        + '<ul><li><code>(genre:western OU genre:comedie) annee&lt;2000 -genre:romance</code></li></ul>'
        + '<b>En phrases</b>, aussi au micro de la télécommande sur la TV :'
        + '<ul><li>« films d\'horreur des années 80 avec Sigourney Weaver »</li><li>« comédies notées plus de 7 sur IMDb »</li>'
        + '<li>« films de Spielberg après 2000 disponibles »</li><li>« séries non vues de moins de 45 minutes »</li><li>« films récemment ajoutés »</li></ul>';

    function ensureSearchTools() {
        var page = searchPage();
        var input = searchInput();
        var fieldsBox = page && page.querySelector('.searchFields');
        if (!fieldsBox || !input) { return; }
        if (page.querySelector('.jsearch-bar')) {
            explainSoon(false);
            return;
        }
        addStyle();

        var bar = document.createElement('div');
        bar.className = 'jsearch-bar';
        bar.innerHTML = (SETTINGS.filterPanel ? '<button type="button" class="jsearch-btn" data-act="filters" aria-expanded="false"><span class="material-icons" aria-hidden="true">tune</span>Filtres</button>' : '')
            + '<button type="button" class="jsearch-btn" data-act="help" aria-expanded="false"><span class="material-icons" aria-hidden="true">help_outline</span>Aide</button>'
            + '<span class="jsearch-understood" aria-live="polite"></span>'
            + '<button type="button" class="jsearch-btn" data-act="save" hidden><span class="material-icons" aria-hidden="true">add_to_queue</span>Créer une tuile</button>';
        fieldsBox.insertAdjacentElement('afterend', bar);

        var save = document.createElement('form');
        save.className = 'jsearch-save';
        save.hidden = true;
        save.innerHTML = '<input type="text" maxlength="60" placeholder="Nom de la tuile" aria-label="Nom de la tuile" required>'
            + '<button type="submit" class="jsearch-btn jsearch-primary">Créer la tuile</button><button type="button" class="jsearch-btn" data-act="cancel">Annuler</button>';
        bar.insertAdjacentElement('afterend', save);

        var help = document.createElement('div');
        help.className = 'jsearch-help';
        help.hidden = true;
        help.innerHTML = HELP;
        save.insertAdjacentElement('afterend', help);

        var panel = null;
        if (SETTINGS.filterPanel) {
            panel = document.createElement('div');
            panel.className = 'jsearch-panel';
            panel.hidden = true;
            panel.innerHTML = '<h3>Composer une recherche</h3>'
                + '<div class="jsearch-row"><input type="text" class="jsearch-words" placeholder="Mots (facultatif)" aria-label="Mots">'
                + '<label>Filtres combinés par <select class="jsearch-join"><option value="ET">ET</option><option value="OU">OU</option></select></label></div>'
                + '<div class="jsearch-rows"></div><span class="jsearch-dls"></span>'
                + '<div class="jsearch-actions"><button type="button" class="jsearch-btn" data-act="add"><span class="material-icons" aria-hidden="true">add</span>Ajouter un filtre</button>'
                + '<button type="button" class="jsearch-btn jsearch-primary" data-act="apply">Rechercher</button>'
                + '<button type="button" class="jsearch-btn" data-act="clear">Effacer</button>'
                + '<span class="jsearch-preview" aria-label="Recherche obtenue"></span></div>'
                + '<div class="jsearch-saved"></div>';
            help.insertAdjacentElement('afterend', panel);
            panel.querySelector('.jsearch-words').addEventListener('input', function () { preview(panel); });
            panel.querySelector('.jsearch-words').addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { panel.querySelector('[data-act="apply"]').click(); }
            });
            panel.querySelector('.jsearch-join').addEventListener('change', function () { preview(panel); });
            panel.querySelector('[data-act="add"]').addEventListener('click', function () {
                loadFacets().then(function () {
                    panel.querySelector('.jsearch-dls').innerHTML = datalists();
                    var row = addRow(panel, panel.querySelectorAll('[data-row]').length ? 'annee' : 'genre');
                    var v = row.querySelector('.jsearch-value');
                    if (v) { v.focus(); }
                });
            });
            panel.querySelector('[data-act="apply"]').addEventListener('click', function () {
                var q = panelSyntax(panel);
                if (q) { setQuery(q); }
            });
            panel.querySelector('[data-act="clear"]').addEventListener('click', function () {
                panel.querySelector('.jsearch-rows').innerHTML = '';
                panel.querySelector('.jsearch-words').value = '';
                preview(panel);
            });
        }

        if (SETTINGS.resultsGrid) {
            var box = document.createElement('div');
            box.className = 'jsearch-results padded-left padded-right';
            box.hidden = true;
            (panel || help).insertAdjacentElement('afterend', box);
            bindResults(box);
        }

        function toggle(el, button) {
            var open = el.hidden;
            el.hidden = !open;
            if (button) {
                button.classList.toggle('on', open);
                button.setAttribute('aria-expanded', open ? 'true' : 'false');
            }
            return open;
        }

        bar.addEventListener('click', function (e) {
            var b = e.target.closest('[data-act]');
            if (!b) { return; }
            var act = b.getAttribute('data-act');
            if (act === 'filters' && panel) {
                if (toggle(panel, b)) {
                    loadFacets().then(function () {
                        panel.querySelector('.jsearch-dls').innerHTML = datalists();
                        if (!panel.querySelector('[data-row]')) {
                            panel.querySelector('.jsearch-words').value = currentQuery();
                            addRow(panel, 'genre');
                        }
                    });
                    renderSaved(panel.querySelector('.jsearch-saved'));
                }
            } else if (act === 'help') {
                toggle(help, b);
            } else if (act === 'save') {
                save.hidden = false;
                var name = save.querySelector('input');
                var suggested = lastExplained && lastExplained.Description && (lastExplained.NaturalLanguage || /[:<>=]/.test(lastExplained.Syntax))
                    ? lastExplained.Description : currentQuery();
                name.value = suggested.charAt(0).toUpperCase() + suggested.slice(1, 60);
                name.focus();
                name.select();
            }
        });
        save.querySelector('[data-act="cancel"]').addEventListener('click', function () { save.hidden = true; });
        save.addEventListener('submit', function (e) {
            e.preventDefault();
            var name = save.querySelector('input').value.trim();
            // the order chosen in the grid goes with the tile
            var q = currentQuery().trim() + (results.q === currentQuery().trim() && results.sort ? ' tri:' + results.sort : '');
            if (!name || !q) { return; }
            send('POST', 'JellySearch/Saved', { Name: name, Query: q }).then(function (s) {
                save.hidden = true;
                savedCache = null;
                toast('Tuile « ' + s.Name + ' » créée : elle apparaît dans « Mes médias », sur le web et la TV.');
                if (panel && !panel.hidden) { renderSaved(panel.querySelector('.jsearch-saved')); }
            }, function () { toast('La tuile n\'a pas pu être créée.'); });
        });

        input.addEventListener('input', function () { explainSoon(false); });
        explainSoon(true);
    }

    function explainSoon(now) {
        var page = searchPage();
        var bar = page && page.querySelector('.jsearch-bar');
        if (!bar) { return; }
        var q = currentQuery().trim();
        if (!now && q === (bar.getAttribute('data-q') || '')) { return; }
        bar.setAttribute('data-q', q);
        clearTimeout(explainTimer);
        explainTimer = setTimeout(function () { explain(bar, q); }, now ? 0 : 450);
        resultsSoon(q, now);
    }

    function explain(bar, q) {
        var out = bar.querySelector('.jsearch-understood');
        bar.querySelector('[data-act="save"]').hidden = !q;
        if (!q || !SETTINGS.explain) {
            out.innerHTML = '';
            lastExplained = null;
            return;
        }
        getJson('JellySearch/Explain', { q: q }).then(function (r) {
            if (bar.getAttribute('data-q') !== q) { return; }
            lastExplained = r;
            var filters = /[:<>=]/.test(r.Syntax) || /\b(OU|NON)\b|(^|\s)-/.test(r.Syntax);
            out.innerHTML = !r.Description || (!r.NaturalLanguage && !filters)
                ? ''
                : '<b>Compris :</b> ' + esc(r.Description) + (r.NaturalLanguage ? '<code>' + esc(r.Syntax) + '</code>' : '');
        });
    }

    /* ---------- 1b. the results: their number, their order, a grid of 4 rows per page ---------- */
    var RESULT_SORTS = [['pertinence', 'Pertinence'], ['imdb', 'Note IMDb'], ['note', 'Note TMDB'], ['critique', 'Note critique'],
        ['annee', 'Année (récents)'], ['ancien', 'Année (anciens)'], ['ajout', 'Ajout récent'], ['titre', 'Titre'], ['duree', 'Durée'], ['hasard', 'Hasard']];
    var KINDS = [['', 'Tout'], ['Movie', 'Films'], ['Series', 'Séries'], ['Episode', 'Épisodes'], ['BoxSet', 'Collections']];
    var KIND_LABEL = { Movie: 'Film', Series: 'Série', Episode: 'Épisode', BoxSet: 'Collection' };
    var ROWS = 4;
    var LIST_PAGE = 30;
    var results = { q: null, sort: '', type: '', start: 0, seq: 0, data: null, items: [] };
    var resultsTimer = null;

    function storage(key, value) {
        try {
            if (value === undefined) { return window.sessionStorage.getItem(PREFIX + '-' + key); }
            window.sessionStorage.setItem(PREFIX + '-' + key, value);
        } catch (e) { /* private mode */ }
        return null;
    }

    function viewMode() {
        try { return window.localStorage.getItem(PREFIX + '-view') === 'list' ? 'list' : 'grid'; } catch (e) { return 'grid'; }
    }

    function setViewMode(mode) {
        try { window.localStorage.setItem(PREFIX + '-view', mode); } catch (e) { /* private mode */ }
    }

    function resultsBox() {
        var page = searchPage();
        return page ? page.querySelector('.jsearch-results') : null;
    }

    // a page fills 4 rows of posters at the current width (30 lines in the list)
    function pageSize(box) {
        if (viewMode() === 'list' || !box) { return LIST_PAGE; }
        // the number of columns the CSS grid makes at this width, measured on an empty grid
        var hidden = box.hidden;
        box.hidden = false;
        var probe = document.createElement('div');
        probe.className = 'jsearch-grid';
        probe.style.cssText = 'visibility:hidden;height:0;overflow:hidden';
        probe.innerHTML = '<div></div>';
        box.appendChild(probe);
        var cols = getComputedStyle(probe).gridTemplateColumns.split(' ').filter(Boolean).length;
        probe.remove();
        box.hidden = hidden;
        return Math.max(2, cols || 0) * ROWS;
    }

    function fr(n, digits) { return Number(n).toFixed(digits).replace('.', ','); }

    function plural(n, one, many) { return n + ' ' + (n > 1 ? many : one); }

    function resultsSoon(q, now) {
        if (!SETTINGS.resultsGrid) { return; }
        clearTimeout(resultsTimer);
        resultsTimer = setTimeout(function () {
            if (q !== results.q) {
                // a new search: its default order (IMDb for filters, relevance for words), first page;
                // coming back to the same search (from a details page) restores its page and order
                var kept = null;
                try { kept = JSON.parse(storage('results') || 'null'); } catch (e) { kept = null; }
                results.q = q;
                results.sort = kept && kept.q === q ? kept.sort : '';
                results.type = kept && kept.q === q ? kept.type : '';
                results.start = kept && kept.q === q ? kept.start : 0;
            }
            loadResults();
        }, now ? 0 : 450);
    }

    function saveResultsState() {
        storage('results', JSON.stringify({ q: results.q, sort: results.sort, type: results.type, start: results.start }));
    }

    function loadResults() {
        var box = resultsBox();
        var q = results.q || '';
        if (!box) { return; }
        if (!q.trim()) {
            box.hidden = true;
            document.body.classList.remove('jsearch-grid-on');
            return;
        }
        var seq = ++results.seq;
        var limit = pageSize(box);
        results.start = Math.floor(results.start / limit) * limit;
        getJson('JellySearch/Results', { q: q, sort: results.sort, types: results.type, start: results.start, limit: limit }).then(function (r) {
            if (seq !== results.seq) { return null; }
            results.data = r;
            if (r.Total > 0 && results.start >= r.Total) {
                results.start = 0;
                return loadResults();
            }
            if (!r.Ids.length) { return renderResults([]); }
            return api().getItems(api().getCurrentUserId(), {
                Ids: r.Ids.join(','),
                Fields: 'PrimaryImageAspectRatio,ProductionYear,Genres,RunTimeTicks',
                EnableImageTypes: 'Primary',
                ImageTypeLimit: 1,
                EnableUserData: true
            }).then(function (items) {
                if (seq !== results.seq) { return; }
                var byId = {};
                (items.Items || []).forEach(function (i) { byId[plainId(i.Id)] = i; });
                renderResults(r.Ids.map(function (id) { return byId[plainId(id)]; }).filter(Boolean));
            });
        }, function () {
            if (seq !== results.seq) { return; }
            box.hidden = true;
            document.body.classList.remove('jsearch-grid-on');
        });
    }

    function plainId(id) { return String(id || '').replace(/-/g, '').toLowerCase(); }

    function imdbOf(r, item) { return r.Imdb[plainId(item.Id)]; }

    function itemHref(item) { return '#/details?id=' + item.Id + '&serverId=' + (item.ServerId || api().serverId()); }

    function posterUrl(item, height) {
        var tag = item.ImageTags && item.ImageTags.Primary;
        return tag ? api().getUrl('Items/' + item.Id + '/Images/Primary', { fillHeight: height, fillWidth: Math.round(height * 2 / 3), quality: 90, tag: tag }) : null;
    }

    function metaHtml(item, imdb, withKind) {
        var parts = [];
        if (withKind && KIND_LABEL[item.Type]) { parts.push(esc(KIND_LABEL[item.Type])); }
        if (item.ProductionYear) { parts.push(String(item.ProductionYear)); }
        if (imdb) {
            parts.push('<span class="jsearch-imdb" title="Note IMDb (' + esc(plural(imdb.Votes, 'vote', 'votes')) + ')"><span class="jsearch-imdb-logo">IMDb</span>' + fr(imdb.Rating, 1) + '</span>');
        } else if (item.CommunityRating) {
            parts.push('<span title="Note TMDB"><span class="jsearch-star">★</span> ' + fr(item.CommunityRating, 1) + '</span>');
        }
        return parts.join('<span aria-hidden="true">·</span>');
    }

    function minutes(item) {
        if (!item.RunTimeTicks) { return ''; }
        var m = Math.round(item.RunTimeTicks / 600000000);
        if (m < 1) { return ''; }
        return m >= 60 ? Math.floor(m / 60) + ' h ' + String(m % 60).padStart(2, '0') : m + ' min';
    }

    function gridHtml(items, r, mixed) {
        return '<div class="jsearch-grid" role="list">' + items.map(function (item) {
            var url = posterUrl(item, 420);
            var played = item.UserData && item.UserData.Played;
            return '<a class="jsearch-card" role="listitem" href="' + esc(itemHref(item)) + '" title="' + esc(item.Name) + '">'
                + '<div class="jsearch-poster' + (url ? '' : ' jsearch-noimg') + '"' + (url ? ' style="background-image:url(\'' + esc(url) + '\')"' : '') + '>'
                + (url ? '' : esc(item.Name))
                + (played ? '<span class="jsearch-played" title="Déjà vu"><span class="material-icons" aria-hidden="true">check</span></span>' : '')
                + (mixed && KIND_LABEL[item.Type] && item.Type !== 'Movie' ? '<span class="jsearch-kind">' + esc(KIND_LABEL[item.Type]) + '</span>' : '')
                + '</div><div class="jsearch-title">' + esc(item.Name) + '</div>'
                + '<div class="jsearch-meta">' + metaHtml(item, imdbOf(r, item), false) + '</div></a>';
        }).join('') + '</div>';
    }

    function listHtml(items, r) {
        return '<table class="jsearch-list"><thead><tr><th></th><th>Titre</th><th class="jsearch-hide-s">Type</th><th>Année</th>'
            + '<th class="jsearch-num">IMDb</th><th class="jsearch-num jsearch-hide-s">TMDB</th><th class="jsearch-num jsearch-hide-s">Durée</th><th class="jsearch-hide-s">Genres</th></tr></thead><tbody>'
            + items.map(function (item) {
                var url = posterUrl(item, 120);
                var imdb = imdbOf(r, item);
                return '<tr><td><a href="' + esc(itemHref(item)) + '" tabindex="-1" aria-hidden="true"><span class="jsearch-thumb"' + (url ? ' style="background-image:url(\'' + esc(url) + '\')"' : '') + '></span></a></td>'
                    + '<td><a href="' + esc(itemHref(item)) + '">' + esc(item.Name) + '</a>' + (item.UserData && item.UserData.Played ? ' <span class="material-icons" title="Déjà vu" style="font-size:1em;vertical-align:-.15em;opacity:.7">check</span>' : '') + '</td>'
                    + '<td class="jsearch-hide-s">' + esc(KIND_LABEL[item.Type] || '') + '</td>'
                    + '<td>' + (item.ProductionYear || '') + '</td>'
                    + '<td class="jsearch-num">' + (imdb ? '<span class="jsearch-imdb" title="' + esc(plural(imdb.Votes, 'vote', 'votes')) + '"><span class="jsearch-imdb-logo">IMDb</span>' + fr(imdb.Rating, 1) + '</span>' : '') + '</td>'
                    + '<td class="jsearch-num jsearch-hide-s">' + (item.CommunityRating ? '<span class="jsearch-star">★</span> ' + fr(item.CommunityRating, 1) : '') + '</td>'
                    + '<td class="jsearch-num jsearch-hide-s">' + minutes(item) + '</td>'
                    + '<td class="jsearch-genres jsearch-hide-s">' + esc((item.Genres || []).slice(0, 3).join(', ')) + '</td></tr>';
            }).join('') + '</tbody></table>';
    }

    function pagerHtml(r, size) {
        var first = r.Total ? results.start + 1 : 0;
        var last = Math.min(r.Total, results.start + size);
        return '<span class="jsearch-pager"><span>' + first + '–' + last + ' sur ' + r.Total + '</span>'
            + '<button type="button" data-page="-1" title="Résultats précédents" aria-label="Résultats précédents"' + (results.start <= 0 ? ' disabled' : '') + '><span class="material-icons" aria-hidden="true">chevron_left</span></button>'
            + '<button type="button" data-page="1" title="Résultats suivants" aria-label="Résultats suivants"' + (last >= r.Total ? ' disabled' : '') + '><span class="material-icons" aria-hidden="true">chevron_right</span></button></span>';
    }

    function renderResults(items) {
        var box = resultsBox();
        var r = results.data;
        if (!box || !r) { return; }
        var all = 0;
        Object.keys(r.Counts).forEach(function (k) { all += r.Counts[k]; });
        if (!all) {
            box.hidden = true;
            document.body.classList.remove('jsearch-grid-on');
            return;
        }
        box.hidden = false;
        document.body.classList.add('jsearch-grid-on');
        var size = pageSize(box);
        var kinds = KINDS.filter(function (k) { return k[0] === '' || r.Counts[k[0]]; });
        var sorts = RESULT_SORTS.filter(function (s) { return s[0] !== 'pertinence' || r.HasText; });
        if (!sorts.some(function (s) { return s[0] === r.Sort; })) { sorts.unshift([r.Sort, r.SortLabel]); }
        var mode = viewMode();
        box.innerHTML = '<div class="jsearch-rhead">'
            + '<span class="jsearch-count" aria-live="polite">' + plural(all, 'résultat', 'résultats')
            + (results.type && r.Total !== all ? '<small>dont ' + r.Total + ' ' + esc(KINDS.filter(function (k) { return k[0] === results.type; })[0][1].toLowerCase()) + '</small>' : '') + '</span>'
            + (kinds.length > 2 ? '<span class="jsearch-tabs" role="group" aria-label="Type">' + kinds.map(function (k) {
                var n = k[0] ? r.Counts[k[0]] : all;
                return '<button type="button" class="jsearch-tab' + (results.type === k[0] ? ' on' : '') + '" data-type="' + k[0] + '" aria-pressed="' + (results.type === k[0]) + '">' + esc(k[1]) + ' (' + n + ')</button>';
            }).join('') + '</span>' : '')
            + '<span class="jsearch-rtools"><label>Trier par <select class="jsearch-rsort" aria-label="Trier par">' + sorts.map(function (s) {
                return '<option value="' + s[0] + '"' + (s[0] === r.Sort ? ' selected' : '') + '>' + esc(s[1]) + '</option>';
            }).join('') + '</select></label>'
            + '<span class="jsearch-views" role="group" aria-label="Affichage">'
            + '<button type="button" data-view="grid" class="' + (mode === 'grid' ? 'on' : '') + '" title="Affiches" aria-label="Affiches" aria-pressed="' + (mode === 'grid') + '"><span class="material-icons" aria-hidden="true">grid_view</span></button>'
            + '<button type="button" data-view="list" class="' + (mode === 'list' ? 'on' : '') + '" title="Liste" aria-label="Liste" aria-pressed="' + (mode === 'list') + '"><span class="material-icons" aria-hidden="true">view_list</span></button></span>'
            + pagerHtml(r, size) + '</span></div>'
            + (items.length ? (mode === 'list' ? listHtml(items, r) : gridHtml(items, r, !results.type && kinds.length > 2)) : '<p class="jsearch-empty">Aucun résultat de ce type.</p>')
            + (r.Total > size ? '<div class="jsearch-rfoot">' + pagerHtml(r, size) + '</div>' : '');
        saveResultsState();
    }

    function bindResults(box) {
        box.addEventListener('click', function (e) {
            var b = e.target.closest('button');
            if (!b || !results.data) { return; }
            var size = pageSize(box);
            if (b.hasAttribute('data-page')) {
                var top = b.closest('.jsearch-rfoot');
                results.start = Math.max(0, results.start + parseInt(b.getAttribute('data-page'), 10) * size);
                loadResults();
                if (top) { window.scrollTo({ top: Math.max(0, box.getBoundingClientRect().top + window.pageYOffset - 90), behavior: 'smooth' }); }
            } else if (b.hasAttribute('data-type')) {
                results.type = b.getAttribute('data-type');
                results.start = 0;
                loadResults();
            } else if (b.hasAttribute('data-view')) {
                setViewMode(b.getAttribute('data-view'));
                results.start = 0;
                loadResults();
            }
        });
        box.addEventListener('change', function (e) {
            if (!e.target.classList.contains('jsearch-rsort')) { return; }
            results.sort = e.target.value;
            results.start = 0;
            loadResults();
        });
    }

    var lastWidth = window.innerWidth;
    window.addEventListener('resize', function () {
        if (Math.abs(window.innerWidth - lastWidth) < 40 || !resultsBox() || !results.data) { return; }
        lastWidth = window.innerWidth;
        clearTimeout(resultsTimer);
        resultsTimer = setTimeout(loadResults, 300);
    });

    /* ---------- 2. saved searches as tiles in My media ---------- */
    var savedCache = null;
    var savedAt = 0;

    function savedSearches() {
        if (savedCache && Date.now() - savedAt < 30000) { return savedCache; }
        savedAt = Date.now();
        savedCache = getJson('JellySearch/Saved').then(function (l) { return l || []; }, function () { return []; });
        return savedCache;
    }

    function ensureHomeTiles() {
        var onHome = /^#\/home(\?|$)/.test(location.hash || '') && !/tab=1/.test(location.hash || '');
        if (!onHome) { return; }
        var libraryCard = document.querySelector('.card[data-type="CollectionFolder"], .card[data-type="UserView"]');
        var container = libraryCard && libraryCard.parentElement;
        if (!container) { return; }
        savedSearches().then(function (list) {
            var wanted = list.map(function (s) { return PREFIX + '-tile-' + s.Id; });
            Array.prototype.forEach.call(container.querySelectorAll('[data-jsearch-tile]'), function (c) {
                if (wanted.indexOf(c.id) < 0) { c.remove(); }
            });
            list.forEach(function (s) {
                var id = PREFIX + '-tile-' + s.Id;
                var existing = document.getElementById(id);
                if (existing && existing.parentElement === container) { return; }
                if (existing) { existing.remove(); }
                var href = '#/search?query=' + encodeURIComponent(s.Query);
                var img = api().getUrl('JellySearch/Saved/' + s.Id + '/Image', { width: 640, v: (SETTINGS.version || '1') + '-' + (s.Name + s.Description).length });
                var card = document.createElement('div');
                card.id = id;
                card.setAttribute('data-jsearch-tile', '');
                card.className = libraryCard.className.split(' ').filter(function (c) { return c === 'card' || /Card$/.test(c) || c === 'card-hoverable'; }).join(' ');
                card.title = s.Description || s.Query;
                card.innerHTML = '<div class="cardBox cardBox-bottompadded"><div class="cardScalable">'
                    + '<div class="cardPadder cardPadder-overflowBackdrop"></div>'
                    + '<a href="' + esc(href) + '" class="cardImageContainer coveredImage cardContent" role="img" aria-label="' + esc(s.Name) + '"'
                    + ' style="background-image:url(\'' + esc(img) + '\')"></a>'
                    + '</div><div class="cardText cardTextCentered cardText-first"><bdi>'
                    + '<a href="' + esc(href) + '" class="textActionButton" title="' + esc(s.Name) + '">' + esc(s.Name) + '</a>'
                    + '</bdi></div></div>';
                container.appendChild(card);
            });
        });
    }

    /* ---------- 3. IMDb rating on the details page ---------- */
    function visibleDetailsPage() {
        var pages = document.querySelectorAll('#itemDetailPage');
        for (var i = 0; i < pages.length; i++) {
            if (!pages[i].classList.contains('hide') && pages[i].offsetParent !== null) { return pages[i]; }
        }
        return null;
    }

    var ratingsLoading = null;
    var ratingsDone = {};

    function ensureRatings() {
        var p = hashParams();
        var id = p.route === '#/details' ? p.params.get('id') : null;
        var page = id && visibleDetailsPage();
        var misc = page && page.querySelector('.itemMiscInfo-primary');
        if (!misc || !misc.children.length) { return; }
        var el = misc.querySelector('.jsearch-imdb');
        if (el && el.getAttribute('data-item') === id) { return; }
        if (el) { el.remove(); }
        if (ratingsLoading === id) { return; }
        var known = ratingsDone[id];
        if (known !== undefined) {
            if (known) { addImdb(misc, id, known); }
            return;
        }
        ratingsLoading = id;
        api().ajax({ type: 'GET', url: api().getUrl('JellySearch/Items/' + id + '/Ratings'), dataType: 'text' }).then(function (body) {
            ratingsLoading = null;
            var r = body ? JSON.parse(body) : null;
            ratingsDone[id] = r && r.Imdb != null ? r : null;
        }, function () { ratingsLoading = null; ratingsDone[id] = null; });
    }

    function addImdb(misc, id, r) {
        addStyle();
        var votes = r.ImdbVotes >= 1000 ? Math.round(r.ImdbVotes / 1000) + ' k votes' : r.ImdbVotes + ' votes';
        var span = document.createElement('div');
        span.className = 'mediaInfoItem jsearch-imdb';
        span.setAttribute('data-item', id);
        span.title = 'Note IMDb (' + votes + ')';
        span.innerHTML = '<span class="jsearch-imdb-logo">IMDb</span>' + esc(r.Imdb.toFixed(1).replace('.', ','));
        var star = misc.querySelector('.starRatingContainer');
        if (star) { star.insertAdjacentElement('afterend', span); } else { misc.appendChild(span); }
    }

    function tick() {
        if (!window.ApiClient || !window.ApiClient.accessToken || !window.ApiClient.accessToken()) { return; }
        if (hashParams().route === '#/search') { ensureSearchTools(); }
        if (SETTINGS.savedTiles) { ensureHomeTiles(); }
        if (SETTINGS.ratingsBadge) { ensureRatings(); }
    }

    setInterval(tick, 700);
    window.addEventListener('hashchange', function () { setTimeout(tick, 80); });
})();
