﻿using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class SimklShowIds : SimklIds
    {
        public int? tvdb { get; set; }
        public int? mal { get; set; }
        public int? anidb { get; set; }
        public int? hulu { get; set; }
        public int? crunchyroll { get; set; }
        public int? tmdb { get; set; }

        public SimklShowIds (Dictionary<string, string> providerMovieIds) : base(providerMovieIds) {}
    }
}