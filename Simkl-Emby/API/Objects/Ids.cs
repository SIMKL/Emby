using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Simkl.Api.Objects
{
    public class Ids
    {
        public int? simkl { get; set; }
        public int? tvdb { get; set; }
        public string imdb { get; set; }
        public int? mal { get; set; }
        public int? anidb { get; set; }
        public int? hulu { get; set; }
        public int? crunchyroll { get; set; }
        public int? tmdb { get; set; }

        /// <summary>
        /// Creates an Ids object given a dictionary
        /// </summary>
        public Ids (Dictionary<string, string> ProviderIds)
        {
            foreach (KeyValuePair<string, string> id in ProviderIds)
            {
                PropertyInfo prop = GetType().GetProperty(id.Key.ToLower());
                if (prop.PropertyType == typeof(int?))
                {
                    prop.SetValue(this, int.Parse(id.Value));
                } else if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(this, id.Value);
                }
            }
        }
    }
}
