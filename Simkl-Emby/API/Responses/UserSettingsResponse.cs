namespace Simkl.Api.Responses
{
    // POST /users/settings; only the fields the plugin shows are declared
    public class UserSettingsResponse
    {
        public UserSettingsUser user { get; set; }
        public UserSettingsAccount account { get; set; }
    }

    public class UserSettingsUser
    {
        public string name { get; set; }
    }

    public class UserSettingsAccount
    {
        public int? id { get; set; }
        public string type { get; set; }    // free, pro or vip
    }

    // what the plugin page gets back from GET /Simkl/account/{userId}
    public class SimklAccount
    {
        public int? id { get; set; }
        public string name { get; set; }
        public string plan { get; set; }    // free, pro or vip; the page shows the plan and asks free accounts for support
        public bool legacy { get; set; }    // true when still signed in with the old pin flow; the page offers to reconnect
    }
}
