using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class SimklShowIds : SimklIds
    {
        public int? mal { get; set; }
        public int? anidb { get; set; }
        public int? hulu { get; set; }
        public int? crunchyroll { get; set; }
        public string zap2it { get; set; }

        public SimklShowIds (Dictionary<string, string> providerMovieIds) : base(providerMovieIds) {}
    }
}
