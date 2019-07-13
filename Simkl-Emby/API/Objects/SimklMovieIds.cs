using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class SimklMovieIds : SimklIds
    {
        public int? tvdb { get; set; }
        public int? mal { get; set; }
        public int? anidb { get; set; }
        public int? hulu { get; set; }
        public int? crunchyroll { get; set; }
        public int? tmdb { get; set; }

        public SimklMovieIds (Dictionary<string, string> providerMovieIds) : base(providerMovieIds) {}
    }
}
