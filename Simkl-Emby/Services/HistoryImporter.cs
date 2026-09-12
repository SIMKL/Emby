using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;

using Simkl.Api;
using Simkl.Api.Exceptions;
using Simkl.Api.Objects;
using Simkl.Api.Responses;
using Simkl.Configuration;

namespace Simkl.Services
{
    // One-way Simkl -> Emby watched import. Emby builds a new instance per request, so run state is static.
    // A manual run pulls the whole history; a scheduled run pulls only what /sync/activities says changed.
    public class HistoryImporter
    {
        public const int CooldownMinutes = 2;
        public const string InterruptedSummary = "Import failed: the server restarted before it finished, run it again";
        public const string CancelledSummary = "Automatic import was stopped before it finished (the scheduled task was cancelled or the server was shutting down). It picks up from the same point on the next scheduled run, or when you press Import changes since the last import.";
        private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
        // the activities row is saved before the watchlist row and date_from is a strict '>' on updateTime, so look back a little
        private const int DateFromSlackSeconds = 60;
        // This client_id predates the "dropped" rename, so the API spells that status "notinteresting" in responses
        private const string DroppedSegment = "notinteresting";

        private class ImportRun
        {
            public bool Running;
            public bool Manual;     // a full import from TryStart; a running check refuses one instead of being joined
            public bool Scheduled;  // started by the task or by switching the toggle on; only these stamp lastAutoRunAt
            public bool Cancelled;
            public bool Reset;      // forget cursors and re-apply Simkl's watch state even to items already played
            public string Message;
            public string Error;
            public DateTime StartedUtc;
            public DateTime? FinishedUtc;
        }

        private class Job
        {
            public User User;
            public LibraryIndex Index;
            public readonly HashSet<long> Seen = new HashSet<long>();   // a multi-episode file matched twice is handled once
            public bool Overwrite;
            public DateTime Started;    // a play state the user changed after this wins over what the run applies
            public int MoviesMarked, EpisodesMarked, AlreadyPlayed, Refreshed, MoviesNotInLibrary, ShowsNotInLibrary, EpisodesNotInLibrary;
        }

        private static readonly ConcurrentDictionary<long, ImportRun> _runs = new ConcurrentDictionary<long, ImportRun>();
        // finish time of the last run of any kind; kept apart from _runs because a new run replaces that entry
        private static readonly ConcurrentDictionary<long, DateTime> _lastFinish = new ConcurrentDictionary<long, DateTime>();

        private static readonly string[] Types = { "shows", "anime", "movies" };

        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;
        private readonly SimklApi _api;

        public HistoryImporter(ILibraryManager libraryManager, IUserDataManager userDataManager, IUserManager userManager,
            IJsonSerializer json, ILogManager logManager, IHttpClient httpClient, IApplicationHost appHost)
        {
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _userManager = userManager;
            _logger = logManager.GetLogger("Simkl Import");
            _api = new SimklApi(json, _logger, httpClient, userManager, appHost);
        }

        public ImportStatus Status(User user)
        {
            _runs.TryGetValue(user.InternalId, out var run);
            return ToStatus(run, LoadState(user));
        }

        // "Reset and re-import fully" (and the plain POST route): always a full import, in the background, at most once per cooldown.
        // reset additionally forgets the import memory and refreshes the watch date of items Emby already has as played.
        public ImportStatus TryStart(User user, UserConfig config, bool reset = false)
        {
            if (RefusalReason(user, config) is string why) return Refused(user, why);

            var run = new ImportRun { Running = true, Manual = true, Reset = reset, Message = "Starting...", StartedUtc = DateTime.UtcNow };
            while (!_runs.TryAdd(user.InternalId, run))
            {
                if (!_runs.TryGetValue(user.InternalId, out var previous)) continue;
                if (previous.Running)
                    return previous.Manual ? ToStatus(previous, LoadState(user)) : Refused(user, "An import is running right now, try again when it finishes");
                if (_runs.TryUpdate(user.InternalId, run, previous)) break;
            }

            var state = Publish(user, run, config);
            _logger.Info("Import for {0} started{1}", user.Name, reset ? " (reset)" : "");
            Task.Run(() => RunAsync(user, config, state, run, true, CancellationToken.None));
            return ToStatus(run, state);
        }

        // scheduled task: incremental once a full import exists, otherwise the one-time full import; never queued behind a live run.
        // The task ticks hourly; each user is checked only when their own interval has elapsed since their last automatic run.
        public Task RunScheduled(User user, UserConfig config, CancellationToken cancellationToken)
        {
            if (RefusalReason(user, config) != null) return Task.CompletedTask;
            var lastAuto = ParseDate(LoadState(user).lastAutoRunAt);
            var hours = Math.Max(1, config.autoImportHours);
            if (lastAuto.HasValue && (DateTime.UtcNow - lastAuto.Value).TotalMinutes < hours * 60 - 5) return Task.CompletedTask;   // 5 min slack for tick drift
            return StartAuto(user, config, true, cancellationToken) ?? Task.CompletedTask;
        }

        // the same check the task would do, right away and in the background: from the "Import changes" button, or from
        // switching the toggle on (scheduled = true, so the interval counts from it like from a task run)
        public ImportStatus TryStartAuto(User user, UserConfig config, bool scheduled)
        {
            if (RefusalReason(user, config) is string why) return Refused(user, why);
            StartAuto(user, config, scheduled, CancellationToken.None);
            return Status(user);
        }

        // null when a run may start
        private static string RefusalReason(User user, UserConfig config)
        {
            if (string.IsNullOrEmpty(config.userToken)) return "Log in to Simkl first";
            if (!user.IsGrantedAccessToFeature(Plugin.StaticId)) return "Simkl is not enabled for this user";
            return CooldownMessage(user);
        }

        // returns the running task, or null when another run for this user is active
        private Task StartAuto(User user, UserConfig config, bool scheduled, CancellationToken cancellationToken)
        {
            var run = new ImportRun { Running = true, Scheduled = scheduled, Message = "Starting...", StartedUtc = DateTime.UtcNow };
            if (!_runs.TryAdd(user.InternalId, run))
            {
                if (!_runs.TryGetValue(user.InternalId, out var previous) || previous.Running || !_runs.TryUpdate(user.InternalId, run, previous)) return null;
            }

            var state = Publish(user, run, config);
            var full = string.IsNullOrEmpty(state.fullImportAt);
            _logger.Info("{0} for {1} started ({2})", scheduled ? "Automatic import" : "New-history check", user.Name, full ? "first run, full" : "incremental");
            return RunAsync(user, config, state, run, full, cancellationToken);
        }

        // the run already owns the _runs entry; retire it if the state cannot be read so it never stays "running"
        private ImportState Publish(User user, ImportRun run, UserConfig config)
        {
            try
            {
                return MarkStarted(user, run, config);
            }
            catch
            {
                _runs.TryRemove(user.InternalId, out _);
                throw;
            }
        }

        private ImportState MarkStarted(User user, ImportRun run, UserConfig config)
        {
            var state = LoadState(user);
            // cursors belong to the Simkl account they were read under; a different login starts over with a full import
            if (run.Reset || state.cursorAccount != AccountKey(config.userToken))
            {
                state.fullImportAt = "";
                state.syncedAt = "";
                state.cursorShows = "";
                state.cursorAnime = "";
                state.cursorMovies = "";
            }
            // marker so a run lost to a server restart is reported instead of looking like the previous result
            state.lastImportAt = Stamp(run.StartedUtc);
            state.lastImportSummary = InterruptedSummary;
            if (run.Scheduled) state.lastAutoRunAt = state.lastImportAt;   // set at start so a failing run also waits the interval
            Persist(user, state);
            return state;
        }

        private async Task RunAsync(User user, UserConfig config, ImportState state, ImportRun run, bool full, CancellationToken cancellationToken)
        {
            var job = new Job { User = user, Overwrite = run.Reset, Started = run.StartedUtc };
            try
            {
                // read activities before pulling so the saved cursor can never run ahead of the data it covers
                var activities = await _api.GetActivities(config, user.InternalId, cancellationToken);
                var cursors = Cursors(activities);
                var changed = full ? new List<string>(Types) : ChangedTypes(state, cursors);

                if (changed.Count > 0)
                {
                    run.Message = "Reading Emby library...";
                    _logger.Info("Import for {0}: reading Emby library", user.Name);
                    job.Index = new LibraryIndex(_libraryManager, user, cancellationToken);
                    job.Index.Prepare(changed);   // a change confined to movies never reads the series and episodes
                    _logger.Info("Import for {0}: {1}", user.Name, job.Index.Describe());
                }

                if (full)
                {
                    await Pull(job, run, config, null, cancellationToken);
                }
                else if (changed.Count > 0)
                {
                    // one call for everything updated since the activities read of the last successful run; the per-type cursors only decide whether to call
                    var since = ParseDate(state.syncedAt) ?? ParseDate(state.fullImportAt);
                    await Pull(job, run, config, since.HasValue ? Stamp(since.Value.AddSeconds(-DateFromSlackSeconds)) : null, cancellationToken);
                }

                foreach (var type in Types)
                {
                    if (cursors.TryGetValue(type, out var cursor) && cursor != null) SetCursor(state, type, cursor);
                }
                state.cursorAccount = AccountKey(config.userToken);
                if (full) state.fullImportAt = Stamp(DateTime.UtcNow);
                state.syncedAt = activities?.all ?? Stamp(run.StartedUtc);   // Simkl's own clock, read before the pull, so a change made during the run is caught next time

                run.Message = (run.Reset ? "Everything re-imported. " : "") + Summary(job, changed.Count == 0);
                _logger.Info("Import for {0}: {1}", user.Name, run.Message);
            }
            catch (InvalidTokenException)
            {
                run.Error = "Simkl login expired, log in again";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // rethrown so Emby records the scheduled task as cancelled; finally still persists the state
                run.Cancelled = true;
                run.Error = CancelledSummary;
                throw;
            }
            catch (HttpException ex)
            {
                // Emby flags every status-less failure (DNS, refused, TLS, timeout) as IsTimedOut, so keep one honest message
                run.Error = ex.StatusCode.HasValue ? "Simkl answered HTTP " + (int)ex.StatusCode.Value
                    : "Simkl did not answer (timed out or unreachable): " + ex.Message;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Import for " + user.Name + " failed", ex);
                run.Error = ex.Message;
            }
            finally
            {
                Finish(user, state, run);
            }
        }

        // one call for everything Simkl has, every type and every list; Simkl builds that response in memory and refuses one that
        // is too big with HTTP 400 MAX_ITEMS, which ends the run with that error (by decision, no splitting into smaller calls)
        private async Task Pull(Job job, ImportRun run, UserConfig config, string dateFrom, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = dateFrom == null ? "everything" : "changes";
            run.Message = "Fetching " + label + "...";
            _logger.Info("Import for {0}: fetching {1}", job.User.Name, label);
            var response = await Fetch(dateFrom, config, job.User.InternalId, cancellationToken);
            run.Message = "Marking movies and episodes...";
            foreach (var type in Types) Apply(job, type, response, cancellationToken);
        }

        private void Finish(User user, ImportState state, ImportRun run)
        {
            try
            {
                run.FinishedUtc = DateTime.UtcNow;
                if (run.Error != null) run.Message = run.Cancelled ? run.Error : "Import failed: " + run.Error;
                _lastFinish[user.InternalId] = run.FinishedUtc.Value;
                state.lastImportAt = Stamp(run.FinishedUtc.Value);
                state.lastImportSummary = run.Message;
                Persist(user, state);
            }
            finally
            {
                run.Running = false;
            }
        }

        // the result lives under its own typed-setting key, so the page's read-modify-write of the user config can never revert it
        private void Persist(User user, ImportState state)
        {
            try
            {
                _userManager.SetTypedUserSetting(user.InternalId, ConfigurationFactory.ImportKey, state);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Couldn't save the import state for " + user.Name, ex);
            }
        }

        private ImportState LoadState(User user)
        {
            return (ImportState)_userManager.GetTypedUserSetting(user.InternalId, ConfigurationFactory.ImportKey) ?? new ImportState();
        }

        // identifies the Simkl login without storing the token a second time
        public static string AccountKey(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "");
            }
        }

        // newest watch-related activity per type; a cursor is the value seen when a run started
        private static Dictionary<string, string> Cursors(ActivitiesResponse activities)
        {
            return new Dictionary<string, string>
            {
                { "shows", Latest(activities?.tv_shows) },
                { "anime", Latest(activities?.anime) },
                { "movies", Latest(activities?.movies) }
            };
        }

        private static string Latest(ActivitiesBucket bucket)
        {
            if (bucket == null) return null;
            string latest = null;
            foreach (var value in new[] { bucket.watching, bucket.hold, bucket.completed, bucket.dropped, bucket.notinteresting })
            {
                if (value != null && (latest == null || string.CompareOrdinal(value, latest) > 0)) latest = value;   // same ISO format, so ordinal order is time order
            }
            return latest;
        }

        private static List<string> ChangedTypes(ImportState state, Dictionary<string, string> cursors)
        {
            var changed = new List<string>();
            foreach (var type in Types)
            {
                var latest = cursors[type];
                var since = Cursor(state, type);
                if (latest != null && (string.IsNullOrEmpty(since) || string.CompareOrdinal(latest, since) > 0)) changed.Add(type);
            }
            return changed;
        }

        private static string Cursor(ImportState state, string type)
        {
            return type == "shows" ? state.cursorShows : type == "anime" ? state.cursorAnime : state.cursorMovies;
        }

        private static void SetCursor(ImportState state, string type, string value)
        {
            if (type == "shows") state.cursorShows = value;
            else if (type == "anime") state.cursorAnime = value;
            else state.cursorMovies = value;
        }

        private async Task<AllItemsResponse> Fetch(string dateFrom, UserConfig config, long embyUserId, CancellationToken cancellationToken)
        {
            // full_anime_seasons is "full" plus the TVDB coordinates of anime episodes; completed shows only carry episodes with
            // include_all_episodes, as virtual rows dated with the show's last watch
            var query = "extended=full_anime_seasons&episode_watched_at=yes&episode_tvdb_id=yes&include_all_episodes=yes" + (dateFrom == null ? "" : "&date_from=" + Uri.EscapeDataString(dateFrom));
            var backoffMs = 1000;
            while (true)
            {
                try
                {
                    return await _api.GetAllItems(query, config, embyUserId, cancellationToken);
                }
                catch (HttpException ex) when ((int?)ex.StatusCode == 429)
                {
                    if (backoffMs > 16000) throw new Exception("Simkl is rate limiting requests, try again later", ex);
                    _logger.Info("Simkl rate limited the import, retrying in {0}s", backoffMs / 1000);
                    await Task.Delay(backoffMs, cancellationToken);
                    backoffMs *= 2;
                }
                catch (HttpException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
                {
                    // 400 on this route is MAX_ITEMS: the history is too big for Simkl to answer in one response
                    throw new Exception("Simkl refused the import: this history is too large for one request", ex);
                }
            }
        }

        private void Apply(Job job, string bucket, AllItemsResponse response, CancellationToken cancellationToken)
        {
            var entries = bucket == "movies" ? response.movies : bucket == "anime" ? response.anime : response.shows;
            foreach (var entry in entries ?? new List<AllItemsEntry>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryStatus = Normalize(entry.status);
                if (entryStatus == "plantowatch") continue;   // never a watch

                // a movie is watched only when completed; hold/dropped/watching anime movies carry no watch
                if (bucket != "movies" && entry.anime_type != "movie") ApplyShow(job, entry, bucket == "anime", cancellationToken);
                else if (entryStatus == "completed") ApplyMovie(job, entry);
            }
        }

        private static string Normalize(string status)
        {
            return status == DroppedSegment ? "dropped" : status;
        }

        private void ApplyMovie(Job job, AllItemsEntry entry)
        {
            var ids = entry.movie?.ids ?? entry.show?.ids;   // anime movies carry their ids under show
            var item = ids == null ? null : job.Index.FindMovie(ids);
            if (item == null)
            {
                job.MoviesNotInLibrary++;
                _logger.Debug("Not in library: movie {0} {1}", entry.movie?.title ?? entry.show?.title, Describe(ids));
                return;
            }
            if (MarkPlayed(job, item, ParseDate(entry.last_watched_at), null)) job.MoviesMarked++;
        }

        private void ApplyShow(Job job, AllItemsEntry entry, bool anime, CancellationToken cancellationToken)
        {
            var ids = entry.show?.ids;
            var title = entry.show?.title;
            string matchedBy = null;
            var series = ids == null ? null : job.Index.FindSeries(ids, out matchedBy);
            var absolute = matchedBy == "anidb" || matchedBy == "mal" || matchedBy == "anilist";
            var fallbackDate = ParseDate(entry.last_watched_at);
            var seasons = entry.seasons ?? new List<AllItemsSeason>();
            // only a completed show gets synthesized rows, all carrying the show's last_watched_at; the
            // episode really watched last shares that date too, so there a date several episodes share counts as synthesized
            var synthesized = Normalize(entry.status) == "completed" && seasons.Sum(s => (s.episodes ?? new List<AllItemsEpisode>()).Count(ep => ParseDate(ep.watched_at) == fallbackDate)) > 1;
            var hits = 0;

            foreach (var season in seasons)
            {
                foreach (var episode in season.episodes ?? new List<AllItemsEpisode>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = FindEpisode(job.Index, series, anime && !absolute, entry, season, episode);
                    if (item == null)
                    {
                        if (series == null) continue;
                        job.EpisodesNotInLibrary++;
                        _logger.Debug("Not in library: {0} S{1}E{2}", title, season.number, episode.number);
                        continue;
                    }
                    hits++;
                    // only a date Simkl really has for this episode may replace one Emby already has
                    var own = ParseDate(episode.watched_at);
                    if (synthesized && own == fallbackDate) own = null;
                    if (MarkPlayed(job, item, own, fallbackDate)) job.EpisodesMarked++;
                }
            }

            if (series != null || hits > 0) return;
            job.ShowsNotInLibrary++;
            _logger.Debug("Not in library: show {0} {1}", title, Describe(ids));
        }

        private static Episode FindEpisode(LibraryIndex index, Series series, bool tvdbNumbering, AllItemsEntry entry, AllItemsSeason season, AllItemsEpisode episode)
        {
            // a TVDB episode id hit counts even when the series itself did not match
            var byId = episode.ids?.tvdb_id is int tvdbId ? index.FindEpisodeByTvdbId(tvdbId) : null;
            if (byId != null || series == null) return byId;
            var (s, n) = tvdbNumbering ? TvdbCoordinates(entry, season, episode) : (season.number, episode.number);
            return index.FindEpisode(series.InternalId, s, n);
        }

        // Anime matched through TVDB/IMDB/TMDB is stored with TVDB seasons in Emby, so translate Simkl's numbers.
        // A tvdb season of 0 is deliberate (specials, anime-lists tvdbseason=0 mappings) and must never fall through
        // to mapped_tvdb_seasons, which cannot express season 0 (the server collapses '0' to [1]).
        private static (int, int) TvdbCoordinates(AllItemsEntry entry, AllItemsSeason season, AllItemsEpisode episode)
        {
            var s = ParseInt(episode.tvdb?.season);
            var e = ParseInt(episode.tvdb?.episode);
            if (s > 0 && e > 0) return (s.Value, e.Value);
            if (s == 0 && e > 0) return (0, e.Value);
            if (season.number == 0) return (0, episode.number);
            var mapped = entry.mapped_tvdb_seasons;
            if (mapped != null && mapped.Length == 1 && mapped[0] > 0) return (mapped[0], episode.number);
            return (1, episode.number);
        }

        private bool MarkPlayed(Job job, BaseItem item, DateTime? own, DateTime? fallback)
        {
            if (!job.Seen.Add(item.InternalId)) return false;
            var data = _userDataManager.GetUserData(job.User, item);
            if (data.PlaystateLastModified.HasValue && data.PlaystateLastModified.Value.UtcDateTime > job.Started) return false;   // changed by hand meanwhile
            if (data.Played)
            {
                job.AlreadyPlayed++;    // never touch items Emby already knows as played, unless a reset asks for Simkl's date of this very item
                if (!job.Overwrite || !own.HasValue) return false;
                job.Refreshed++;
                data.LastPlayedDate = new DateTimeOffset(own.Value, TimeSpan.Zero);
                data.PlaystateLastModified = DateTimeOffset.UtcNow;
                _userDataManager.SaveUserData(job.User, item, data, UserDataSaveReason.Import, CancellationToken.None);
                return false;
            }
            var watchedAt = own ?? fallback;
            data.Played = true;
            data.PlayCount = Math.Max(data.PlayCount, 1);
            if (watchedAt.HasValue) data.LastPlayedDate = new DateTimeOffset(watchedAt.Value, TimeSpan.Zero);
            data.PlaybackPositionTicks = 0;
            data.PlaystateLastModified = DateTimeOffset.UtcNow;
            _userDataManager.SaveUserData(job.User, item, data, UserDataSaveReason.Import, CancellationToken.None);
            return true;
        }

        public static string Stamp(DateTime utc)
        {
            return utc.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime? ParseDate(string value)
        {
            if (!DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) return null;
            return date.Year < 2000 ? (DateTime?)null : date;   // Simkl's "date unknown" sentinel
        }

        private static int? ParseInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : (int?)null;
        }

        private static string Summary(Job job, bool nothingChanged)
        {
            if (nothingChanged) return "Nothing new on Simkl since the last import.";
            var text = "Marked " + job.MoviesMarked + " movies and " + job.EpisodesMarked + " episodes as played. "
                + job.AlreadyPlayed + " already played" + (job.Overwrite ? " (" + job.Refreshed + " watch dates refreshed from Simkl)" : "") + ". "
                + "Not in your Emby library: " + job.MoviesNotInLibrary + " movies, " + job.ShowsNotInLibrary + " shows, " + job.EpisodesNotInLibrary + " episodes.";
            return text;
        }

        private static string Describe(SimklIds ids)
        {
            if (ids == null) return "(no ids)";
            return "simkl=" + ids.simkl + " imdb=" + ids.imdb + " tmdb=" + ids.tmdb + " tvdb=" + ids.tvdb + " mal=" + ids.mal + " anidb=" + ids.anidb + " anilist=" + ids.anilist;
        }

        // any run within the last CooldownMinutes blocks another; the message names both times
        private static string CooldownMessage(User user)
        {
            if (!_lastFinish.TryGetValue(user.InternalId, out var last)) return null;
            var elapsed = DateTime.UtcNow - last;
            if (elapsed.TotalMinutes >= CooldownMinutes) return null;
            var ago = (int)elapsed.TotalMinutes;
            return "An import ran " + (ago == 0 ? "less than a minute" : Minutes(ago)) + " ago. Wait " + Minutes(CooldownMinutes - ago) + " before running another.";
        }

        private static string Minutes(int count)
        {
            return count + (count == 1 ? " minute" : " minutes");
        }

        private static ImportStatus ToStatus(ImportRun run, ImportState state)
        {
            var interrupted = run == null && state.lastImportSummary == InterruptedSummary;
            var finished = run != null && !run.Running && run.FinishedUtc.HasValue;   // in-memory result wins while the process lives
            return new ImportStatus
            {
                running = run?.Running ?? false,
                message = run?.Message ?? Stored(state.lastImportSummary),
                error = run?.Error ?? (interrupted ? InterruptedSummary : null),
                lastImportAt = finished ? Stamp(run.FinishedUtc.Value) : Stored(state.lastImportAt),
                lastAutoRunAt = Stored(state.lastAutoRunAt)
            };
        }

        private ImportStatus Refused(User user, string error)
        {
            var status = ToStatus(null, LoadState(user));
            status.error = error;
            return status;
        }

        private static string Stored(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
