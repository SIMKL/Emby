using System;
using System.Collections.Generic;
using System.Text;

namespace Simkl.Api.Responses
{
    public class CodeStatusResponse
    {
        public string result { get; set; }
        public string message { get; set; }
        public string access_token { get; set; }
    }
}
