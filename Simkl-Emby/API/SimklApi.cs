using System;
using System.IO;
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
        public const string REDIRECT_URI = @"https://simkl.com/apps/emby/connected/";
        public const string APIKEY = @"27dd5d6adc24aa1ad9f95ef913244cbaf6df5696036af577ed41670473dc97d0";
        public const string APP_NAME = "emby-simkl";
        private const int DefaultTimeoutMs = 30000;
        private const int AllItemsTimeoutMs = 300000;

        // https://api.simkl.org/conventions/headers: every request carries client_id, app-name, app-version and the Emby
        // server's emby-version as query params plus a name/version User-Agent; Authorization only where a user token is involved.
        private static readonly string AppVersion = typeof(SimklApi).Assembly.GetName().Version.ToString(3);
        private readonly string _clientParams;

        private string WithClientParams(string url)
        {
            return url + (url.Contains("?") ? "&" : "?") + _clientParams;
        }

        // Emby caches one HttpClient per (host, timeout): the long-running all-items pulls get their own so a timeout
        // there never blacks out the scrobbler's client for 30 s (EnableAutomaticTimeouts)
        private HttpRequestOptions GetOptions(string url, string userToken = null, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default(CancellationToken))
        {
            var allItems = timeoutMs == AllItemsTimeoutMs;
            HttpRequestOptions options = new HttpRequestOptions
            {
                Url = BASE_URL + WithClientParams(url),
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
            _clientParams = "client_id=" + APIKEY + "&app-name=" + APP_NAME + "&app-version=" + AppVersion + "&emby-version=" + appHost.ApplicationVersion;
        }

        public async Task<CodeResponse> getCode()
        {
            return _json.DeserializeFromStream<CodeResponse>(await _get("/oauth/pin?redirect=" + REDIRECT_URI));
        }

        public async Task<CodeStatusResponse> getCodeStatus(string user_code)
        {
            return _json.DeserializeFromStream<CodeStatusResponse>(await _get("/oauth/pin/" + user_code));
        }

        // null when Simkl found nothing: that answer is an empty array, not an object
        public async Task<SearchFileResponse> getFromFile(string filename) {
            SimklFile f = new SimklFile {file = filename};
            _logger.Info("Posting: " + _json.SerializeToString(f));
            StreamReader r = new StreamReader(await _post("/search/file/", null, f));
            string t = r.ReadToEnd();
            _logger.Debug("Response: " + t);
            return t != null && t.TrimStart().StartsWith("{") ? _json.DeserializeFromString<SearchFileResponse>(t) : null;
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
            });
        }

        public Task<UserSettingsResponse> GetUserSettings(UserConfig userConfig, long embyUserId)
        {
            return Authorized(userConfig, embyUserId, async () => _json.DeserializeFromStream<UserSettingsResponse>(await _post("/users/settings", userConfig.userToken)));
        }

        public Task<ActivitiesResponse> GetActivities(UserConfig userConfig, long embyUserId, CancellationToken cancellationToken)
        {
            return Authorized(userConfig, embyUserId, async () => _json.DeserializeFromStream<ActivitiesResponse>(await _get("/sync/activities", userConfig.userToken, cancellationToken: cancellationToken)));
        }

        // runs one call made with the user's token; clears the token when Simkl rejects it
        private async Task<T> Authorized<T>(UserConfig userConfig, long embyUserId, Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (MediaBrowser.Model.Net.HttpException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw InvalidateToken(userConfig, embyUserId);
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
                stored.userToken = null;
                _userManager.SetTypedUserSetting(embyUserId, ConfigurationFactory.ConfigKey, stored);
            }
            userConfig.userToken = null;
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
