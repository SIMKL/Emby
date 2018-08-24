using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Objects
{
    public class UserSettings
    {
        public User user { get; set; }
        public Account account { get; set; }
        public string error { get; set; }
    }

    public class User
    {
        public string name { get; set; }
        public DateTime joined_at { get; set; }
        public string gender { get; set; }
        public string avatar { get; set; }
        public string bio { get; set; }
        public string loc { get; set; }
        public string age { get; set; }
    }

    public class Account
    {
        public int id { get; set; }
        public string timezone { get; set; }
    }

    public class Connections
    {
        public bool facebook { get; set; }
    }
}
