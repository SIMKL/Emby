using System;

namespace Simkl.Configuration
{
    public class UserConfig
    {
        public bool scrobbleMovies { get; set; }  // Default true, enable false
        public bool scrobbleShows { get; set; } // Default true, enable false
        public int scr_pct { get; set; }    // Scrobbling percentage
        public int scr_w_pct { get; set; }  // Scrobbling "now watching" percentage (only for movies?)
        public int min_length { get; set; } // Minimum length for scrobbling (in minutes)
        public string userToken { get; set; }   // Is the user logged in
        public int scrobbleTimeout { get; set; } // Time between scrobbling tries
        public string guid { get; set; }    // Emby user id

        public UserConfig()
        {
            this.scrobbleMovies = true;
            this.scrobbleShows = true;
            this.scr_pct = 70;
            this.scr_w_pct = 5;
            this.min_length = 5;
            this.userToken = "";    // Todo: check if token is still valid
            this.scrobbleTimeout = 30;
        }
    }
}
