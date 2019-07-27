using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq; 

using Simkl.Api;
using Simkl.Api.Objects;
using Simkl.Api.Exceptions;

using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
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
        private DateTime nextTry;

        // public static Scrobbler Instance; Instance = this
        public Scrobbler(IJsonSerializer json, ISessionManager sessionManager, ILogManager logManager,
            IHttpClient httpClient, INotificationManager notifications)
        {
            _json = json;
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger("Simkl Scrobbler");
            _notifications = notifications;
            _api = new SimklApi(json, _logger, httpClient);
            lastScrobbled = new Dictionary<string,string>();
            nextTry = DateTime.UtcNow;
        }

        public void Run()
        {
            _sessionManager.PlaybackProgress += embyPlaybackProgress;
        }

        public void Dispose()
        {
            _sessionManager.PlaybackProgress -= embyPlaybackProgress;
            _api = null;
        }

        public static bool canBeScrobbled(UserConfig config, SessionInfo session) {
            float percentageWatched = (float)(session.PlayState.PositionTicks) / (float)(session.NowPlayingItem.RunTimeTicks) * 100f;

            // If percentage watched is below minimum, can't scrobble
            if (percentageWatched < config.scr_pct) return false;
            // If it's below minimum length, can't scrobble
            if (session.NowPlayingItem.RunTimeTicks < 60 * 10000 * config.min_length) return false;

            BaseItem item = session.FullNowPlayingItem;
            if (item is Movie) return config.scrobbleMovies;
            else if (item is Episode) return config.scrobbleShows;

            return false;
        }

        private bool canSendNotification(BaseItemDto item) {
            if (item.IsMovie == true || item.Type == "Movie")
                return _notifications.GetNotificationTypes().Any(t => t.Type == SimklNotificationsFactory.NOTIFICATION_MOVIE_TYPE && t.Enabled);
            
            if (item.IsSeries == true || item.Type == "Episode")
                return _notifications.GetNotificationTypes().Any(t => t.Type == SimklNotificationsFactory.NOTIFICATION_SHOW_TYPE && t.Enabled);

            return false;
        }
        
        private async void embyPlaybackProgress(object sessions, PlaybackProgressEventArgs e)
        {
            try {
                if (DateTime.UtcNow < nextTry) return;
                nextTry = DateTime.UtcNow.AddSeconds(30);
                
                UserConfig userConfig = Plugin.Instance.PluginConfiguration.getByGuid(e.Session.UserId);
                if (userConfig == null || userConfig.userToken == "")
                {
                    _logger.Error("Can't scrobble: User " + e.Session.UserName + " not logged in (" + (userConfig == null) + ")");
                    return;
                }

                if (!canBeScrobbled(userConfig, e.Session)) return;

                string uid = e.Session.UserId, npid = e.Session.NowPlayingItem.Id;
                if (lastScrobbled.ContainsKey(uid) && lastScrobbled[uid] == npid) {
                    _logger.Debug("Alredy scrobbled {0} for {1}", e.Session.NowPlayingItem.Name, e.Session.UserName);
                    return;
                }

                _logger.Debug(_json.SerializeToString(e.Session.NowPlayingItem));
                _logger.Info("Trying to scrobble {0} ({1}) for {2} ({3})", 
                    e.Session.NowPlayingItem.Name, e.Session.NowPlayingItem.Id,
                    e.Session.UserName, e.Session.UserId);

                var response = await _api.markAsWatched(e.MediaInfo, userConfig.userToken);
                if(response.success) {
                    _logger.Debug("Scrobbled without errors");
                    lastScrobbled[e.PlaySessionId] = e.Session.NowPlayingItem.Id;

                    if (canSendNotification(response.item)) {
                        await _notifications.SendNotification(
                            SimklNotificationsFactory.GetNotificationRequest(response.item, e.Session.UserInternalId),
                            e.Session.FullNowPlayingItem, CancellationToken.None);
                    }
                }
            } catch (InvalidTokenException) {
                _logger.Info("Deleted user token");
            } catch (Exception ex) {
                _logger.Error("Caught unknown exception while trying to scrobble: " + ex.Message);
                _logger.Error(ex.StackTrace);
            }
        }

    }
}
