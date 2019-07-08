using System;
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
            return userConfigs.Where(c => c.guid == guid).First();
        }

        /// <summary>
        /// Deletes an invalid or revoked userToken for all accounts that use it
        /// </summary>
        /// <param name="userToken">The Simkl's user token</param>
        public void deleteUserToken(string userToken)
        {
            foreach (UserConfig config in userConfigs)
            {
                if (config.userToken == userToken)
                    config.userToken = "";
            }
        }
    }
}
