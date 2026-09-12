using MediaBrowser.Model.Dto;

namespace Simkl.Api.Objects
{
    public class SimklMovie
    {
        public string title { get; set; }
        public int? year { get; set; }
        public SimklIds ids { get; set; }

        public SimklMovie() { }

        public SimklMovie(BaseItemDto item)
        {
            title = item.OriginalTitle ?? item.Name;
            year = item.ProductionYear;
            ids = new SimklIds(item.ProviderIds);
        }
    }
}
