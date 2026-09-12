namespace Simkl.Configuration
{
    public class UserConfig
    {
        public bool scrobbleMovies { get; set; }
        public bool scrobbleShows { get; set; }
        public bool allowRewatch { get; set; }          // stop carries allow_rewatch=yes so a title already in the history is logged as a rewatch (Simkl Pro/VIP)
        public int min_length { get; set; }     // Minimum length for scrobbling (in minutes)
        public string userToken { get; set; }   // Empty when the user is logged out
        public string guid { get; set; }        // Emby user id; only read by the one-shot migration from the old global config
        public bool autoImport { get; set; }            // Scheduled one-way Simkl -> Emby watched import; the import buttons work regardless
        public int autoImportHours { get; set; }        // How often the hourly task checks this user's Simkl activity

        public UserConfig()
        {
            scrobbleMovies = true;
            scrobbleShows = true;
            min_length = 5;
            autoImportHours = 24;
            userToken = "";
        }
    }
}
