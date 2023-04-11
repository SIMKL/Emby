using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class SimklMovieIds : SimklIds
    {
        public int? mal { get; set; }
        public int? anidb { get; set; }
        public int? hulu { get; set; }
        public int? crunchyroll { get; set; }
        public string moviedb { get; set; }

        public SimklMovieIds (Dictionary<string, string> providerMovieIds) : base(providerMovieIds) {}
    }
}
