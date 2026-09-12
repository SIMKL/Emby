using System;

using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Services;

using Simkl.Api.Exceptions;
using Simkl.Api.Responses;
using Simkl.Configuration;
using Simkl.Services;

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

    [Route("/Simkl/import/{userId}", "POST")]
    [Authenticated]
    public class StartImport : IReturn<ImportStatus>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose Simkl history is imported", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string userId { get; set; }
    }

    [Route("/Simkl/import/{userId}/reset", "POST")]
    [Authenticated]
    public class StartResetImport : IReturn<ImportStatus>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose import memory is cleared before a full import that also refreshes already played items", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string userId { get; set; }
    }

    [Route("/Simkl/import/{userId}/check", "POST")]
    [Authenticated]
    public class StartAutoCheck : IReturn<ImportStatus>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose Simkl history is checked the way the scheduled task does", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string userId { get; set; }
        [ApiMember(Name = "automatic", Description = "true when switching automatic import on: the check then counts as the first automatic run", IsRequired = false, DataType = "bool", ParameterType = "query", Verb = "POST")]
        public bool automatic { get; set; }
    }

    [Route("/Simkl/import/{userId}/status", "GET")]
    [Authenticated]
    public class GetImportStatus : IReturn<ImportStatus>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose import status is read", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string userId { get; set; }
    }

    [Route("/Simkl/account/{userId}", "GET")]
    [Authenticated]
    public class GetAccount : IReturn<SimklAccount>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose Simkl account is read", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string userId { get; set; }
    }

    class ServerEndpoint : IService, IRequiresRequest
    {
        private readonly SimklApi _api;
        private readonly IAuthorizationContext _authContext;
        private readonly IUserManager _userManager;
        private readonly HistoryImporter _importer;

        public IRequest Request { get; set; }

        public ServerEndpoint(SimklApi api, IAuthorizationContext authContext, IUserManager userManager, HistoryImporter importer)
        {
            _api = api;
            _authContext = authContext;
            _userManager = userManager;
            _importer = importer;
        }

        public CodeResponse Get(GetPin request)
        {
            return _api.getCode().Result;
        }

        public CodeStatusResponse Get(GetPinStatus request)
        {
            return _api.getCodeStatus(request.user_code).Result;
        }

        // Kept synchronous on purpose: Task-returning IService handlers never dispatch on Emby's ServiceStack fork
        public ImportStatus Post(StartImport request)
        {
            var user = AuthorizedTarget(request.userId);
            return _importer.TryStart(user, LoadConfig(user));
        }

        public ImportStatus Post(StartResetImport request)
        {
            var user = AuthorizedTarget(request.userId);
            return _importer.TryStart(user, LoadConfig(user), true);
        }

        public ImportStatus Post(StartAutoCheck request)
        {
            var user = AuthorizedTarget(request.userId);
            return _importer.TryStartAuto(user, LoadConfig(user), request.automatic);
        }

        public ImportStatus Get(GetImportStatus request)
        {
            return _importer.Status(AuthorizedTarget(request.userId));
        }

        // the Simkl user id and name behind the stored token, so the page can link to the user's own Simkl pages
        public SimklAccount Get(GetAccount request)
        {
            var user = AuthorizedTarget(request.userId);
            var config = LoadConfig(user);
            if (string.IsNullOrEmpty(config.userToken)) return new SimklAccount();
            try
            {
                var settings = _api.GetUserSettings(config, user.InternalId).Result;
                return new SimklAccount { id = settings?.account?.id, name = settings?.user?.name, plan = settings?.account?.type };
            }
            catch (AggregateException ex) when (ex.InnerException is InvalidTokenException || ex.InnerException is HttpException)
            {
                return new SimklAccount();
            }
        }

        // The caller may act on itself; an administrator on anyone
        private User AuthorizedTarget(string userId)
        {
            var caller = _authContext.GetAuthorizationInfo(Request).User;
            if (caller == null) throw new SecurityException("A user session is required", SecurityExceptionType.Unauthenticated);

            var target = Guid.TryParse(userId, out var id) ? _userManager.GetUserById(id) : null;
            if (target == null) throw new ResourceNotFoundException("User not found");
            if (target.InternalId != caller.InternalId && !caller.Policy.IsAdministrator)
                throw new SecurityException("Only the user or an administrator can import history", SecurityExceptionType.Generic);

            return target;
        }

        private UserConfig LoadConfig(User user)
        {
            return ConfigurationFactory.LoadUser(_userManager, user.InternalId) ?? new UserConfig();
        }
    }
}
