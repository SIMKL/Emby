using System;
using MediaBrowser.Model.Dto;

namespace Simkl.Api.Objects
{
    public class SimklMovie: MediaObject
    {
        public string title { get; set; }
        public int? year { get; set; }
        public override SimklIds ids { get; set; }
        public string watched_at { get; }

        public SimklMovie (BaseItemDto MediaInfo)
        {
            title = MediaInfo.OriginalTitle;
            year = MediaInfo.ProductionYear;
            ids = new SimklMovieIds(MediaInfo.ProviderIds);
            watched_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH\\:mm\\:ss");
        }
    }
}
