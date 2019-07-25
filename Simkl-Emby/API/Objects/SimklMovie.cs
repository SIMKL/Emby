using System;
using MediaBrowser.Model.Dto;

namespace Simkl.Api.Objects
{
    public class SimklMovie: SimklMediaObject
    {
        public string title { get; set; }
        public int? year { get; set; }
        public override SimklIds ids { get; set; }
        public string watched_at { get; }

        public SimklMovie (BaseItemDto item)
        {
            title = item.OriginalTitle;
            year = item.ProductionYear;
            ids = new SimklMovieIds(item.ProviderIds);
            watched_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH\\:mm\\:ss");
        }
    }
}
