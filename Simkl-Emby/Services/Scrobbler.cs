using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;

namespace Simkl.Services
{
    public class Scrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly ILogger _logger;
        private readonly IUserManager _userManager;
        private SimklApi _api;
        private readonly ConcurrentDictionary<string, PlaybackState> _sessions = new ConcurrentDictionary<string, PlaybackState>();
        // one send at a time per Simkl account (by token: two Emby users may share one), across playbacks: Simkl serializes an
        // account's scrobbles behind a lock, and a stop or pause that overtook the next item's start would clear that item's session
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountGates = new ConcurrentDictionary<string, SemaphoreSlim>();
        // playbacks that just ended, by session and item: a progress tick of theirs landing after the stop would re-open them on Simkl
        private readonly ConcurrentDictionary<string, DateTime> _stopped = new ConcurrentDictionary<string, DateTime>();
        private static readonly object _registration = new object();   // a report registers on all its account gates as one step

        // One per tracked playback. Event threads and the hold continuations all touch it; every send goes through Gate so
        // the flag flip and the call it stands for reach Simkl in the order the user acted.
        private class PlaybackState
        {
            public string ItemId;
            public bool Paused;         // what Emby last reported (a pause may still be on hold)
            public bool PauseSent;      // Simkl holds a pause row and no session for this playback
            public bool Ignored;        // nobody on this session scrobbles this item; skip the settings lookup on later ticks
            public ScrobbleRequest Resolved;    // what the file search identified when Simkl did not know the ids; later events reuse it instead of repeating the 404 and the search
            public bool Unmatched;      // neither the ids nor the file name mean anything to Simkl; every later event of this playback would get the same answer
            public long[] UserIds;      // who was watching when the item started; a user switch in the same browser must not re-address the stop
            public long PositionTicks;  // position at the last playing tick (or where it was paused), to tell a seek from normal advance
            public DateTime ReportedUtc;
            public CancellationTokenSource PendingPause;    // a pause is held briefly: a stop or resume right after it makes it noise
            public CancellationTokenSource PendingSeek;     // scrubbing produces a burst of jumps; only the final position is reported
            public readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        }

        private const long SeekToleranceTicks = 30 * TimeSpan.TicksPerSecond;
        private const int PauseHoldMs = 2000;
        private const int SeekHoldMs = 2000;
        private const int StoppedMemoryMs = 30000;      // a stray tick this long after the stop is still noise
        private const int StoppedPruneMs = 3600000;     // a report can wait its turn far longer than that, so the marker itself lives an hour

        private class Target
        {
            public long EmbyUserId;
            public string Name;
            public UserConfig Config;
            public string Account;      // hash of the token, the key of the account gate
        }

        public Scrobbler(IJsonSerializer json, ISessionManager sessionManager, ILogManager logManager,
            IHttpClient httpClient, IUserManager userManager, IApplicationHost appHost)
        {
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger("Simkl Scrobbler");
            _userManager = userManager;
            _api = new SimklApi(json, _logger, httpClient, userManager, appHost);
        }

        public void Run()
        {
            UpgradeDataIfNeeded();
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
        }

        private void UpgradeDataIfNeeded()
        {
            // TODO: Delete this a couple weeks after the plugin has gotten updated, to avoid any erroneous upgrades from happening when they shouldn't.

            var userConfigs = Plugin.Instance.Configuration.userConfigs;
            if (userConfigs == null) return;

            // an entry that could not be migrated stays in the old store and is retried on the next start
            var failed = new List<UserConfig>();
            foreach (var userConfig in userConfigs)
            {
                try
                {
                    UpgradeData(userConfig);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error upgrading user config", ex);
                    failed.Add(userConfig);
                }
            }

            Plugin.Instance.Configuration.userConfigs = failed.Count == 0 ? null : failed.ToArray();
            Plugin.Instance.SaveConfiguration();
        }

        private void UpgradeData(UserConfig userConfig)
        {
            if (userConfig == null || string.IsNullOrEmpty(userConfig.guid))
            {
                return;
            }

            var embyUserId = _userManager.GetInternalId(userConfig.guid);
            if (embyUserId == 0) return;
            // a retry must not overwrite a login made after the failed attempt
            if (!string.IsNullOrEmpty(ConfigurationFactory.LoadUser(_userManager, embyUserId)?.userToken)) return;
            _userManager.SetTypedUserSetting(embyUserId, ConfigurationFactory.ConfigKey, userConfig);
        }

        public void Dispose()
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _api = null;
        }

        public static bool canBeScrobbled(UserConfig config, BaseItem item)
        {
            if (item.RunTimeTicks < 60 * 10000000L * config.min_length) return false;
            if (item is Movie) return config.scrobbleMovies;
            if (item is Episode) return config.scrobbleShows;
            return false;
        }

        public static double? Progress(long? positionTicks, long? runTimeTicks)
        {
            if (positionTicks == null || runTimeTicks == null || runTimeTicks <= 0) return null;
            return Math.Max(0, Math.Min(100, (double)positionTicks * 100 / (double)runTimeTicks));
        }

        // the identification the file search found, with the progress of the event at hand
        private static ScrobbleRequest WithProgress(ScrobbleRequest resolved, double progress)
        {
            return new ScrobbleRequest(progress) { movie = resolved.movie, show = resolved.show, episode = resolved.episode };
        }

        private static string SessionKey(PlaybackProgressEventArgs e)
        {
            return e.PlaySessionId ?? e.Session?.Id ?? "";
        }

        private static string StoppedKey(PlaybackProgressEventArgs e)
        {
            return SessionKey(e) + "|" + e.MediaInfo?.Id;
        }

        // Emby models a session's users as a list (profile sharing); report for every one that is logged in to Simkl
        private List<Target> EligibleUsers(PlaybackProgressEventArgs e, BaseItem item, long[] rememberedIds)
        {
            var targets = new List<Target>();
            var users = rememberedIds != null ? rememberedIds.Select(id => _userManager.GetUserById(id)).ToList()
                : e.Users?.Count > 0 ? e.Users : new List<User> { _userManager.GetUserById(e.Session?.UserId ?? "") };

            foreach (var user in users)
            {
                if (user == null || !user.IsGrantedAccessToFeature(Plugin.StaticId)) continue;   // an admin can switch Simkl off per user
                var config = ConfigurationFactory.LoadUser(_userManager, user.InternalId);
                if (config == null || string.IsNullOrEmpty(config.userToken) || !canBeScrobbled(config, item)) continue;
                targets.Add(new Target { EmbyUserId = user.InternalId, Name = user.Name, Config = config, Account = HistoryImporter.AccountKey(config.userToken) });
            }

            return targets;
        }

        // Simkl wants one event per user action (play, pause, resume, seek, stop) and no heartbeat, so the periodic
        // progress event only produces a call when the paused flag flips, the position jumps or a new item starts.
        private async void OnPlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            _stopped.TryRemove(StoppedKey(e), out _);   // the same item may genuinely start again right after its stop
            // a restart of the same session must not let a pause or seek held for the old playback fire later
            if (_sessions.TryGetValue(SessionKey(e), out var old)) CancelHolds(old);
            // without a position nothing can be reported; leaving the session unknown lets the next positioned tick send the start
            if (!e.PlaybackPositionTicks.HasValue) return;
            await Report(e, "start", null, null);
        }

        private async void OnPlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
            // a tick without a position carries nothing Simkl can take; the pause or resume it announces is picked up from the next positioned tick
            if (!e.PlaybackPositionTicks.HasValue) return;
            var known = _sessions.TryGetValue(SessionKey(e), out var state) && state.ItemId == e.MediaInfo?.Id;
            if (!known)
            {
                // a tick of a playback that already ended (in flight while the stop was handled) would re-open it on Simkl
                if (_stopped.TryGetValue(StoppedKey(e), out var endedAt) && (DateTime.UtcNow - endedAt).TotalMilliseconds < StoppedMemoryMs) return;
                // Emby Web reports "paused" when the media runs out, a moment before the stop event; that pause is noise
                if (e.IsPaused && Progress(e.PlaybackPositionTicks, e.Item?.RunTimeTicks) >= 99.5) return;
                await Report(e, e.IsPaused ? "pause" : "start", null, null);
                return;
            }
            if (state.Ignored || state.Unmatched) return;

            // a seek is a user action too: a position far from where it should be gets a fresh session at the new place
            var seek = !e.IsPaused && Jumped(state, e);
            // only playing ticks move the clock; while paused the position stays put, so a jump made during the pause shows on the resume tick
            if (!e.IsPaused) { state.PositionTicks = e.PlaybackPositionTicks.Value; state.ReportedUtc = DateTime.UtcNow; }
            if (state.Paused == e.IsPaused && !seek) return;

            if (e.IsPaused)
            {
                state.Paused = true;
                await HoldPause(e, state, Renew(ref state.PendingPause));
                return;
            }

            // resume (or seek): a pause that was still on hold is dropped and needs no matching start
            var pauseSent = await Resume(state);
            if (seek) await HoldSeek(e, state, Renew(ref state.PendingSeek));
            else if (pauseSent) await Report(e, "start", null, state);
        }

        private async void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            _sessions.TryRemove(SessionKey(e), out var ended);
            if (ended != null) CancelHolds(ended);
            _stopped[StoppedKey(e)] = DateTime.UtcNow;
            foreach (var stale in _stopped.Where(p => (DateTime.UtcNow - p.Value).TotalMilliseconds > StoppedPruneMs).Select(p => p.Key).ToList()) _stopped.TryRemove(stale, out _);
            var previous = ended?.ItemId == e.MediaInfo?.Id ? ended : null;
            // Emby's PlayedToCompletion follows its own resume threshold, so a reported position always decides; without one
            // the last playing tick stands in, and only a positionless completion counts as the end
            var progress = e.PlaybackPositionTicks.HasValue ? null : e.PlayedToCompletion ? (double?)100 : Progress(previous?.PositionTicks, e.Item?.RunTimeTicks);
            await Report(e, "stop", progress, previous);
        }

        // the flip waits for a send in flight, so a held pause that lost the race to the resume is skipped rather than answered late
        private static async Task<bool> Resume(PlaybackState state)
        {
            await state.Gate.WaitAsync();
            try
            {
                state.PendingPause?.Cancel();
                state.PendingPause = null;
                state.Paused = false;
                return state.PauseSent;
            }
            finally
            {
                state.Gate.Release();
            }
        }

        private async Task HoldPause(PlaybackProgressEventArgs e, PlaybackState state, CancellationTokenSource pending)
        {
            if (!await Held(PauseHoldMs, pending)) return;
            await Report(e, "pause", null, state, () => state.PendingPause == pending && state.Paused);
        }

        private async Task HoldSeek(PlaybackProgressEventArgs e, PlaybackState state, CancellationTokenSource pending)
        {
            if (!await Held(SeekHoldMs, pending)) return;
            // a pause that arrived during the hold takes over: its own hold reports the seeked position, the resume brings the start
            await Report(e, "seek", null, state, () => state.PendingSeek == pending && !state.Paused);
        }

        // cancels whatever was on hold for this field and hands back the fresh token source that now owns the hold
        private static CancellationTokenSource Renew(ref CancellationTokenSource hold)
        {
            hold?.Cancel();
            return hold = new CancellationTokenSource();
        }

        // false when something cancelled the hold before it ran out
        private static async Task<bool> Held(int ms, CancellationTokenSource pending)
        {
            try
            {
                await Task.Delay(ms, pending.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static void CancelHolds(PlaybackState state)
        {
            state.PendingPause?.Cancel();
            state.PendingSeek?.Cancel();
        }

        private static bool Jumped(PlaybackState state, PlaybackProgressEventArgs e)
        {
            if (state.ReportedUtc == default(DateTime)) return false;
            var expected = state.Paused ? state.PositionTicks : state.PositionTicks + (DateTime.UtcNow - state.ReportedUtc).Ticks;
            return Math.Abs(e.PlaybackPositionTicks.Value - expected) > SeekToleranceTicks;
        }

        // true while the state still belongs to the playback that is being tracked (a stop or restart replaces it)
        private bool StillTracked(PlaybackProgressEventArgs e, PlaybackState state)
        {
            return _sessions.TryGetValue(SessionKey(e), out var current) && current == state;
        }

        // action: start, pause, stop, or seek = a start at the new position (the API leaves a session alone when the same item starts again behind its clock);
        // stillWanted is evaluated under the gate for held events, so one overtaken by a resume, a newer hold or a stop is dropped
        private async Task Report(PlaybackProgressEventArgs e, string action, double? forcedProgress, PlaybackState previous, Func<bool> stillWanted = null)
        {
            try
            {
                var dto = e.MediaInfo;
                var item = e.Item;
                var key = SessionKey(e);
                if (dto == null || item == null || key == "") return;
                var since = DateTime.UtcNow;    // a stop that lands while this report waits its turn is newer than the report

                var progress = forcedProgress ?? Progress(e.PlaybackPositionTicks, item.RunTimeTicks);
                var targets = EligibleUsers(e, item, previous?.UserIds);
                var request = progress == null || targets.Count == 0 ? null : _api.BuildRequest(dto, item, progress.Value);
                var state = previous ?? new PlaybackState();

                // the account gates come first and are queued right here, before anything else waits, so one account's events reach
                // Simkl in the order Emby raised them even across playbacks (the last item's stop ahead of the next item's start)
                var accounts = targets.Select(t => t.Account).Distinct().OrderBy(a => a, StringComparer.Ordinal).Select(a => _accountGates.GetOrAdd(a, id => new SemaphoreSlim(1, 1))).ToList();
                // queued on every gate in one atomic step: a shared session keeps its place on all of them, and two reports can never
                // interleave their registrations and end up each holding a gate the other waits for
                List<Task> waits;
                lock (_registration)
                {
                    waits = accounts.Select(gate => gate.WaitAsync()).ToList();
                }
                foreach (var wait in waits) await wait;
                try
                {
                    await state.Gate.WaitAsync();
                    try
                    {
                        if (stillWanted != null && !stillWanted()) return;
                        string[] calls;
                        if (action == "stop")
                        {
                            calls = new[] { action };
                        }
                        else
                        {
                            // a stop that landed meanwhile has removed the remembered state; putting it back would let a start follow the stop
                            if (previous != null && !StillTracked(e, previous)) return;
                            // a stop that landed while this report waited its turn ended the playback; publishing it now would revive it
                            if (previous == null && _stopped.TryGetValue(StoppedKey(e), out var endedAt) && endedAt >= since) return;
                            // a new start of another item marks the running session watched when Simkl's clock says it passed 80%; a new start of
                            // the same item behind that clock (a seek back) does not since API 2026-09-12, so a seek needs no pause first
                            calls = new[] { action == "seek" ? "start" : action };
                            state.ItemId = dto.Id;
                            state.Paused = action == "pause";
                            state.PauseSent = action == "pause";
                            state.Ignored = request == null;
                            state.UserIds = previous?.UserIds ?? targets.Select(t => t.EmbyUserId).ToArray();
                            state.PositionTicks = e.PlaybackPositionTicks ?? 0;
                            state.ReportedUtc = DateTime.UtcNow;
                            if (previous == null) _sessions[key] = state;
                        }
                        if (request == null || state.Unmatched) return;

                        foreach (var target in targets)
                        {
                            try
                            {
                                foreach (var call in calls)
                                {
                                    var payload = state.Resolved == null ? request : WithProgress(state.Resolved, progress.Value);
                                    _logger.Info("Simkl {0} {1}% for {2}: {3}", call, payload.progress, target.Name, dto.Name);
                                    var response = await Send(call, payload, dto, target.Config, target.EmbyUserId, progress.Value, state);
                                    if (state.Unmatched) return;
                                    if (response != null)
                                    {
                                        _logger.Info("Simkl answered {0}{1} for {2}", response.action, response.rewatch_status == null ? "" : " (rewatch " + response.rewatch_status + ")", dto.Name);
                                        if (response.rewatch_status == "pro_required") DisableRewatch(target);
                                    }
                                }
                            }
                            catch (InvalidTokenException)
                            {
                                _logger.Info("Deleted Simkl token of " + target.Name);
                            }
                            catch (Exception ex)
                            {
                                // one account's trouble must not cost the others on a shared session their report
                                _logger.ErrorException("Couldn't report " + action + " to Simkl for " + target.Name, ex);
                            }
                        }
                    }
                    finally
                    {
                        state.Gate.Release();
                    }
                }
                finally
                {
                    foreach (var gate in accounts) gate.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Couldn't report " + action + " to Simkl", ex);
            }
        }

        // Simkl answers pro_required when the account behind the token is not Pro/VIP any more: the flag is switched off in the
        // stored settings so later stops go out without it, and on the snapshot in hand so this playback's do too. The settings page
        // is the other guard; it never lets a free account tick the box. Only the config of the token that was answered is touched.
        private void DisableRewatch(Target target)
        {
            // the stored settings are read before the snapshot is touched: Emby hands out its cached object, so both are often the same
            var stored = ConfigurationFactory.LoadUser(_userManager, target.EmbyUserId);
            if (stored != null && stored.allowRewatch && stored.userToken == target.Config.userToken)
            {
                stored.allowRewatch = false;
                _userManager.SetTypedUserSetting(target.EmbyUserId, ConfigurationFactory.ConfigKey, stored);
                _logger.Info("Simkl rewatches need a Pro or VIP plan; switched them off for " + target.Name);
            }
            target.Config.allowRewatch = false;
        }

        // an item Simkl does not know by ids is identified through its file search once per playback: the match is kept in the
        // state for the following events, and an item neither the ids nor the file name identify is not tried again
        private async Task<ScrobbleResponse> Send(string action, ScrobbleRequest request, BaseItemDto dto, UserConfig userConfig, long embyUserId, double progress, PlaybackState state)
        {
            try
            {
                return await Deliver(action, request, dto, userConfig, embyUserId);
            }
            catch (HttpException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                if (state.Resolved == null)
                {
                    _logger.Info("Simkl couldn't match ids for {0}, identifying by file name", dto.Name);
                    foreach (var fullPath in new[] { true, false })
                    {
                        var resolved = await _api.BuildRequestFromFile(dto, fullPath, progress);
                        if (resolved == null) continue;
                        try
                        {
                            var response = await Deliver(action, resolved, dto, userConfig, embyUserId);
                            state.Resolved = resolved;
                            return response;
                        }
                        catch (HttpException ex2) when (ex2.StatusCode == HttpStatusCode.NotFound)
                        {
                        }
                    }
                }
                state.Unmatched = true;
                _logger.Info("Simkl doesn't know {0}; it is not reported again during this playback", dto.Name);
                return null;
            }
        }

        // one scrobble call and the answers that settle it, whichever ids it carries: 409 = already watched within the hour,
        // 400 RATE_LIMIT / 429 = Simkl's 20-second per-user lock, where a stop is worth one retry and pause/resume flips are not
        private async Task<ScrobbleResponse> Deliver(string action, ScrobbleRequest request, BaseItemDto dto, UserConfig userConfig, long embyUserId)
        {
            try
            {
                return await _api.Scrobble(action, request, userConfig, embyUserId);
            }
            catch (HttpException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.Info("Simkl already marked {0} as watched within the last hour", dto.Name);
                return null;
            }
            catch (HttpException ex) when (ex.StatusCode == HttpStatusCode.BadRequest || (int?)ex.StatusCode == 429)
            {
                if (action != "stop") return null;
                _logger.Info("Simkl rejected stop for {0} with HTTP {1}, retrying once in 21s", dto.Name, (int?)ex.StatusCode);
                await Task.Delay(21000);
                return await _api.Scrobble(action, request, userConfig, embyUserId);
            }
        }
    }
}
