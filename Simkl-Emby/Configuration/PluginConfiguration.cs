// using System;
// using System.Collections.Generic;
// using System.Text;
using MediaBrowser.Model.Plugins;

namespace Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configurate it
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration // From MediaBrowser.Model.Plugins
    {
        // id's based on simkl's kodi add-on
        // https://github.com/SIMKL/script.simkl/blob/master/resources/settings.xml

        public bool autoscrobble { get; set; }  // Default true, enable false
        public int scr_pct { get; set; }    // Scrobbling percentage
        public int scr_w_pct { get; set; }  // Scrobbling "now watching" percentage (only for movies?)
        public bool bubble { get; set; }     // Show bubble while scrobbling
        public int min_length { get; set; } // Minimum length for scrobbling (in minutes)
        public string userToken { get; set; }   // Is the user logged in
        public int scrobbleTimeout { get; set; } // Time between scrobbling tries

        public PluginConfiguration()    // Default values
        {
            this.autoscrobble = true;
            this.scr_pct = 70;
            this.scr_w_pct = 5;
            this.bubble = true;
            this.min_length = 5;
            this.userToken = "";    // Todo: check if token is still valid
            this.scrobbleTimeout = 30;
        }

    }
}
