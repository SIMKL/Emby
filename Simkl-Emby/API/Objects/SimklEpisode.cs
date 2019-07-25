namespace Simkl.Api.Objects {
    public class SimklEpisode : SimklMediaObject {
        public string watched_at;
        public override SimklIds ids { get; set; }
    }
}