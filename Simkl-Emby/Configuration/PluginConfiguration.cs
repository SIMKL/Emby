using System;
using MediaBrowser.Model.Plugins;

namespace Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configurate it
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public UserConfig[] UserConfigs { get; set; }

        public PluginConfiguration()
        {
            UserConfigs = new UserConfig[] { };
        }

        public int searchByGuid(Guid guid)
        {
            int index = -1;
            for (int i = 0; index == -1 && i < UserConfigs.Length; i++)
            {
                if (guid == UserConfigs[i].id) index = i;
            }

            return index;
        }

        public UserConfig getByGuid(Guid guid)
        {
            int i = searchByGuid(guid);
            return (i == -1)?new UserConfig():UserConfigs[i];
        }

        /// <summary>
        /// Deletes an invalid or revoked userToken for all accounts that use it
        /// </summary>
        /// <param name="userToken">The Simkl's user token</param>
        public void deleteUserToken(string userToken)
        {
            foreach (UserConfig config in UserConfigs)
            {
                if (config.userToken == userToken)
                    config.userToken = "";
            }
        }
    }
}
