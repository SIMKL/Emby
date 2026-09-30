using System;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;

using Simkl.Configuration;

namespace Simkl.Services
{
    // Nudges a user still signed in with the old pin flow to reconnect, with a toast on the client during playback, at
    // most once every few days. New (OAuth 2) logins are never nudged. Playback is chosen because it is the one moment
    // the user is provably at a client that can show the message, and the toast never holds up a scrobble.
    public class ReloginNudge
    {
        private static readonly TimeSpan Interval = TimeSpan.FromDays(5);

        // The Emby Web toast renders only the body, dropping the header, so the text names Simkl itself.
        private const string Text = "Simkl - reconnect your account in settings.";

        // A timeout is what makes the client show a toast rather than a modal dialog; its value is capped by the client.
        private const long ToastMs = 12000;

        private readonly ISessionManager _sessionManager;
        private readonly ILogger _logger;
        private readonly IUserManager _userManager;

        public ReloginNudge(ISessionManager sessionManager, ILogger logger, IUserManager userManager)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _userManager = userManager;
        }

        public void Remind(UserConfig config, long embyUserId, string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || config == null || string.IsNullOrEmpty(config.userToken) || config.IsOAuth2())
            {
                return;
            }

            // cheap check first; the atomic claim (one caller wins, so two sessions do not both toast) only when it is due
            if (config.lastReloginPromptAt != default(DateTime) && DateTime.UtcNow - config.lastReloginPromptAt < Interval)
            {
                return;
            }

            if (!ConfigurationFactory.MarkReloginPrompted(_userManager, embyUserId, config.userToken, Interval))
            {
                return;
            }

            try
            {
                var send = _sessionManager.SendMessageCommand(sessionId, sessionId, new MessageCommand { Header = "Simkl", Text = Text, TimeoutMs = ToastMs }, CancellationToken.None);
                _ = send.ContinueWith(t => _logger.ErrorException("Couldn't send the Simkl reconnect reminder", t.Exception), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                _logger.Info("Asked session {0} to reconnect its Simkl login; not again for {1} days", sessionId, Interval.TotalDays);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Couldn't send the Simkl reconnect reminder", ex);
            }
        }
    }
}
