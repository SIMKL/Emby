using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Objects
{
    public class SimklMovie: MediaObject
    {
        public string title { get; set; }
        public int year { get; set; }
    }
}
