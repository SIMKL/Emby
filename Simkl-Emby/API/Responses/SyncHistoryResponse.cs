using System.Collections.Generic;
using Simkl.Api.Objects;

namespace Simkl.Api.Responses
{
    // answer of POST /sync/history (added) and /sync/history/remove (deleted); not_found echoes the entries Simkl could not identify
    public class SyncHistoryResponse
    {
        public HistoryCounts added { get; set; }
        public HistoryCounts deleted { get; set; }
        public HistoryNotFound not_found { get; set; }
    }

    public class HistoryCounts
    {
        public int movies { get; set; }
        public int shows { get; set; }
        public int episodes { get; set; }
    }

    public class HistoryNotFound
    {
        public List<HistoryMovie> movies { get; set; }
        public List<HistoryShow> shows { get; set; }
        public List<HistoryEpisode> episodes { get; set; }
    }
}
