using System;

namespace Simkl.Configuration
{
    public class UserConfig
    {
        public bool autoscrobble { get; set; }  // Default true, enable false
        public int scr_pct { get; set; }    // Scrobbling percentage
        public int scr_w_pct { get; set; }  // Scrobbling "now watching" percentage (only for movies?)
        public bool bubble { get; set; }     // Show bubble while scrobbling
        public int min_length { get; set; } // Minimum length for scrobbling (in minutes)
        public string userToken { get; set; }   // Is the user logged in
        public int scrobbleTimeout { get; set; } // Time between scrobbling tries
        public Guid id { get; set; }    // Simkl user id

        public UserConfig()
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
