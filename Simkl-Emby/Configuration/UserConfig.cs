using System;

namespace Simkl.Configuration
{
    public class UserConfig
    {
        // an access token minted by the OAuth 2 device flow starts with this; a token without it is a legacy pin-flow one
        public const string OAuth2Prefix = "simkl_at_";

        // renew a day before the seven-day access token expires, leaving room for a server that was off
        private static readonly TimeSpan RenewBefore = TimeSpan.FromHours(24);

        public bool scrobbleMovies { get; set; }
        public bool scrobbleShows { get; set; }
        public bool syncWatchedMarks { get; set; }      // marking played/unplayed by hand is mirrored to the Simkl history; on by default, independent of the two above
        public bool allowRewatch { get; set; }          // stop carries allow_rewatch=yes so a title already in the history is logged as a rewatch (Simkl Pro/VIP)
        public int min_length { get; set; }     // Minimum length for scrobbling (in minutes)
        public string userToken { get; set; }   // Empty when the user is logged out; holds a simkl_at_ token for a device-flow login
        public string refreshToken { get; set; }        // Empty for a legacy login; the device flow's non-rotating refresh token
        public DateTime tokenExpiresAt { get; set; }     // When the access token expires (UTC); default for a legacy token, which never expires
        public string tokenScope { get; set; }           // Space-separated scope the grant was given
        public DateTime lastReloginPromptAt { get; set; } // When a legacy user was last asked to reconnect (UTC)
        public string guid { get; set; }        // Emby user id; only read by the one-shot migration from the old global config
        public bool autoImport { get; set; }            // Scheduled one-way Simkl -> Emby watched import; the import buttons work regardless
        public int autoImportHours { get; set; }        // How often the hourly task checks this user's Simkl activity

        public UserConfig()
        {
            scrobbleMovies = true;
            scrobbleShows = true;
            syncWatchedMarks = true;    // a user stored before this setting existed has no value for it and keeps mirroring
            min_length = 5;
            autoImportHours = 24;
            userToken = "";
            refreshToken = "";
            tokenScope = "";
        }

        // a token minted by the device flow, told apart from a legacy pin-flow one by its prefix; the client id a call
        // goes out under follows this, so a legacy token keeps resolving under the app that issued it
        public static bool IsOAuth2Token(string token)
        {
            return token != null && token.StartsWith(OAuth2Prefix, StringComparison.Ordinal);
        }

        // Kept as methods, not properties, so the typed-user-setting serializer never writes them back as stored state.
        public bool IsOAuth2()
        {
            return IsOAuth2Token(userToken);
        }

        public bool NeedsRefresh()
        {
            return IsOAuth2() && !string.IsNullOrEmpty(refreshToken)
                && (tokenExpiresAt == default(DateTime) || tokenExpiresAt - DateTime.UtcNow < RenewBefore);
        }

        // the five server-owned fields move together: the page never writes them, so a login, a renewal and a logout
        // each carry the whole set or none of it
        public void CopyTokensFrom(UserConfig other)
        {
            userToken = other.userToken;
            refreshToken = other.refreshToken;
            tokenExpiresAt = other.tokenExpiresAt;
            tokenScope = other.tokenScope;
            lastReloginPromptAt = other.lastReloginPromptAt;
        }

        public void ClearTokens()
        {
            userToken = "";
            refreshToken = "";
            tokenExpiresAt = default(DateTime);
            tokenScope = "";
        }

        // Identifies the Simkl grant across access-token renewals. The access token rotates about weekly, the refresh
        // token does not, so anything that must tell one login from the next (the import cursor) keys on this, not on
        // userToken, or a renewal would look like a new account and throw the cursors away.
        public string AccountAnchor()
        {
            return IsOAuth2() && !string.IsNullOrEmpty(refreshToken) ? refreshToken : userToken;
        }
    }
}
