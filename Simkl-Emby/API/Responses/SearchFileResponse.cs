using System;
using Simkl.Api.Objects;

namespace Simkl.Api.Responses {
    public class SearchFileResponse {
        public string type { get; set; }

        public SimklEpisode episode { get; set; }
        public SimklMovie movie { get; set; }
    }
}