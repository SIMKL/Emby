using MediaBrowser.Model.Dto;

namespace Simkl.Api.Objects
{
    public class SimklMovie: MediaObject
    {
        public string title { get; set; }
        public int? year { get; set; }

        public SimklMovie (BaseItemDto MediaInfo)
        {
            title = MediaInfo.OriginalTitle;
            year = MediaInfo.ProductionYear;
            ids = new Ids(MediaInfo.ProviderIds);
        }
    }
}
