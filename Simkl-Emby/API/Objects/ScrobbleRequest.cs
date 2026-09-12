using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class ScrobbleRequest
    {
        public double progress { get; set; }
        public SimklMovie movie { get; set; }
        public SimklShow show { get; set; }
        public ScrobbleEpisode episode { get; set; }

        public ScrobbleRequest(double watchedPercent)
        {
            progress = System.Math.Round(watchedPercent, 2);
        }
    }

    public class ScrobbleEpisode
    {
        public int? season { get; set; }
        public int? number { get; set; }
        public ScrobbleEpisodeIds ids { get; set; }
    }

    public class ScrobbleEpisodeIds
    {
        public string tvdb { get; set; }
        public string anidb { get; set; }

        public ScrobbleEpisodeIds(Dictionary<string, string> providerIds)
        {
            tvdb = SimklIds.Get(providerIds, "Tvdb");
            anidb = SimklIds.Get(providerIds, "AniDB");
        }
    }
}
