using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq; 

using Simkl.Api;

using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Notifications;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using Simkl.Configuration;

namespace Simkl.Services
{
    public class Scrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;   // Needed to set up de startPlayBack and endPlayBack functions
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;
        private readonly INotificationManager _notifications;
        private SimklApi _api;
        private Dictionary<string, string> lastScrobbled;   // Library ID of last scrobbled item

        // public static Scrobbler Instance; Instance = this
        public Scrobbler(IJsonSerializer json, ISessionManager sessionManager, ILogManager logManager, IHttpClient httpClient, INotificationManager notifications)
        {
            _json = json;
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger("Simkl Scrobbler");
            _notifications = notifications;
            _api = new SimklApi(json, _logger, httpClient);
            lastScrobbled = new Dictionary<string,string>();
        }

        public void Run()
        {
            _sessionManager.PlaybackProgress += embyPlaybackProgress;
        }

        public void Dispose()
        {
            _sessionManager.PlaybackProgress -= embyPlaybackProgress;
        }

        public static bool canBeScrobbled(UserConfig config, SessionInfo session) {
            float percentageWatched = (float)(session.PlayState.PositionTicks) / (float)(session.NowPlayingItem.RunTimeTicks) * 100f;

            // If percentage watched is below minimum, can't scrobble
            if (percentageWatched < config.scr_pct) return false;
            // If it's below minimum length, can't scrobble
            if (session.NowPlayingItem.RunTimeTicks < 60 * 10000 * config.min_length) return false;

            BaseItem item = session.FullNowPlayingItem;
            if (item is Movie) return true; // TODO: Working only with movies

            return false;
        }
        
        private void embyPlaybackProgress(object sessions, PlaybackProgressEventArgs e)
        {
            UserConfig userConfig = Plugin.Instance.PluginConfiguration.getByGuid(e.Session.UserId);
            if (userConfig is null || userConfig.userToken == "")
            {
                _logger.Error("Can't scrobble: User " + e.Session.UserName + " not logged in");
                return;
            }

            if (canBeScrobbled(userConfig, e.Session))
            {
                string uid = e.Session.UserId, npid = e.Session.NowPlayingItem.Id;

                if (!lastScrobbled.ContainsKey(uid) || lastScrobbled[uid] != npid) {
                    _logger.Info("Trying to scrobble {0} ({1}) for {2} ({3})", e.Session.NowPlayingItem.Name, e.Session.NowPlayingItem.Id,
                        e.Session.UserName, e.Session.UserId);
                    _api.markAsWatched(e.MediaInfo, userConfig.userToken);
                    _logger.Debug("Scrobbled without errors");
                    lastScrobbled[e.Session.UserId] = e.Session.NowPlayingItem.Id;

                    if (_notifications.GetNotificationTypes().Any(t => t.Type == SimklNotificationsFactory.NOTIFICATION_MOVIE_TYPE && t.Enabled)) {
                        String message = String.Format("The movie {0}", e.Session.NowPlayingItem.Name);

                        int ? year = e.Session.NowPlayingItem.ProductionYear ?? e.Session.NowPlayingItem.DateCreated?.Year;
                        if (year != null)
                            message += " (" + year + ")";

                        message += " has been scrobbled to your account";

                        // TODO: Set url parameter to simkl's movie url
                        _notifications.SendNotification(new NotificationRequest{
                            NotificationType = SimklNotificationsFactory.NOTIFICATION_MOVIE_TYPE,
                            Date = DateTime.UtcNow,
                            Name = "Movie Scrobbled to Simkl!",
                            Description = message,
                            UserIds = new long[] {e.Session.UserInternalId},
                            SendToUserMode = SendToUserType.Custom
                        }, e.Session.FullNowPlayingItem, CancellationToken.None);
                    }
                } else {
                    _logger.Debug("Alredy scrobbled {0} for {1}", e.Session.NowPlayingItem.Name, e.Session.UserName);
                }
            }
        }

    }
}
