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
    [Route("/Simkl/oauth/device/{userId}", "POST")]
    [Authenticated]
    public class StartDeviceLogin : IReturn<DeviceLoginStatus>
    {
        [ApiMember(Name = "userId", Description = "Emby user starting a Simkl sign-in", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string userId { get; set; }
    }

    [Route("/Simkl/oauth/device/{userId}/poll", "POST")]
    [Authenticated]
    public class PollDeviceLogin : IReturn<DeviceLoginStatus>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose pending sign-in is polled", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string userId { get; set; }
    }

    [Route("/Simkl/oauth/device/{userId}/cancel", "POST")]
    [Authenticated]
    public class CancelDeviceLogin : IReturn<DeviceLoginStatus>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose pending sign-in is cancelled", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string userId { get; set; }
    }

    [Route("/Simkl/logout/{userId}", "POST")]
    [Authenticated]
    public class LogOut : IReturn<SimklAccount>
    {
        [ApiMember(Name = "userId", Description = "Emby user whose Simkl account is disconnected", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string userId { get; set; }
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
        private readonly MediaBrowser.Model.Logging.ILogger _logger;

        public IRequest Request { get; set; }

        public ServerEndpoint(SimklApi api, IAuthorizationContext authContext, IUserManager userManager, HistoryImporter importer, MediaBrowser.Model.Logging.ILogManager logManager)
        {
            _api = api;
            _authContext = authContext;
            _userManager = userManager;
            _importer = importer;
            _logger = logManager.GetLogger("Simkl");
        }

        // Kept synchronous on purpose: Task-returning IService handlers never dispatch on Emby's ServiceStack fork.
        // The device code Simkl mints stays on the server; the page is handed only the user code and the link, and the
        // completed token is stored server-side, never returned to the browser.
        public DeviceLoginStatus Post(StartDeviceLogin request)
        {
            var user = AuthorizedTarget(request.userId);
            DeviceCodeResponse code;
            try
            {
                code = _api.StartDeviceLogin(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // a network failure reaching Simkl is answered as a status the page can show, not as an HTTP 500
                _logger.Warn("Simkl device login could not be started: {0}", ex.Message);
                code = null;
            }

            if (code == null)
            {
                return new DeviceLoginStatus { status = DeviceLoginStatus.Error, message = "Could not reach Simkl. Try again." };
            }

            var seconds = code.expires_in > 0 ? code.expires_in : 900;
            var interval = Math.Max(5, code.interval);
            DeviceLoginStore.Start(user.InternalId, code.device_code, DateTime.UtcNow.AddSeconds(seconds), interval);
            return new DeviceLoginStatus
            {
                status = DeviceLoginStatus.Pending,
                userCode = code.user_code,
                verificationUri = code.verification_uri,
                verificationUriComplete = code.verification_uri_complete,
                expiresIn = seconds,
                interval = interval
            };
        }

        public DeviceLoginStatus Post(PollDeviceLogin request)
        {
            var user = AuthorizedTarget(request.userId);
            var pending = DeviceLoginStore.Get(user.InternalId);
            if (pending == null || DateTime.UtcNow > pending.ExpiresUtc)
            {
                if (pending != null) DeviceLoginStore.Cancel(user.InternalId, pending);
                return new DeviceLoginStatus { status = DeviceLoginStatus.Expired };
            }

            (TokenResponse Tokens, string Error) result;
            try
            {
                result = _api.PollDeviceLogin(pending.DeviceCode, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // the network dropped this one poll; the code is still good, so keep waiting. Warn, not Error: a poll
                // every few seconds through a Simkl outage would otherwise fill the log with errors for one condition
                _logger.Warn("Simkl device login poll failed, will retry: {0}", ex.Message);
                return new DeviceLoginStatus { status = DeviceLoginStatus.Pending, interval = pending.Interval };
            }

            if (result.Tokens != null && !string.IsNullOrEmpty(result.Tokens.access_token))
            {
                // store only if this poll's own attempt is still the pending one: a Cancel or a restart that dropped it
                // while this poll was in flight must not bring an abandoned login back to life
                if (!DeviceLoginStore.Cancel(user.InternalId, pending))
                {
                    return new DeviceLoginStatus { status = DeviceLoginStatus.Expired };
                }

                var expiresAt = DateTime.UtcNow.AddSeconds(result.Tokens.expires_in > 0 ? result.Tokens.expires_in : SimklApi.DefaultAccessLifetimeSeconds);
                ConfigurationFactory.StoreLogin(_userManager, user.InternalId, result.Tokens.access_token, result.Tokens.refresh_token ?? "", expiresAt, result.Tokens.scope ?? "");
                return new DeviceLoginStatus { status = DeviceLoginStatus.Connected };
            }

            var error = result.Error;
            if (error == OAuthErrorResponse.AuthorizationPending)
            {
                pending.Failures = 0;   // a real answer from Simkl: the failure count is for unanswered polls in a row
                return new DeviceLoginStatus { status = DeviceLoginStatus.Pending, interval = pending.Interval };
            }

            if (error == OAuthErrorResponse.SlowDown)
            {
                pending.Failures = 0;
                pending.Interval += 5;
                return new DeviceLoginStatus { status = DeviceLoginStatus.Pending, interval = pending.Interval };
            }

            if (!OAuthErrorResponse.IsTerminal(error))
            {
                // a 502 or a reset: keep the code and let a few of these pass in a row before giving up
                pending.Failures++;
                if (pending.Failures < 12)
                {
                    return new DeviceLoginStatus { status = DeviceLoginStatus.Pending, interval = pending.Interval };
                }

                DeviceLoginStore.Cancel(user.InternalId, pending);
                return new DeviceLoginStatus { status = DeviceLoginStatus.Error, message = "Simkl could not be reached. Try again." };
            }

            DeviceLoginStore.Cancel(user.InternalId, pending);
            if (error == OAuthErrorResponse.ExpiredToken)
            {
                return new DeviceLoginStatus { status = DeviceLoginStatus.Expired };
            }

            return new DeviceLoginStatus { status = DeviceLoginStatus.Error, message = "Sign-in was declined or failed. Try again." };
        }

        public DeviceLoginStatus Post(CancelDeviceLogin request)
        {
            var user = AuthorizedTarget(request.userId);
            DeviceLoginStore.Cancel(user.InternalId);
            return new DeviceLoginStatus { status = DeviceLoginStatus.Expired };
        }

        public SimklAccount Post(LogOut request)
        {
            var user = AuthorizedTarget(request.userId);
            DeviceLoginStore.Cancel(user.InternalId);
            ConfigurationFactory.LogOut(_userManager, user.InternalId);
            return new SimklAccount();
        }

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
                // read legacy after the call: a renewal in GetUserSettings may have swapped the token, and it is the kind
                // of token the account still holds that decides whether the page offers to reconnect
                return new SimklAccount { id = settings?.account?.id, name = settings?.user?.name, plan = settings?.account?.type, legacy = !config.IsOAuth2() };
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
