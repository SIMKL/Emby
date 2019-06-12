using System;
using System.Collections.Generic;
using System.Text;

using Simkl.Api;

using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
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
        private SimklApi _api;
        private DateTime nextScrobble;
        private string lastScrobbled;   // Library ID of last scrobbled item

        // public static Scrobbler Instance; Instance = this
        public Scrobbler(IJsonSerializer json, ISessionManager sessionManager, ILogManager logManager, IHttpClient httpClient)
        {
            _json = json;
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger("Simkl Scrobbler");
            _api = new SimklApi(json, _logger, httpClient);
            nextScrobble = DateTime.Now;
        }

        public void Run()
        {
            _sessionManager.PlaybackProgress += embyPlaybackProgress;
            _sessionManager.PlaybackStart += embyPlaybackStart;
            // _sessionManager.PlaybackStop
        }

        public void Dispose()
        {
            _sessionManager.PlaybackProgress -= embyPlaybackProgress;
            _sessionManager.PlaybackStart -= embyPlaybackStart;
        }

        private bool canBeScrobbled(long? playBackPositionTicks, long? runTimeTicks, int min_length, int scr_pct)
        {
            // Note: 1 tick = 0.1 ms, 1 min = 60 * 1000 * 10 tick
            float percentageWatched = (float)playBackPositionTicks / (float)(runTimeTicks) * 100;
            bool greaterThanMinLength = runTimeTicks > 60 * 10000 * min_length;
            return percentageWatched > scr_pct && greaterThanMinLength && DateTime.Now > nextScrobble;
        }
        
        private void embyPlaybackProgress(object sessions, PlaybackProgressEventArgs e)
        {
            // _logger.Debug(_json.SerializeToString(sessions));
            // _logger.Debug(_json.SerializeToString(e));
            bool v = lastScrobbled != e.MediaSourceId;
            _logger.Debug("Current time: " + DateTime.Now + ", next scrobble: " + nextScrobble + ", notScrobbled: " + v);
            _logger.Debug("PlaybackProgressEventArgs: " + _json.SerializeToString(e));
            _logger.Debug(e.Session.UserId.ToString());
            UserConfig userConfig = Plugin.Instance.PluginConfiguration.getByGuid(System.Guid.Parse(e.Session.UserId));
            if (userConfig.userToken == "")
            {
                _logger.Info("Can't scrobble: User " + e.Session.UserName + " not logged in");
                v = false;
            }
            if (v && canBeScrobbled(e.PlaybackPositionTicks, e.MediaInfo.RunTimeTicks, userConfig.min_length, userConfig.scr_pct))
            {
                nextScrobble = DateTime.Now.AddSeconds(userConfig.scrobbleTimeout);
                lastScrobbled = e.MediaSourceId;
                _api.markAsWatched(e.MediaInfo, userConfig.userToken);
            }
        }

        private void embyPlaybackStart(object obj, PlaybackProgressEventArgs e)
        {
            _logger.Debug("EMBY PLAYBACK STARTED");
            _logger.Debug("Obj: " + _json.SerializeToString(obj));
            _logger.Debug("Event: " + _json.SerializeToString(e));
        }

    }
}
