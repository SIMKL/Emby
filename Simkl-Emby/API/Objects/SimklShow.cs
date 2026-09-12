using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class SimklShow
    {
        public string title { get; set; }
        public int? year { get; set; }
        public SimklIds ids { get; set; }

        public SimklShow() { }

        public SimklShow(string seriesTitle, int? seriesYear, Dictionary<string, string> seriesProviderIds)
        {
            title = seriesTitle;
            year = seriesYear;
            ids = new SimklIds(seriesProviderIds);
        }
    }
}
