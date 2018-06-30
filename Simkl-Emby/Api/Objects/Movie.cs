using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Objects
{
    class Movie
    {
        public string title { get; set; }
        public int year { get; set; }
        public Dictionary<string, string> ids { get; set; }
    }
}
