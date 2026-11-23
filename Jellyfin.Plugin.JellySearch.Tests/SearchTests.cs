using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellySearch.Search;
using Xunit;

namespace Jellyfin.Plugin.JellySearch.Tests;

/// <summary>A small library to search in.</summary>
internal static class Library
{
    public static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static SearchDocument Movie(string name, int year, string[] genres, double rating, string director, string[] actors,
        double runtime = 110, string? overview = null, string? original = null, string[]? tags = null, string? media = null, string? imdb = null,
        string? official = null, int addedDaysAgo = 400)
        => new()
        {
            Id = Guid.NewGuid(),
            Kind = DocKind.Movie,
            Name = name,
            OriginalTitle = original,
            Year = year,
            Genres = genres,
            CommunityRating = rating,
            RuntimeMinutes = runtime,
            Overview = overview,
            Tags = tags ?? [],
            People = actors.Select(a => new DocPerson(a, PersonRole.Actor)).Append(new DocPerson(director, PersonRole.Director)).ToList(),
            MediaId = media,
            ImdbId = imdb,
            OfficialRating = official,
            DateCreated = Now.AddDays(-addedDaysAgo)
        };

    public static readonly List<SearchDocument> Docs =
    [
        Movie("Alien, le huitième passager", 1979, ["Horreur", "Science-Fiction"], 8.1, "Ridley Scott", ["Sigourney Weaver", "Tom Skerritt"], 117,
            "Le vaisseau spatial Nostromo recueille une créature hostile.", "Alien", media: "MOVIES03", imdb: "tt0078748", official: "FR-12"),
        Movie("Aliens, le retour", 1986, ["Action", "Science-Fiction"], 7.9, "James Cameron", ["Sigourney Weaver", "Michael Biehn"], 137, original: "Aliens", media: "MOVIES03", imdb: "tt0090605"),
        Movie("Le Fabuleux Destin d'Amélie Poulain", 2001, ["Comédie", "Romance"], 7.9, "Jean-Pierre Jeunet", ["Audrey Tautou", "Mathieu Kassovitz"], 122,
            "Amélie, une serveuse de Montmartre, décide de faire le bonheur des autres.", media: "MOVIES05", official: "TP", addedDaysAgo: 3),
        Movie("Harry Potter à l'école des sorciers", 2001, ["Aventure", "Fantastique"], 7.9, "Chris Columbus", ["Daniel Radcliffe", "Emma Watson"], 152,
            "Un jeune orphelin découvre qu'il est un sorcier.", "Harry Potter and the Philosopher's Stone", media: "MOVIES05", official: "TP"),
        Movie("Danse avec les loups", 1990, ["Western", "Drame"], 7.8, "Kevin Costner", ["Kevin Costner", "Mary McDonnell"], 181, media: "MOVIES05"),
        Movie("The Mask", 1994, ["Comédie", "Fantastique"], 6.9, "Chuck Russell", ["Jim Carrey", "Cameron Diaz"], 101),
        Movie("Dumb & Dumber", 1994, ["Comédie"], 6.6, "Peter Farrelly", ["Jim Carrey", "Jeff Daniels"], 107),
        Movie("Déjà vu", 2006, ["Action", "Thriller"], 6.9, "Tony Scott", ["Denzel Washington"], 126),
        Movie("Jurassic Park", 1993, ["Aventure", "Science-Fiction"], 7.9, "Steven Spielberg", ["Sam Neill", "Laura Dern"], 127, imdb: "tt0107290"),
        Movie("Les Dents de la mer", 1975, ["Horreur", "Thriller"], 7.7, "Steven Spielberg", ["Roy Scheider"], 124, tags: ["requin", "plage"], original: "Jaws"),
        Movie("Arrête-moi si tu peux", 2002, ["Crime", "Drame"], 7.9, "Steven Spielberg", ["Leonardo DiCaprio", "Tom Hanks"], 141),
        Movie("La Guerre des étoiles", 1977, ["Science-Fiction", "Aventure"], 8.2, "George Lucas", ["Mark Hamill"], 121),
        Movie("1984", 1984, ["Drame"], 6.8, "Michael Radford", ["John Hurt"], 113),
        Movie("Amour", 2012, ["Drame", "Romance"], 7.6, "Michael Haneke", ["Jean-Louis Trintignant"], 127),
        new SearchDocument { Id = Guid.NewGuid(), Kind = DocKind.Series, Name = "Breaking Bad", Year = 2008, Genres = ["Drame", "Crime"], CommunityRating = 8.9, RuntimeMinutes = 47, DateCreated = Now.AddDays(-100) },
        new SearchDocument { Id = Guid.NewGuid(), Kind = DocKind.Episode, Name = "Pilot", SeriesName = "Breaking Bad", Year = 2008, Genres = ["Drame"], RuntimeMinutes = 58, DateCreated = Now.AddDays(-100) }
    ];

    public static readonly SearchIndex Index = SearchIndex.Build(Docs);

    public static SearchContext Context(HashSet<Guid>? played = null) => new()
    {
        Now = Now,
        Played = () => played ?? [],
        IsMediaPresent = m => m != "MOVIES03",
        Imdb = id => id switch { "tt0078748" => (8.5, 950000), "tt0090605" => (8.4, 750000), "tt0107290" => (8.2, 1100000), _ => null }
    };

    public static List<string> Names(string query, SearchContext? context = null)
        => SearchEngine.Search(Index, query, context ?? Context()).Docs.Select(i => Docs[i].Name).ToList();
}

public class TextTests
{
    [Fact]
    public void Folding_removes_accents_case_and_punctuation()
    {
        Assert.Equal("l ete meurtrier", TextFold.Fold("L'Été meurtrier"));
        Assert.Equal("coeur de pirate", TextFold.Fold("Cœur de pirate"));
        Assert.Equal("note 7.5", TextFold.Fold("note 7,5"));
        Assert.Equal("comedie", TextFold.Singular("comedies"));
        Assert.Equal("animal", TextFold.Singular("animaux"));
    }

    [Fact]
    public void Typos_are_counted_with_swaps()
    {
        Assert.Equal(1, Fuzzy.Distance("ailen", "alien", 2));
        Assert.Equal(1, Fuzzy.Distance("spilberg", "spielberg", 2));
        Assert.True(Fuzzy.Distance("alien", "matrix", 2) > 2);
        Assert.False(Fuzzy.Similar("son", "sun"));
    }
}

public class FreeTextTests
{
    [Fact]
    public void Accents_are_ignored()
    {
        Assert.Equal("Le Fabuleux Destin d'Amélie Poulain", Library.Names("amelie").First());
        Assert.Equal("La Guerre des étoiles", Library.Names("guerre des etoiles").First());
    }

    [Fact]
    public void Typos_are_forgiven()
    {
        Assert.Equal("Alien, le huitième passager", Library.Names("ailen").First());
        Assert.Equal("Harry Potter à l'école des sorciers", Library.Names("harri poter").First());
        Assert.Equal(3, Library.Names("spilberg").Count);
    }

    [Fact]
    public void Overviews_people_tags_and_original_titles_are_searched()
    {
        Assert.Contains("Alien, le huitième passager", Library.Names("vaisseau"));
        Assert.Equal(["Alien, le huitième passager", "Aliens, le retour"], Library.Names("sigourney").OrderBy(n => n).ToList());
        Assert.Equal("Les Dents de la mer", Library.Names("requin").Single());
        Assert.Equal("Les Dents de la mer", Library.Names("jaws").First());
    }

    [Fact]
    public void Titles_rank_before_people_and_overviews()
    {
        // "harry" is in a title; nothing else
        Assert.Equal("Harry Potter à l'école des sorciers", Library.Names("harry").First());

        // the title typed exactly comes first
        Assert.Equal("Alien, le huitième passager", Library.Names("alien le huitieme passager").First());
    }

    [Fact]
    public void A_word_being_typed_matches_as_a_prefix()
    {
        Assert.Equal("Jurassic Park", Library.Names("jura").First());
    }

    [Fact]
    public void A_year_typed_alone_is_also_a_year()
    {
        Assert.Equal("1984", Library.Names("1984").First());
    }
}

public class FilterTests
{
    [Fact]
    public void Numbers_compare()
    {
        Assert.All(Library.Names("note>7.9"), n => Assert.Contains(n, new[] { "Alien, le huitième passager", "La Guerre des étoiles", "Breaking Bad" }));
        Assert.Equal(["Dumb & Dumber", "The Mask"], Library.Names("annee:1990..1999 genre:comedie").OrderBy(n => n).ToList());
        Assert.Equal(["Dumb & Dumber", "The Mask"], Library.Names("annee:90s comédie").OrderBy(n => n).ToList());
        Assert.DoesNotContain("Danse avec les loups", Library.Names("duree<2h type:film"));
        Assert.Contains("Danse avec les loups", Library.Names("duree>=3h"));
        Assert.Equal(["Alien, le huitième passager", "Aliens, le retour"], Library.Names("imdb>=8.4").OrderBy(n => n).ToList());
        Assert.Equal("Jurassic Park", Library.Names("votes>1m").Single());
        Assert.DoesNotContain("Alien, le huitième passager", Library.Names("age<=10"));
        Assert.Contains("Le Fabuleux Destin d'Amélie Poulain", Library.Names("age<=10"));
    }

    [Fact]
    public void Booleans_and_or_not()
    {
        Assert.Equal(["Danse avec les loups", "Dumb & Dumber", "The Mask"],
            Library.Names("(genre:western OU genre:comedie) annee<2000").OrderBy(n => n).ToList());
        Assert.Equal(["Danse avec les loups", "Dumb & Dumber", "The Mask"],
            Library.Names("genre:western|comedie -genre:romance").OrderBy(n => n).ToList());
        Assert.Empty(Library.Names("NON genre:comedie annee:1994"));
        Assert.Equal(["Les Dents de la mer"], Library.Names("realisateur:spielberg AND tag:requin"));
    }

    [Fact]
    public void Media_availability_type_watched_and_added()
    {
        Assert.Equal(2, Library.Names("media:MOVIES03").Count);
        Assert.Equal(["Alien, le huitième passager", "Aliens, le retour"], Library.Names("dispo:non").OrderBy(n => n).ToList());
        Assert.Equal("Breaking Bad", Library.Names("type:serie").Single());
        var amelie = Library.Docs.First(d => d.Name.StartsWith("Le Fabuleux", StringComparison.Ordinal)).Id;
        Assert.DoesNotContain("Le Fabuleux Destin d'Amélie Poulain", Library.Names("vu:non type:film", Library.Context([amelie])));
        Assert.Equal("Le Fabuleux Destin d'Amélie Poulain", Library.Names("ajout<7j").Single());
    }

    [Fact]
    public void Filter_only_searches_skip_episodes_unless_asked()
    {
        Assert.DoesNotContain("Pilot", Library.Names("genre:drame"));
        Assert.Contains("Pilot", Library.Names("type:episode"));
        Assert.Contains("Pilot", Library.Names("pilot"));
    }

    [Fact]
    public void Sort_orders()
    {
        Assert.Equal("Amour", Library.Names("genre:drame type:film tri:annee").First());
        Assert.Equal("1984", Library.Names("genre:drame type:film tri:ancien").First());
        Assert.Equal("La Guerre des étoiles", Library.Names("genre:science-fiction").First());
    }

    [Fact]
    public void Syntax_variants()
    {
        Assert.Equal(Library.Names("note>7.8"), Library.Names("note > 7,8"));
        Assert.Equal(Library.Names("acteur:\"jim carrey\""), Library.Names("avec:carrey"));
    }
}

public class PhraseTests
{
    [Fact]
    public void Genre_decade_and_type()
    {
        Assert.Equal(["Alien, le huitième passager", "Les Dents de la mer"], Library.Names("films d'horreur des années 70").OrderBy(n => n).ToList());
    }

    [Fact]
    public void People_with_avec_and_de()
    {
        Assert.Equal(["Dumb & Dumber", "The Mask"], Library.Names("comédies des années 90 avec Jim Carrey").OrderBy(n => n).ToList());
        Assert.Equal(["Arrête-moi si tu peux", "Jurassic Park"], Library.Names("films de spielberg après 1990").OrderBy(n => n).ToList());
    }

    [Fact]
    public void Titles_stay_titles()
    {
        Assert.Equal("Danse avec les loups", Library.Names("danse avec les loups").First());
        Assert.Equal("Déjà vu", Library.Names("deja vu").First());
        Assert.Equal("La Guerre des étoiles", Library.Names("la guerre des étoiles").First());
    }

    [Fact]
    public void Ratings_runtime_and_state()
    {
        Assert.Equal(["Alien, le huitième passager", "La Guerre des étoiles"], Library.Names("films notés plus de 8").OrderBy(n => n).ToList());
        Assert.Equal(["Alien, le huitième passager", "Aliens, le retour"], Library.Names("films avec une note imdb supérieure à 8.3").OrderBy(n => n).ToList());
        Assert.Contains("Danse avec les loups", Library.Names("films de plus de 3 heures"));
        Assert.DoesNotContain("Danse avec les loups", Library.Names("westerns de moins de 2 heures"));
        Assert.Equal(["Alien, le huitième passager", "Aliens, le retour"], Library.Names("films non disponibles").OrderBy(n => n).ToList());
        Assert.Equal("Breaking Bad", Library.Names("séries").Single());
        Assert.Equal("Le Fabuleux Destin d'Amélie Poulain", Library.Names("films récemment ajoutés").Single());
        Assert.Equal(["Alien, le huitième passager", "Aliens, le retour"], Library.Names("sur le disque MOVIES03").OrderBy(n => n).ToList());
    }

    [Fact]
    public void Joined_with_ou()
    {
        Assert.Equal(["Danse avec les loups", "Dumb & Dumber", "The Mask"],
            Library.Names("westerns ou comédies des années 90").OrderBy(n => n).ToList());
    }

    [Fact]
    public void Understanding_is_described()
    {
        var q = QueryParser.Parse("films d'horreur des années 80 notés plus de 7", Library.Index);
        Assert.True(q.NaturalLanguage);
        Assert.Equal("films · genre Horreur · années 80 · note TMDB > 7", q.Description);
        Assert.Contains("type:film", q.Syntax);
        Assert.Contains("annee:1980..1989", q.Syntax);
        Assert.Contains("tmdb>7", q.Syntax);

        // the syntax reads back the same
        var again = QueryParser.Parse(q.Syntax, Library.Index);
        Assert.Equal(Library.Names(q.Syntax), Library.Names("films d'horreur des années 80 notés plus de 7"));
        Assert.Equal(q.Syntax, again.Syntax);
        Assert.Equal("type:film genre=Horreur annee:1980..1989 tmdb>7", q.Syntax);
        Assert.Equal("avec Jim Carrey · réalisé par Steven Spielberg · après 1990 · non vus", QueryParser.Parse("acteur:\"Jim Carrey\" realisateur:spielberg annee>1990 vu:non", Library.Index).Description);
    }
}

public class PluginPartsTests
{
    [Fact]
    public void Tv_ratings_line_is_french()
    {
        Assert.Equal("⭐ IMDb **8,5** (950 k votes) · TMDB **8,1**", Jellyfin.Plugin.JellySearch.Ratings.TvRatingsMiddleware.Line(8.5, 950000, 8.1));
        Assert.Equal("⭐ IMDb **7,0** (2,1 k votes)", Jellyfin.Plugin.JellySearch.Ratings.TvRatingsMiddleware.Line(7, 2100, null));
        var json = Jellyfin.Plugin.JellySearch.Ratings.TvRatingsMiddleware.AddToOverview("{\"Id\":\"x\",\"Overview\":\"Résumé\"}", "L");
        Assert.Contains("\"Overview\":\"L\\n\\nR", json);
        Assert.Null(Jellyfin.Plugin.JellySearch.Ratings.TvRatingsMiddleware.AddToOverview("[1]", "L"));
    }

    [Fact]
    public void Tile_colours_and_image()
    {
        Assert.Equal("#ff0000", Jellyfin.Plugin.JellySearch.Saved.TileImage.Hsl(0, 1, 0.5));
        Assert.Equal("#00ff00", Jellyfin.Plugin.JellySearch.Saved.TileImage.Hsl(120, 1, 0.5));
        var svg = Jellyfin.Plugin.JellySearch.Saved.TileImage.Svg("Horreur <70>", "films · genre Horreur");
        Assert.Contains("Horreur &lt;70&gt;", svg);
        Assert.DoesNotContain("<70>", svg);
    }

    [Fact]
    public void Media_id_is_the_folder_after_the_base_path()
    {
        Assert.Equal("MOVIES03", Jellyfin.Plugin.JellySearch.Media.MediaPresence.GetMediaId("/mnt/usb/MOVIES03/MOVIES03/Alien.mkv", "/mnt/usb/"));
        Assert.Equal("MOVIES03", Jellyfin.Plugin.JellySearch.Media.MediaPresence.GetMediaId("/mnt/usb/MOVIES03/Alien.mkv", "/mnt/usb"));
        Assert.Null(Jellyfin.Plugin.JellySearch.Media.MediaPresence.GetMediaId("/srv/films/Alien.mkv", "/mnt/usb/"));
    }

    [Fact]
    public void Web_requests_are_split_between_JellySearch_and_Jellyfin()
    {
        var web = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["includeItemTypes"] = new(["Movie", "Series", "MusicAlbum", "Audio"])
        });
        var asked = Jellyfin.Plugin.JellySearch.Web.SearchMiddleware.KindsAsked(web)!;
        Assert.Equal(new HashSet<DocKind> { DocKind.Movie, DocKind.Series }, asked.Ours);
        Assert.Equal(2, asked.Others.Count);

        var videos = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["mediaTypes"] = "Video" });
        Assert.Null(Jellyfin.Plugin.JellySearch.Web.SearchMiddleware.KindsAsked(videos));

        var people = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["includeItemTypes"] = "Person" });
        Assert.Empty(Jellyfin.Plugin.JellySearch.Web.SearchMiddleware.KindsAsked(people)!.Ours);
    }
}
