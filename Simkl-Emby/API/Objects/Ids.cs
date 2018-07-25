using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class Ids
    {
        public int? simkl { get; set; }
        public int? tvdb { get; set; }
        public string imdb { get; set; }
        public int? mal { get; set; }
        public int? anidb { get; set; }
        public int? hulu { get; set; }
        public int? crunchyroll { get; set; }
        public int? tmdb { get; set; }

        /// <summary>
        /// Creates an Ids object given a dictionary
        /// </summary>
        public Ids (Dictionary<string, string> ProviderIds)
        {
            imdb = ProviderIds["Imdb"];
            tmdb = int.Parse(ProviderIds["Tmdb"]);
        }
    }
}
