using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Objects
{
    public class SimklHistory
    {
        public SimklMovie[] movies { get; set; }
        public SimklShow[] shows { get; set; }
        public SimklEpisode[] episodes { get; set; }
    }
}
