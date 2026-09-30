using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

using Simkl.Api.Objects;
using Simkl.Api.Responses;
using Simkl.Api.Exceptions;
using MediaBrowser.Model.Dto;
using Simkl.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Simkl.Api
{
    public class SimklApi
    {
        private readonly IJsonSerializer _json;
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;
        private readonly IUserManager _userManager;

        public const string BASE_URL = @"https://api.simkl.com";

        // Two client ids. A Simkl token is bound to the app that minted it, so a call names the id its token belongs to:
        // a pin-flow token stops resolving the moment a request names the OAuth 2 id, and the user looks logged out.
        public const string ApikeyLegacy = @"27dd5d6adc24aa1ad9f95ef913244cbaf6df5696036af577ed41670473dc97d0";   // the old pin-flow app; its tokens never expire and only resolve under this id
        public const string ApikeyOAuth2 = @"bb759d5ad2fdac0f7ce326fbf8deca0333e1b9228d4b8309f2c9538d02da1c81";   // the OAuth 2 device-flow app; a public client, no secret

        public const string APP_NAME = "emby-simkl";
        public const string Scope = "media:read media:write";   // write is needed: scrobble and the file search are both POSTs with a body
        public const int DefaultAccessLifetimeSeconds = 604800; // seven days; used only when a token answer omits expires_in
        private const int DefaultTimeoutMs = 30000;
        private const int AllItemsTimeoutMs = 300000;

        // one renewal at a time per Simkl account (keyed by refresh token, which two Emby users may share), so a burst of
        // calls that all see the token near expiry makes one refresh, not one each
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshGates = new ConcurrentDictionary<string, SemaphoreSlim>();

        // https://api.simkl.org/conventions/headers: every request carries client_id, app-name, app-version and the Emby
        // server's emby-version as query params plus a name/version User-Agent; Authorization only where a user token is involved.
        private static readonly string AppVersion = typeof(SimklApi).Assembly.GetName().Version.ToString(3);
        private readonly string _clientParamsLegacy;
        private readonly string _clientParamsOAuth2;

        // Emby caches one HttpClient per (host, timeout): the long-running all-items pulls get their own so a timeout
        // there never blacks out the scrobbler's client for 30 s (EnableAutomaticTimeouts). The client id follows the
        // token unless one is named outright, which only the /oauth2 routes do.
        private HttpRequestOptions GetOptions(string url, string userToken = null, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default(CancellationToken), string clientId = null)
        {
            var apiKey = clientId ?? (UserConfig.IsOAuth2Token(userToken) ? ApikeyOAuth2 : ApikeyLegacy);
            var clientParams = apiKey == ApikeyOAuth2 ? _clientParamsOAuth2 : _clientParamsLegacy;
            var allItems = timeoutMs == AllItemsTimeoutMs;
            HttpRequestOptions options = new HttpRequestOptions
            {
                Url = BASE_URL + url + (url.Contains("?") ? "&" : "?") + clientParams,
                UserAgent = APP_NAME + "/" + AppVersion,
                RequestContentType = "application/json",
                LogRequest = true,
                LogRequestAsDebug = true,
                LogResponse = true,
                LogResponseHeaders = true,
                LogErrorResponseBody = true,
                TimeoutMs = timeoutMs,
                EnableAutomaticTimeouts = !allItems,
                BufferContent = allItems,   // whole body read under the token, not handed back as a live stream
                CancellationToken = cancellationToken
            };
            if ( !string.IsNullOrEmpty(userToken) )
                options.RequestHeaders.Add("Authorization", "Bearer " + userToken);

            return options;
        }

        public SimklApi(IJsonSerializer json, ILogger logger, IHttpClient httpClient, IUserManager userManager, IApplicationHost appHost)
        {
            _json = json;
            _logger = logger;
            _httpClient = httpClient;
            _userManager = userManager;
            var shared = "&app-name=" + APP_NAME + "&app-version=" + AppVersion + "&emby-version=" + appHost.ApplicationVersion;
            _clientParamsLegacy = "client_id=" + ApikeyLegacy + shared;
            _clientParamsOAuth2 = "client_id=" + ApikeyOAuth2 + shared;
        }

        // Starts the OAuth 2 device flow: a user code to type at simkl.com/pin and a device code the server polls with.
        // The device code is a credential and stays here; only the user code and the link ever reach the browser.
        public async Task<DeviceCodeResponse> StartDeviceLogin(CancellationToken cancellationToken)
        {
            var (status, body) = await PostOAuth("/oauth2/device", new OAuthRequest { client_id = ApikeyOAuth2, scope = Scope }, cancellationToken).ConfigureAwait(false);
            if ((int)status >= 400)
            {
                _logger.Error("Simkl device login start failed (HTTP {0})", (int)status);
                return null;
            }

            return _json.DeserializeFromString<DeviceCodeResponse>(body);
        }

        // Polls once for the device code. Before the user approves, Simkl answers HTTP 400 with authorization_pending
        // (or slow_down); the tokens arrive on approval. The error string is what the caller decides to keep waiting on.
        public Task<(TokenResponse Tokens, string Error)> PollDeviceLogin(string deviceCode, CancellationToken cancellationToken)
        {
            return Exchange(new OAuthRequest { client_id = ApikeyOAuth2, grant_type = OAuthRequest.DeviceCodeGrant, device_code = deviceCode }, cancellationToken);
        }

        // Renews an access token from the stored refresh token. force skips the not-due-yet test, for a 401 on a token
        // that still looks fresh. Returns true when a usable token is in hand, false when the login is over; a transient
        // failure during a forced renewal throws rather than discarding a login that a Simkl hiccup only failed to renew.
        public async Task<bool> TryRefresh(UserConfig config, long embyUserId, bool force, CancellationToken cancellationToken)
        {
            if (!config.IsOAuth2() || string.IsNullOrEmpty(config.refreshToken))
            {
                return false;   // a legacy login has nothing to renew: false means it is over
            }

            var refresh = config.refreshToken;
            var gate = RefreshGates.GetOrAdd(refresh, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var attempted = config.userToken;

                // another call already renewed while this one waited for the gate: adopt what is stored and stop
                var stored = ConfigurationFactory.LoadUser(_userManager, embyUserId);
                if (stored != null && stored.userToken != attempted)
                {
                    config.CopyTokensFrom(stored);
                    return !string.IsNullOrEmpty(config.userToken);
                }

                if (!force && !config.NeedsRefresh())
                {
                    return true;
                }

                var (tokens, error) = await Exchange(new OAuthRequest { client_id = ApikeyOAuth2, grant_type = OAuthRequest.RefreshTokenGrant, refresh_token = refresh }, cancellationToken).ConfigureAwait(false);
                if (tokens == null || string.IsNullOrEmpty(tokens.access_token))
                {
                    _logger.Error("Simkl token renewal failed: {0}", error ?? "no answer");
                    if (!OAuthErrorResponse.IsTerminal(error))
                    {
                        if (force)
                        {
                            throw new MediaBrowser.Model.Net.HttpException("Simkl token renewal did not complete");
                        }

                        return true;   // proactive renewal, transient failure: keep the current token and let the call try it
                    }

                    return false;   // terminal: the grant is gone
                }

                var expiresAt = DateTime.UtcNow.AddSeconds(tokens.expires_in > 0 ? tokens.expires_in : DefaultAccessLifetimeSeconds);
                var keptRefresh = string.IsNullOrEmpty(tokens.refresh_token) ? refresh : tokens.refresh_token;   // Simkl reuses the refresh token
                var keptScope = string.IsNullOrEmpty(tokens.scope) ? config.tokenScope : tokens.scope;

                // write by the old value, so a logout or a second login that already replaced it wins and this is dropped
                ConfigurationFactory.ReplaceToken(_userManager, attempted, tokens.access_token, keptRefresh, expiresAt, keptScope);
                config.userToken = tokens.access_token;
                config.refreshToken = keptRefresh;
                config.tokenExpiresAt = expiresAt;
                config.tokenScope = keptScope;
                return true;
            }
            finally
            {
                gate.Release();
            }
        }

        // POST to an /oauth2 route without throwing on a 4xx, so the poll can read authorization_pending out of a 400.
        // Nothing of these bodies is logged: they carry the device code, the refresh token and, on success, the tokens.
        private async Task<(HttpStatusCode Status, string Body)> PostOAuth(string url, object data, CancellationToken cancellationToken)
        {
            var options = GetOptions(url, null, DefaultTimeoutMs, cancellationToken, ApikeyOAuth2);
            options.LogRequest = false;
            options.LogResponse = false;
            options.LogErrorResponseBody = false;
            options.ThrowOnErrorResponse = false;
            if (data != null)
            {
                options.RequestContent = _json.SerializeToString(data).AsMemory();
            }

            var response = await _httpClient.SendAsync(options, "POST").ConfigureAwait(false);
            using (var reader = new StreamReader(response.Content))
            {
                return (response.StatusCode, await reader.ReadToEndAsync().ConfigureAwait(false));
            }
        }

        private async Task<(TokenResponse Tokens, string Error)> Exchange(OAuthRequest body, CancellationToken cancellationToken)
        {
            var (status, respBody) = await PostOAuth("/oauth2/token", body, cancellationToken).ConfigureAwait(false);
            if ((int)status < 400)
            {
                return (_json.DeserializeFromString<TokenResponse>(respBody), null);
            }

            return (null, ErrorFrom(respBody) ?? ("HTTP " + (int)status));
        }

        // the RFC error string out of a 4xx body; null when the body is not the JSON envelope (a proxy's HTML 502)
        private string ErrorFrom(string body)
        {
            if (string.IsNullOrEmpty(body) || !body.TrimStart().StartsWith("{"))
            {
                return null;
            }

            try
            {
                return _json.DeserializeFromString<OAuthErrorResponse>(body)?.error;
            }
            catch
            {
                return null;
            }
        }

        // null when Simkl found nothing: that answer is an empty array, not an object
        public async Task<SearchFileResponse> getFromFile(string filename) {
            SimklFile f = new SimklFile {file = filename};
            _logger.Info("Posting: " + _json.SerializeToString(f));
            using (StreamReader r = new StreamReader(await _post("/search/file/", null, f)))
            {
                string t = r.ReadToEnd();
                _logger.Debug("Response: " + t);
                return t.TrimStart().StartsWith("{") ? _json.DeserializeFromString<SearchFileResponse>(t) : null;
            }
        }

        public ScrobbleRequest BuildRequest(BaseItemDto dto, BaseItem item, double progress)
        {
            var request = new ScrobbleRequest(progress);

            if (item is Movie)
            {
                request.movie = new SimklMovie(dto);
            }
            else if (item is Episode episode)
            {
                var series = episode.Series;
                request.show = new SimklShow(dto.SeriesName, series?.ProductionYear, series?.ProviderIds);
                request.episode = new ScrobbleEpisode
                {
                    season = dto.ParentIndexNumber,
                    number = dto.IndexNumber,
                    ids = new ScrobbleEpisodeIds(dto.ProviderIds)
                };
            }
            else
            {
                return null;
            }

            return request;
        }

        /// <summary>
        /// Fallback when Simkl can't match the ids: identify the file by name and build the request from Simkl's own match;
        /// null when the item has no path or the search found nothing of the same type as the item
        /// </summary>
        public async Task<ScrobbleRequest> BuildRequestFromFile(BaseItemDto dto, bool fullpath, double progress)
        {
            var name = fullpath ? dto.Path : Path.GetFileName(dto.Path);
            var mo = string.IsNullOrEmpty(name) ? null : await getFromFile(name);
            var found = mo != null && string.Equals(mo.type, dto.Type, StringComparison.OrdinalIgnoreCase)
                && (mo.type == "movie" ? mo.movie != null : mo.show != null && mo.episode != null);
            if (!found)
            {
                _logger.Info("Simkl file search returned {0} for {1} {2}", mo?.type ?? "nothing", dto.Type, name);
                return null;
            }

            var request = new ScrobbleRequest(progress);
            if (mo.type == "movie") request.movie = mo.movie;
            else
            {
                request.show = mo.show;
                request.episode = new ScrobbleEpisode { season = mo.episode.season, number = mo.episode.episode };
            }
            return request;
        }

        public Task<ScrobbleResponse> Scrobble(string action, ScrobbleRequest request, UserConfig userConfig, long embyUserId)
        {
            _logger.Info("Scrobble " + action + ": " + _json.SerializeToString(request));
            // rewatches are opted into per user and only make sense on the stop, never on start (Simkl ignores it there)
            var path = "/scrobble/" + action + (action == "stop" && userConfig.allowRewatch ? "?allow_rewatch=yes" : "");
            return Authorized(userConfig, embyUserId, async () => _json.DeserializeFromStream<ScrobbleResponse>(await _post(path, userConfig.userToken, request)));
        }

        // items marked as watched by hand in Emby; never with allow_rewatch, so re-marking something already watched stays a no-op
        public Task<SyncHistoryResponse> AddToHistory(HistoryRequest request, UserConfig userConfig, long embyUserId)
        {
            return Authorized(userConfig, embyUserId, async () => _json.DeserializeFromStream<SyncHistoryResponse>(await _post("/sync/history", userConfig.userToken, request)));
        }

        // a single item unmarked by hand in Emby; every show entry names its episodes, one without them would wipe the whole show
        public Task<SyncHistoryResponse> RemoveFromHistory(HistoryRequest request, UserConfig userConfig, long embyUserId)
        {
            return Authorized(userConfig, embyUserId, async () => _json.DeserializeFromStream<SyncHistoryResponse>(await _post("/sync/history/remove", userConfig.userToken, request)));
        }

        public Task<AllItemsResponse> GetAllItems(string query, UserConfig userConfig, long embyUserId, CancellationToken cancellationToken)
        {
            var url = "/sync/all-items?" + query;
            return Authorized(userConfig, embyUserId, async () =>
            {
                // an empty list answers the literal body "null" for this client_id, which the serializer rejects
                using (var reader = new StreamReader(await _get(url, userConfig.userToken, AllItemsTimeoutMs, cancellationToken)))
                {
                    var body = (await reader.ReadToEndAsync()).Trim();
                    return body == "" || body == "null" ? new AllItemsResponse() : _json.DeserializeFromString<AllItemsResponse>(body);
                }
            }, cancellationToken);
        }

        public Task<UserSettingsResponse> GetUserSettings(UserConfig userConfig, long embyUserId)
        {
            return Authorized(userConfig, embyUserId, async () => _json.DeserializeFromStream<UserSettingsResponse>(await _post("/users/settings", userConfig.userToken)));
        }

        public Task<ActivitiesResponse> GetActivities(UserConfig userConfig, long embyUserId, CancellationToken cancellationToken)
        {
            return Authorized(userConfig, embyUserId, async () => _json.DeserializeFromStream<ActivitiesResponse>(await _get("/sync/activities", userConfig.userToken, cancellationToken: cancellationToken)), cancellationToken);
        }

        // runs one call made with the user's token, renewing an OAuth 2 token that is due or that Simkl rejects, and
        // clearing the token only when a renewal cannot save it
        private async Task<T> Authorized<T>(UserConfig userConfig, long embyUserId, Func<Task<T>> call, CancellationToken cancellationToken = default(CancellationToken))
        {
            await EnsureFresh(userConfig, embyUserId, cancellationToken).ConfigureAwait(false);
            try
            {
                return await call();
            }
            catch (MediaBrowser.Model.Net.HttpException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
            {
                // an access token Simkl stopped accepting before its stored expiry is worth one forced renewal; a legacy
                // token, or a renewal that comes back terminal, means the login is genuinely over
                if (await TryRefresh(userConfig, embyUserId, true, cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        return await call();
                    }
                    catch (MediaBrowser.Model.Net.HttpException retry) when (retry.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw InvalidateToken(userConfig, embyUserId);
                    }
                }

                throw InvalidateToken(userConfig, embyUserId);
            }
        }

        private async Task EnsureFresh(UserConfig userConfig, long embyUserId, CancellationToken cancellationToken)
        {
            if (!userConfig.NeedsRefresh())
            {
                return;
            }

            try
            {
                await TryRefresh(userConfig, embyUserId, false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // renewing ahead of expiry is best effort: whatever went wrong (a hiccup, a proxy's HTML answer that
                // is not a token), the call proceeds with the current token, and the 401 path forces a renewal if
                // that one is really gone
            }
        }

        private InvalidTokenException InvalidateToken(UserConfig userConfig, long embyUserId)
        {
            _logger.Error("Invalid user token, deleting");
            // the caller may hold a snapshot the page has since replaced (a save swaps the stored object), so clear the
            // token on the stored object, and only when it is still the token Simkl just rejected
            var stored = ConfigurationFactory.LoadUser(_userManager, embyUserId) ?? userConfig;
            if (stored.userToken == userConfig.userToken)
            {
                stored.ClearTokens();
                _userManager.SetTypedUserSetting(embyUserId, ConfigurationFactory.ConfigKey, stored);
            }
            userConfig.ClearTokens();
            return new InvalidTokenException("Invalid user token");
        }

        /// <summary>
        /// API's private get method, given RELATIVE url and headers
        /// </summary>
        /// <param name="url">Relative url</param>
        /// <param name="userToken">Authentication token</param>
        /// <returns>HTTP(s) Stream to be used</returns>
        private async Task<Stream> _get(string url, string userToken = null, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _httpClient.Get(GetOptions(url, userToken, timeoutMs, cancellationToken)).ConfigureAwait(false);
        }

        /// <summary>
        /// API's private post method
        /// </summary>
        /// <param name="url">Relative post url</param>
        /// <param name="data">Object to serialize</param>
        /// <param name="userToken">Authentication token</param>
        /// <returns></returns>
        private async Task<Stream> _post(string url, string userToken = null, object data = null)
        {
            HttpRequestOptions options = GetOptions(url, userToken);
            if (data != null) options.RequestContent = _json.SerializeToString(data).AsMemory();

            return (await _httpClient.Post(options).ConfigureAwait(false)).Content;
        }
    }
}
