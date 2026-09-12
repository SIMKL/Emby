using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    // POST /sync/history: what a user marked as watched by hand in Emby
    public class HistoryRequest
    {
        public List<HistoryMovie> movies { get; set; } = new List<HistoryMovie>();
        public List<HistoryShow> shows { get; set; } = new List<HistoryShow>();
        public List<HistoryEpisode> episodes { get; set; } = new List<HistoryEpisode>();   // found by their own TVDB id, no season mapping involved
    }

    public class HistoryMovie : SimklMovie
    {
        public string watched_at { get; set; }
    }

    public class HistoryShow : SimklShow
    {
        public List<HistorySeason> seasons { get; set; } = new List<HistorySeason>();
    }

    public class HistorySeason
    {
        public int number { get; set; }
        public List<HistoryEpisode> episodes { get; set; } = new List<HistoryEpisode>();
    }

    public class HistoryEpisode
    {
        public int? number { get; set; }
        public string watched_at { get; set; }
        public ScrobbleEpisodeIds ids { get; set; }
    }
}
