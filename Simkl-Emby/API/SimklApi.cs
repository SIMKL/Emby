using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
// using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

using Simkl.Api.Objects;
using Simkl.Api.Responses;


namespace Simkl.Api
{
    public class SimklApi
    {
        /* INTERFACES */
        private readonly IJsonSerializer _json;
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;

        /* BASIC API THINGS */
        public const string BASE_URL = @"https://api.simkl.com";
        // public const string BASE_URL = @"http://private-9c39b-simkl.apiary-mock.com";

        public const string REDIRECT_URI = @"https://ddavo.me/redirected?from=EmbySimkl";
        public const string APIKEY = @"27dd5d6adc24aa1ad9f95ef913244cbaf6df5696036af577ed41670473dc97d0";
        public const string SECRET = @"d7b9feb9d48bbaa69dbabaca21ba4671acaa89198637e9e136a4d69ec97ab68b";

        private HttpRequestOptions GetOptions(string userToken = null)
        {
            HttpRequestOptions options = new HttpRequestOptions
            {
                RequestContentType = "application/json",
                LogRequest = true,
                LogRequestAsDebug = true,
                LogResponse = true,
                LogResponseHeaders = true,
                LogErrorResponseBody = true,
                EnableDefaultUserAgent = true // ??
            };
            options.RequestHeaders.Add("simkl-api-key", APIKEY);
            // options.RequestHeaders.Add("Content-Type", "application/json");
            if ( !string.IsNullOrEmpty(userToken) )
                options.RequestHeaders.Add("authorization", "Bearer " + userToken);

            return options;
        }

        public SimklApi(IJsonSerializer json, ILogger logger, IHttpClient httpClient)
        {
            _json = json;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<CodeResponse> getCode()
        {
            string uri = String.Format("/oauth/pin?client_id={0}&redirect={1}", APIKEY, REDIRECT_URI);
            return _json.DeserializeFromStream<CodeResponse>(await _get(uri));
        }

        public async Task<CodeStatusResponse> getCodeStatus(string user_code)
        {
            string uri = String.Format("/oauth/pin/{0}?client_id={1}", user_code, APIKEY);
            return _json.DeserializeFromStream<CodeStatusResponse>(await _get(uri));
        }

        public async Task<UserSettings> getUserSettings(string user_code)
        {
            string uri = String.Format("/users/settings");
            return _json.DeserializeFromStream<UserSettings>(await _post(uri, user_code));
        }


        // It should return the unserialized response (string)
        // TODO: Return serialized response
        /// <summary>
        /// Scrobbles a single movie
        /// </summary>
        /// <param name="movie">The movie object to scrobble</param>
        /// <param name="progress">Progress</param>
        /// <returns>Serialized response</returns>
        public async Task<Stream> ScrobbleSingleMovieAsync(SimklMovie movie, int progress, string userToken) {
            // TODO: Find a way to check the progress (get settings)
            return await SyncHistoryAsync(new SimklHistory
            {
                movies = new SimklMovie[] { movie }
            }, userToken);
        }

        /// <summary>
        /// Implements /sync/history method from simkl
        /// </summary>
        /// <param name="history">History object</param>
        /// <param name="userToken">User token</param>
        /// <returns></returns>
        public async Task<Stream> SyncHistoryAsync(SimklHistory history, string userToken)
        {
            return await _post("/sync/history", userToken, history);

            // using (var r = await _get("/sync/history"))
        }

        /// <summary>
        /// API's private get method, given RELATIVE url and headers
        /// </summary>
        /// <param name="url">Relative url</param>
        /// <param name="userToken">Authentication token</param>
        /// <returns>HTTP(s) Stream to be used</returns>
        private async Task<Stream> _get(string url, string userToken = null)
        {
            // Todo: If string is not null neither empty
            HttpRequestOptions options = GetOptions(userToken);
            options.Url = BASE_URL + url;

            return await _httpClient.Get(options).ConfigureAwait(false);
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
            HttpRequestOptions options = GetOptions(userToken);
            options.Url = BASE_URL + url;
            if (data != null) options.RequestContent = _json.SerializeToString(data);

            return (await _httpClient.Post(options).ConfigureAwait(false)).Content;
        }
    }
}
