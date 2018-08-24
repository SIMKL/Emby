using System;
using MediaBrowser.Controller.Net;
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

    [Route("/Simkl/users/settings/{userId}", "GET")]
    public class GetUserSettings : IReturn<UserSettings>
    {
        // Note: In the future, when we'll have config for more than one user, we'll use a parameter
        [ApiMember(Name = "id", Description = "emby's user id", IsRequired = true, DataType = "Guid", ParameterType = "path", Verb = "GET")]
        public Guid userId { get; set; }
    }

    class ServerEndpoint : IService, IHasResultFactory
    {
        public IHttpResultFactory ResultFactory { get; set; }
        public IRequest Request { get; set; }

        private readonly SimklApi _api;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;

        public ServerEndpoint(SimklApi api, ILogger logger, IJsonSerializer json)
        {
            _api = api;
            _logger = logger;
            _json = json;
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
            _logger.Debug(_json.SerializeToString(request));
            return _api.getUserSettings(Plugin.Instance.Configuration.getByGuid(request.userId).userToken).Result;
        }
    }
}
