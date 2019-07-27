namespace Simkl.Api.Objects {
    public class SimklEpisode : SimklMediaObject {
        public string watched_at;
        public override SimklIds ids { get; set; }
        public string title { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
        public bool? multipart { get; set; }
    }
}