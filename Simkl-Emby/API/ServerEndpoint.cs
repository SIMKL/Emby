using System;
using System.Collections.Generic;
using System.Text;

using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

using Simkl.Api.Objects;
using Simkl.Api.Responses;

namespace Simkl.Api
{
    [Route("/Simkl/oauth/pin", "GET")]
    public class GetPin : IReturn <CodeResponse>
    {
        // Doesn't receive anything
    }

    [Route("/Simkl/oauth/pin/{user_code}", "GET")]
    public class GetPinStatus : IReturn <CodeStatusResponse>
    {
        [ApiMember(Name = "user_code", Description = "pin to be introduced by the user", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string user_code { get; set; }
    }

    [Route("/Simkl/users/settings", "POST")]
    public class GetUserSettings : IReturn<UserSettings>
    {
        [ApiMember(Name = "client_id", Description = "client 'password'", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string client_id { get; set; }
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

        public CodeStatusResponse Get(GetPinStatus request)
        {
            return _api.getCodeStatus(request.user_code).Result;
        }

        public UserSettings Post(GetUserSettings request)
        {
            return _api.getUserSettings("fb7a0d9994662121be2c646c287704e41aa8689733b30c74390b69de1931d912").Result;
        }
    }
}
