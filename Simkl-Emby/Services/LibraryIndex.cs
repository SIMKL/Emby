using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

using Simkl.Api.Objects;

namespace Simkl.Services
{
    // Provider-id and season/episode lookups over the items one user can see, built once per import run. Movies and shows
    // (series with their episodes) are read separately and only on first use, so a run with movie changes alone never walks
    // the episodes; Prepare reads up front what a run already knows it needs.
    public class LibraryIndex
    {
        // Emby provider key -> Simkl id name; tvdb only identifies series and episodes
        private static readonly (string emby, string simkl)[] Providers =
        {
            ("Tvdb", "tvdb"), ("Imdb", "imdb"), ("Tmdb", "tmdb"), ("AniDB", "anidb"), ("MyAnimeList", "mal"), ("AniList", "anilist")
        };

        private readonly ILibraryManager _libraryManager;
        private readonly User _user;
        private readonly CancellationToken _cancellationToken;
        private bool _moviesRead, _showsRead;
        private readonly Dictionary<string, BaseItem> _movies = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Series> _series = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Episode> _episodesByCoords = new Dictionary<string, Episode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Episode> _episodesByTvdbId = new Dictionary<string, Episode>(StringComparer.OrdinalIgnoreCase);

        public int MovieCount { get; private set; }
        public int SeriesCount { get; private set; }
        public int EpisodeCount { get; private set; }

        public LibraryIndex(ILibraryManager libraryManager, User user, CancellationToken cancellationToken)
        {
            _libraryManager = libraryManager;
            _user = user;
            _cancellationToken = cancellationToken;
        }

        // types are Simkl's buckets: movies, shows, anime (which holds shows and movies)
        public void Prepare(IEnumerable<string> types)
        {
            var list = types.ToList();
            if (list.Contains("movies") || list.Contains("anime")) ReadMovies();
            if (list.Contains("shows") || list.Contains("anime")) ReadShows();
        }

        public string Describe()
        {
            return "indexed " + (_moviesRead ? MovieCount + " movies" : "no movies (not needed)") + ", "
                + (_showsRead ? SeriesCount + " series, " + EpisodeCount + " episodes" : "no series (not needed)");
        }

        private void ReadMovies()
        {
            if (_moviesRead) return;
            _moviesRead = true;
            foreach (var item in Query("Movie"))
            {
                MovieCount++;
                Add(_movies, item, ProviderKeys(item, false));
            }
        }

        private void ReadShows()
        {
            if (_showsRead) return;
            _showsRead = true;
            foreach (var item in Query("Series"))
            {
                if (!(item is Series series)) continue;
                SeriesCount++;
                Add(_series, series, ProviderKeys(series, true));
            }
            foreach (var item in Query("Episode"))
            {
                if (!(item is Episode episode)) continue;
                EpisodeCount++;
                AddEpisode(episode);
            }
        }

        // asked from the user root folder, the query applies library access and the parental policy: the same primitive Emby
        // uses for its own visibility check, and one that has not changed since Emby 4.8
        private BaseItem[] Query(string type)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _libraryManager.GetUserRootFolder().GetItemList(new InternalItemsQuery(_user)
            {
                IncludeItemTypes = new[] { type },
                Recursive = true,
                IsVirtualItem = false,
                EnableTotalRecordCount = false,
                DtoOptions = new DtoOptions { EnableImages = false, EnableUserData = false }
            });
        }

        private static IEnumerable<string> ProviderKeys(BaseItem item, bool withTvdb)
        {
            var ids = item.ProviderIds;
            if (ids == null) yield break;
            foreach (var (emby, simkl) in Providers)
            {
                if (!withTvdb && simkl == "tvdb") continue;
                if (ids.TryGetValue(emby, out var value) && !string.IsNullOrEmpty(value)) yield return simkl + ":" + value;
            }
        }

        private static void Add<T>(Dictionary<string, T> index, T item, IEnumerable<string> keys)
        {
            foreach (var key in keys) Put(index, key, item);
        }

        // first item wins so duplicates in the library stay deterministic
        private static void Put<T>(Dictionary<string, T> index, string key, T item)
        {
            if (!index.ContainsKey(key)) index[key] = item;
        }

        private void AddEpisode(Episode episode)
        {
            var ids = episode.ProviderIds;
            if (ids != null && ids.TryGetValue("Tvdb", out var tvdbId) && !string.IsNullOrEmpty(tvdbId)) Put(_episodesByTvdbId, "tvdb:" + tvdbId, episode);

            if (episode.ParentIndexNumber == null || episode.IndexNumber == null) return;
            var first = episode.IndexNumber.Value;
            var last = Math.Max(first, episode.IndexNumberEnd ?? first);    // a multi-episode file covers a range
            for (var number = first; number <= last; number++)
            {
                Put(_episodesByCoords, Coord(episode.SeriesId, episode.ParentIndexNumber.Value, number), episode);
            }
        }

        private static string Coord(long seriesId, int season, int number)
        {
            return seriesId + ":" + season + ":" + number;
        }

        public BaseItem FindMovie(SimklIds ids)
        {
            ReadMovies();
            return Find(_movies, ids, false, out _);
        }

        // matchedBy tells the caller how the library numbers episodes: tvdb/imdb/tmdb = TVDB seasons, anidb/mal/anilist = absolute
        public Series FindSeries(SimklIds ids, out string matchedBy)
        {
            ReadShows();
            return Find(_series, ids, true, out matchedBy);
        }

        public Episode FindEpisode(long seriesId, int season, int number)
        {
            ReadShows();
            return Get(_episodesByCoords, Coord(seriesId, season, number));
        }

        public Episode FindEpisodeByTvdbId(int tvdbId)
        {
            ReadShows();
            return Get(_episodesByTvdbId, "tvdb:" + tvdbId);
        }

        private static T Get<T>(Dictionary<string, T> index, string key) where T : class
        {
            return index.TryGetValue(key, out var item) ? item : null;
        }

        private static T Find<T>(Dictionary<string, T> index, SimklIds ids, bool withTvdb, out string matchedBy) where T : class
        {
            foreach (var (provider, value) in SimklKeys(ids, withTvdb))
            {
                if (string.IsNullOrEmpty(value)) continue;
                var item = Get(index, provider + ":" + value);
                if (item == null) continue;
                matchedBy = provider;
                return item;
            }
            matchedBy = null;
            return null;
        }

        private static IEnumerable<(string provider, string value)> SimklKeys(SimklIds ids, bool withTvdb)
        {
            if (withTvdb) yield return ("tvdb", ids.tvdb);
            yield return ("imdb", ids.imdb);
            yield return ("tmdb", ids.tmdb);
            yield return ("anidb", ids.anidb);
            yield return ("mal", ids.mal);
            yield return ("anilist", ids.anilist);
        }
    }
}
