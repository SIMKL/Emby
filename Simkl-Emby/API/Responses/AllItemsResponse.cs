using System.Collections.Generic;

using Simkl.Api.Objects;

namespace Simkl.Api.Responses
{
    // GET /sync/all-items/{type}/{status}; an empty library answers {} so every list may be null
    public class AllItemsResponse
    {
        public List<AllItemsEntry> shows { get; set; }
        public List<AllItemsEntry> movies { get; set; }
        public List<AllItemsEntry> anime { get; set; }
    }

    public class AllItemsEntry
    {
        public string status { get; set; }
        public string last_watched_at { get; set; }
        public string anime_type { get; set; }
        public int[] mapped_tvdb_seasons { get; set; }
        public SimklShow show { get; set; }
        public SimklMovie movie { get; set; }
        public List<AllItemsSeason> seasons { get; set; }
    }

    public class AllItemsSeason
    {
        public int number { get; set; }
        public List<AllItemsEpisode> episodes { get; set; }
    }

    public class AllItemsEpisode
    {
        public int number { get; set; }
        public string watched_at { get; set; }
        public AllItemsTvdbCoords tvdb { get; set; }
        public AllItemsEpisodeIds ids { get; set; }
    }

    // raw DB values that arrive as int or string
    public class AllItemsTvdbCoords
    {
        public string season { get; set; }
        public string episode { get; set; }
    }

    public class AllItemsEpisodeIds
    {
        public int? tvdb_id { get; set; }
    }
}
