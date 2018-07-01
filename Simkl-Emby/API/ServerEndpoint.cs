using System;
using System.Collections.Generic;
using System.Text;

using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

using Simkl.Api.Responses;

namespace Simkl.Api
{
    [Route("/Simkl/oauth/pin", "GET")]
    public class GetPin : IReturn <CodeResponse>
    {
        // Doesn't receive anything
    }

    class ServerEndpoint : IService
    {
        private readonly SimklApi _api;
        private readonly ILogger _logger;
        // json?

        public ServerEndpoint(SimklApi api, ILogger logger)
        {
            _api = api;
            _logger = logger;
        }

        public CodeResponse Get(GetPin request)
        {
            return _api.getCode().Result;
        }
    }
}
