using System;
using System.Collections.Generic;
using System.Linq;

namespace Simkl.Api.Objects
{
    public class SimklIds
    {
        public int? simkl { get; set; }
        public string imdb { get; set; }
        public string tmdb { get; set; }
        public string tvdb { get; set; }
        public string mal { get; set; }
        public string anidb { get; set; }
        public string anilist { get; set; }

        public SimklIds() { }

        public SimklIds(Dictionary<string, string> providerIds)
        {
            imdb = Get(providerIds, "Imdb");
            tmdb = Get(providerIds, "Tmdb");
            tvdb = Get(providerIds, "Tvdb");
            mal = Get(providerIds, "MyAnimeList");
            anidb = Get(providerIds, "AniDB");
            anilist = Get(providerIds, "AniList");
        }

        public static string Get(Dictionary<string, string> providerIds, string key)
        {
            return providerIds?.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        }
    }
}
