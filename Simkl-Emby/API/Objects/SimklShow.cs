using MediaBrowser.Model.Dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Objects
{
    public class SimklShow: MediaObject
    {
        public string title { get; set; }
        public int? year { get; set; }
        public Season[] seasons { get; set; }
        public override SimklIds ids { get; set; }

        public SimklShow(BaseItemDto MediaInfo)
        {
            title = MediaInfo.SeriesName;
            ids = new SimklShowIds(MediaInfo.ProviderIds);
            year = MediaInfo.ProductionYear;
            seasons = new Season[] {
                new Season
                {
                    number = MediaInfo.ParentIndexNumber,
                    episodes = new ShowEpisode[]
                    {
                        new ShowEpisode { number = MediaInfo.IndexNumber }
                    }
                }
            };
        }
    }

    public class ShowEpisode
    {
        public int? number { get; set; }
        // TODO: watched_at
    }

    public class Season
    {
        public int? number { get; set; }
        public ShowEpisode[] episodes { get; set; }
    }
}
