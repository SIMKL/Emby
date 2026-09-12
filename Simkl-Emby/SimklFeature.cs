using System.Collections.Generic;
using Emby.Features;

namespace Simkl
{
    /// <summary>
    /// This registers a feature in user permissions for each user, so that admins can restrict use of it
    /// </summary>
    public class SimklFeature : IFeatureFactory
    {
        public List<FeatureInfo> GetFeatureInfos(string language)
        {
            return new List<FeatureInfo>
            {
                new FeatureInfo
                {
                    Id = Plugin.StaticId,
                    Name = Plugin.StaticName,
                    FeatureType = FeatureType.User
                }
            };
        }
    }
}
