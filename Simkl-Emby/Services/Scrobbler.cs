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
using MediaBrowser.Model.Dto;

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

        public bool canBeScrobbled(long? playBackPositionTicks, long? runTimeTicks)
        {
            // Note: 1 tick = 0.1 ms, 1 min = 60 * 1000 * 10 tick
            float percentageWatched = (float)playBackPositionTicks / (float)(runTimeTicks) * 100;
            bool greaterThanMinLength = runTimeTicks > 60 * 10000 * Plugin.Instance.PluginConfiguration.min_length;
            return percentageWatched > Plugin.Instance.PluginConfiguration.scr_pct && greaterThanMinLength;
        }
        
        private async void embyPlaybackProgress(object sessions, PlaybackProgressEventArgs e)
        {
            // _logger.Debug(_json.SerializeToString(sessions));
            // _logger.Debug(_json.SerializeToString(e));
            bool v = lastScrobbled != e.MediaSourceId;
            _logger.Debug("Current time: " + DateTime.Now + ", next scrobble: " + nextScrobble + ", notScrobbled: " + v);
            if (v && canBeScrobbled(e.PlaybackPositionTicks, e.MediaInfo.RunTimeTicks) && DateTime.Now > nextScrobble)
            {
                nextScrobble = DateTime.Now.AddSeconds(Plugin.Instance.PluginConfiguration.scrobbleTimeout);
                lastScrobbled = e.MediaSourceId;
                _logger.Debug("Scrobbling");
                try
                {
                    _api.markAsWatched(e.MediaInfo, Plugin.Instance.PluginConfiguration.userToken);
                }
                catch (NotImplementedException)
                {
                    _logger.Warn("That wasn't a movie");
                }
                catch
                {
                    throw;
                }
            }
        }

        private async void embyPlaybackStart(object obj, PlaybackProgressEventArgs e)
        {
            _logger.Debug("EMBY PLAYBACK STARTED");
            _logger.Debug("Obj: " + _json.SerializeToString(obj));
            _logger.Debug("Event: " + _json.SerializeToString(e));
        }

    }
}
