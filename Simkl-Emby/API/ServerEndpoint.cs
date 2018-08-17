using System;

using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
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

    [Route("/Simkl/users/settings", "GET")]
    public class GetUserSettings : IReturn<UserSettings>
    {
        // Note: In the future, when we'll have config for more than one user, we'll use a parameter
        [ApiMember(Name = "client_id", Description = "client 'password'", IsRequired = true, DataType = "Guid", ParameterType = "path", Verb = "POST")]
        public Guid userId { get; set; }
    }

    class ServerEndpoint : IService
    {
        private readonly SimklApi _api;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;

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

        public UserSettings Get(GetUserSettings request)
        {
            return _api.getUserSettings(request.userId).Result;
        }
    }
}
