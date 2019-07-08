using System;
using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configurate it
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public Dictionary<string, UserConfig> userConfigs { get; set; }

        public PluginConfiguration() {}

        public UserConfig getByGuid(string guid)
        {
            return userConfigs[guid];
        }

        /// <summary>
        /// Deletes an invalid or revoked userToken for all accounts that use it
        /// </summary>
        /// <param name="userToken">The Simkl's user token</param>
        public void deleteUserToken(string userToken)
        {
            foreach (KeyValuePair<string,UserConfig> config in userConfigs)
            {
                if (config.Value.userToken == userToken)
                    config.Value.userToken = "";
            }
        }
    }
}
