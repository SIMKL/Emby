using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Objects
{
    public class SimklShow
    {
        public string title { get; set; }
        public int year { get; set; }
        public Dictionary<string, string> ids { get; set; }
        public Season[] seasons { get; set; }
    }

    public class ShowEpisode: MediaObject
    {
        public int number { get; set; }
    }

    public class Season: MediaObject
    {
        public int number { get; set; }
        public ShowEpisode[] episodes { get; set; }
    }
}
