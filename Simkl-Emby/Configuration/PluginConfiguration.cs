using System;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;
using System.Linq;

namespace Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configurate it
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public UserConfig[] userConfigs { get; set; }

        public PluginConfiguration() {
            userConfigs = new UserConfig[]{};
        }

        public UserConfig getByGuid(string guid)
        {
            return userConfigs.Where(c => c.guid == guid).FirstOrDefault();
        }
    }
}
