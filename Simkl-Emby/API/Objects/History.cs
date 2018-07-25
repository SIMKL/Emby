using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    public class SimklHistory
    {
        public List<SimklMovie> movies { get; set; }
        public List<SimklShow> shows { get; set; }
        public List<SimklEpisode> episodes { get; set; }

        public SimklHistory()
        {
            movies = new List<SimklMovie>();
            shows = new List<SimklShow>();
            episodes = new List<SimklEpisode>();
        }
    }
}
