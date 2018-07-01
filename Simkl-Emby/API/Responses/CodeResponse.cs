using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Responses
{
    public class CodeResponse
    {
        public string result { get; set; }
        public string device_code { get; set; }
        public string user_code { get; set; }
        public string verification_url { get; set; }
        public int expires_in { get; set; }
        public int interval { get; set; }
    }
}
