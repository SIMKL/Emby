using System;
using System.Collections.Generic;
using System.Text;
// using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

using Simkl.Api.Objects;


namespace Simkl.Api
{
    class SimklApi
    {
        /* INTERFACES */
        private readonly IJsonSerializer _json;
        private readonly ILogger logger;
        private readonly IHttpClient _httpClient;

        /* BASIC API THINGS */
        public const string REDIRECT_URI = @"urn:ietf:wg:oauth:2.0:oob";
        public const string APIKEY = @"27dd5d6adc24aa1ad9f95ef913244cbaf6df5696036af577ed41670473dc97d0";
        public const string SECRET = @"d7b9feb9d48bbaa69dbabaca21ba4671acaa89198637e9e136a4d69ec97ab68b";
        public const string BASE_URL = @"https://api.simkl.com";

        public SimklApi(IJsonSerializer json, ILogger logger, IHttpClient httpClient)
        {
            _json = json;
            this.logger = logger;
            _httpClient = httpClient;
        }

        // It should return the unserialized response (string)
        // TODO: Return serialized response
        /// <summary>
        /// Scrobbles a movie to simkl
        /// </summary>
        /// <param name="movie">The movie object to scrobble</param>
        /// <param name="progress">Progress</param>
        /// <returns>Serialized response</returns>
        public async Task<string> ScrobbleMovieAsync(Movie movie, int progress) {
        }
    }
}
