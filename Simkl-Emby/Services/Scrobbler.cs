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

namespace Simkl.Services
{
    public class Scrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;   // Needed to set up de startPlayBack and endPlayBack functions
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;
        private SimklApi _api;

        // public static Scrobbler Instance; Instance = this
        public Scrobbler(IJsonSerializer json, ISessionManager sessionManager, ILogManager logManager, IHttpClient httpClient)
        {
            _json = json;
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger("Simkl Scrobbler");
            _api = new SimklApi(json, _logger, httpClient);
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
        
        private async void embyPlaybackProgress(object sessions, PlaybackProgressEventArgs e)
        {
            // _logger.Debug(_json.SerializeToString(sessions));
            // _logger.Debug(_json.SerializeToString(e));
            float percentage = (float)(e.PlaybackPositionTicks) / (float)(e.MediaInfo.RunTimeTicks) * 100;
            _logger.Debug("Percentage watched: " + percentage);
            if (percentage > Plugin.Instance.PluginConfiguration.scr_pct)
            {
                _logger.Debug("Scrobbling because " + percentage + " > " + Plugin.Instance.PluginConfiguration.scr_pct);
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
