using Simkl.Api.Objects;

namespace Simkl.Api.Responses {
    public class SyncHistoryResponseCount {
        public int movies { get; set; }
        public int shows { get; set; }
        public int episodes { get; set; }
    }

    public class SyncHistoryNotFound {
        public SimklMovie[] movies {get; set; }
        public SimklShow[] shows {get; set; }
        public SimklEpisode[] episodes {get; set; }
    }

    public class SyncHistoryResponse {
        public SyncHistoryResponseCount added { get; set; }
        public SyncHistoryNotFound not_found {get; set; }
    }
}