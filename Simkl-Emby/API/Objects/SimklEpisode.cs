using MediaBrowser.Model.Dto;

namespace Simkl.Api.Objects {
    public class SimklEpisode : SimklMediaObject {
        public string watched_at;
        public override SimklIds ids { get; set; }
        public string title { get; set; }
        public int? year { get; set; }
        public int? season { get; set; }
        public int? episode { get; set; }
        public bool? multipart { get; set; }

        public SimklEpisode(BaseItemDto MediaInfo)
        {
            title = MediaInfo.SeriesName;
            ids = new SimklIds(MediaInfo.ProviderIds);
            year = MediaInfo.ProductionYear;
            season = MediaInfo.ParentIndexNumber;
            episode = MediaInfo.IndexNumber;
        }
    }

}