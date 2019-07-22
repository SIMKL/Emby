namespace Simkl.Api.Objects {
    public class SimklEpisode : MediaObject {
        public string watched_at;
        public override SimklIds ids { get; set; }
    }
}