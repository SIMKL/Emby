using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Simkl.Api;
using Simkl.Api.Exceptions;
using Simkl.Api.Objects;
using Simkl.Api.Responses;
using Simkl.Configuration;

using MediaBrowser.Common;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;

namespace Simkl.Services
{
    // "Mark played" done by hand in Emby (a movie, an episode, a whole season or series) goes to Simkl's history under the
    // same rules as scrobbling: the user is logged in and the movie or show checkbox is on. Playback and the plugin's own
    // import save user data with other reasons and stay with the scrobbler and the importer. A single unmark is mirrored as a
    // removal from the Simkl history; several unmarks at once (a season, a series, a clean-up) are deliberately not.
    public class WatchedMarks : IServerEntryPoint
    {
        private const int HoldMs = 2000;    // a season or series mark arrives as one event per episode; one request carries them all

        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;
        private SimklApi _api;
        private readonly ConcurrentDictionary<long, Batch> _batches = new ConcurrentDictionary<long, Batch>();

        private class Batch
        {
            public readonly List<Mark> Marks = new List<Mark>();    // Marks, Unmarked, FolderUnmarked and Pending are touched under lock (this)
            public readonly HashSet<long> Unmarked = new HashSet<long>();   // every item unmarked in this hold, eligible or not
            public bool FolderUnmarked;                              // a season, series or other folder was unmarked: bulk by definition
            public CancellationTokenSource Pending;
            public readonly SemaphoreSlim Sending = new SemaphoreSlim(1, 1);    // one request per user at a time; a burst that ends while another is sending waits
        }

        private class Mark
        {
            public BaseItem Item;
            public bool Played;         // false = unmarked
            public DateTime WatchedAt;
        }

        public WatchedMarks(IJsonSerializer json, ILogManager logManager, IHttpClient httpClient, IUserManager userManager, IUserDataManager userDataManager, IApplicationHost appHost)
        {
            _userDataManager = userDataManager;
            _userManager = userManager;
            _logger = logManager.GetLogger("Simkl Watched");
            _api = new SimklApi(json, _logger, httpClient, userManager, appHost);
        }

        public void Run()
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
        }

        public void Dispose()
        {
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            _api = null;
        }

        // raised inline by Emby's save: it has to stay quick and must never throw
        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            try
            {
                if (e.SaveReason != UserDataSaveReason.TogglePlayed || e.UserData == null || e.User == null || e.Item == null) return;
                var item = e.Item;
                var played = e.UserData.Played;
                var eligible = (item is Movie || item is Episode) && e.User.IsGrantedAccessToFeature(Plugin.StaticId) && Eligible(e.User, item);
                if (played && !eligible) return;

                var batch = _batches.GetOrAdd(e.User.InternalId, id => new Batch());
                CancellationTokenSource pending;
                lock (batch)
                {
                    // every unmark counts towards "several at once", eligible or not: a season or series unmark fires one event per
                    // episode (and one for the folder itself), and a short or excluded episode must not make the rest look single
                    if (!played)
                    {
                        batch.Unmarked.Add(item.InternalId);
                        if (item.IsFolder) batch.FolderUnmarked = true;
                    }
                    if (eligible)
                    {
                        // a mark and an unmark of the same item inside one hold cancel out (an accidental click): neither is sent
                        var opposite = batch.Marks.FindIndex(m => m.Item.InternalId == item.InternalId && m.Played != played);
                        if (opposite >= 0) batch.Marks.RemoveAt(opposite);
                        else batch.Marks.Add(new Mark { Item = item, Played = played, WatchedAt = e.UserData.LastPlayedDate?.UtcDateTime ?? DateTime.UtcNow });
                    }
                    batch.Pending?.Cancel();
                    pending = batch.Pending = new CancellationTokenSource();
                }
                _ = Flush(e.User, batch, pending);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Couldn't queue a watched mark for Simkl", ex);
            }
        }

        private bool Eligible(User user, BaseItem item)
        {
            var config = ConfigurationFactory.LoadUser(_userManager, user.InternalId);
            return config != null && !string.IsNullOrEmpty(config.userToken) && Scrobbler.canBeScrobbled(config, item);
        }

        // waits for the burst to end, then sends everything collected for the user in one request
        private async Task Flush(User user, Batch batch, CancellationTokenSource pending)
        {
            try
            {
                try
                {
                    await Task.Delay(HoldMs, pending.Token);
                }
                catch (OperationCanceledException)
                {
                    return;     // a newer mark restarted the hold and flushes for both
                }

                List<Mark> marks;
                bool bulkUnmark;
                lock (batch)
                {
                    if (batch.Pending != pending) return;
                    marks = new List<Mark>(batch.Marks);
                    bulkUnmark = batch.Unmarked.Count > 1 || batch.FolderUnmarked;
                    batch.Marks.Clear();
                    batch.Unmarked.Clear();
                    batch.FolderUnmarked = false;
                    batch.Pending = null;
                }

                var config = ConfigurationFactory.LoadUser(_userManager, user.InternalId);   // the user may have logged out during the hold
                if (config == null || string.IsNullOrEmpty(config.userToken)) return;

                await batch.Sending.WaitAsync();
                try
                {
                    await SendMarks(user, config, marks.Where(m => m.Played).ToList(), false);
                    // an unmark is mirrored only when it stands alone: several at once are a season, a series or a clean-up in
                    // Emby, not something to wipe from a Simkl history
                    var unmarks = marks.Where(m => !m.Played).ToList();
                    if (unmarks.Count > 0 && bulkUnmark) _logger.Info("{0} unmarked several items at once in Emby; only a single unmark is sent to Simkl", user.Name);
                    else await SendMarks(user, config, unmarks, true);
                }
                finally
                {
                    batch.Sending.Release();
                }
            }
            catch (InvalidTokenException)
            {
                _logger.Info("Deleted Simkl token of " + user.Name);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Couldn't mark watched items on Simkl for " + user.Name, ex);
            }
        }

        private async Task SendMarks(User user, UserConfig config, List<Mark> marks, bool remove)
        {
            var request = Build(marks, out var skipped);
            var count = request.movies.Count + request.episodes.Count + request.shows.Sum(s => s.seasons.Sum(x => x.episodes.Count));
            if (count == 0) return;

            _logger.Info("Simkl history: {0} {1} items {2} {3} in Emby{4}", remove ? "removing" : "sending", count, user.Name, remove ? "unmarked" : "marked as watched", skipped == 0 ? "" : " (" + skipped + " without usable ids skipped)");
            var response = await Deliver(request, config, user, remove);
            var counts = remove ? response?.deleted : response?.added;
            var notFound = (response?.not_found?.movies?.Count ?? 0) + (response?.not_found?.shows?.Count ?? 0) + (response?.not_found?.episodes?.Count ?? 0);
            _logger.Info("Simkl answered for {0}: {1} movies and {2} episodes {3}{4}", user.Name, counts?.movies ?? 0, counts?.episodes ?? 0, remove ? "removed" : "added", notFound == 0 ? "" : ", " + notFound + " not found on Simkl");
        }

        // Simkl answers 400 RATE_LIMIT while its 20-second per-user lock is held and may be briefly unreachable: one retry, then the
        // marks are given up with the error in the log
        private async Task<SyncHistoryResponse> Deliver(HistoryRequest request, UserConfig config, User user, bool remove)
        {
            Func<Task<SyncHistoryResponse>> call = () => remove ? _api.RemoveFromHistory(request, config, user.InternalId) : _api.AddToHistory(request, config, user.InternalId);
            try
            {
                return await call();
            }
            catch (HttpException ex)
            {
                _logger.Info("Simkl rejected the history update for {0} ({1}), retrying once in 21s", user.Name, ex.StatusCode.HasValue ? "HTTP " + (int)ex.StatusCode.Value : ex.Message);
                await Task.Delay(21000);
                return await call();
            }
        }

        // one request for the whole batch: movies with their date; episodes under their show with season, number and their own
        // TVDB/AniDB ids (matched within the show, number as the fallback, anime seasons mapped server-side); the bare episode route
        // only for an episode Emby cannot place in a show
        private static HistoryRequest Build(List<Mark> marks, out int skipped)
        {
            var request = new HistoryRequest();
            var shows = new Dictionary<long, HistoryShow>();
            var seen = new HashSet<string>();   // alternate versions of one item arrive as separate marks
            skipped = 0;

            foreach (var mark in marks)
            {
                var watchedAt = HistoryImporter.Stamp(mark.WatchedAt);
                if (mark.Item is Movie movie)
                {
                    var ids = new SimklIds(movie.ProviderIds);
                    if (!seen.Add("m" + (ids.imdb ?? ids.tmdb ?? "#" + movie.InternalId))) continue;
                    request.movies.Add(new HistoryMovie { title = movie.OriginalTitle ?? movie.Name, year = movie.ProductionYear, ids = ids, watched_at = watchedAt });
                    continue;
                }

                var episode = (Episode)mark.Item;
                var first = episode.IndexNumber ?? 0;
                var last = Math.Max(first, episode.IndexNumberEnd ?? 0);   // a multi-episode file covers a range
                var series = episode.Series;
                if (series == null || episode.ParentIndexNumber == null || episode.IndexNumber == null)
                {
                    // without a show and a number the episode id is the only route left; on that top-level route the server reads
                    // an anidb as an ANIME id and would complete a whole other series, so only the TVDB episode id travels there
                    var tvdb = SimklIds.Get(episode.ProviderIds, "Tvdb");
                    if (tvdb == null) skipped++;
                    else if (seen.Add("e" + tvdb)) request.episodes.Add(new HistoryEpisode { watched_at = watchedAt, ids = new ScrobbleEpisodeIds(episode.ProviderIds) { anidb = null } });
                    continue;
                }
                if (!shows.TryGetValue(series.InternalId, out var show))
                {
                    show = new HistoryShow { title = series.Name, year = series.ProductionYear, ids = new SimklIds(series.ProviderIds) };
                    shows[series.InternalId] = show;
                    request.shows.Add(show);
                }
                var season = show.seasons.FirstOrDefault(s => s.number == episode.ParentIndexNumber.Value);
                if (season == null)
                {
                    season = new HistorySeason { number = episode.ParentIndexNumber.Value };
                    show.seasons.Add(season);
                }
                // under its show the entry carries the episode ids as well: the server matches them within that show and falls back to
                // the season and number (anime seasons are mapped there too), so an id Simkl lacks costs nothing;
                // the ids name the first episode of the file, the rest of a multi-episode file goes by number alone
                var episodeIds = new ScrobbleEpisodeIds(episode.ProviderIds);
                for (var number = first; number <= last; number++)
                {
                    if (!seen.Add("s" + series.InternalId + ":" + episode.ParentIndexNumber + ":" + number)) continue;
                    season.episodes.Add(new HistoryEpisode { number = number, watched_at = watchedAt, ids = number == first && (episodeIds.tvdb != null || episodeIds.anidb != null) ? episodeIds : null });
                }
            }

            return request;
        }
    }
}
