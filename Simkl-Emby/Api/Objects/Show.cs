using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Objects
{
    class Show
    {
        public string title { get; set; }
        public int year { get; set; }
        public Dictionary<string, string> ids { get; set; }
        public Season[] seasons { get; set; }
    }

    struct episode
    {
        int number;
    }

    class Season
    {
        public int number { get; set; }
        public episode[] episodes { get; set; }
    }
}
