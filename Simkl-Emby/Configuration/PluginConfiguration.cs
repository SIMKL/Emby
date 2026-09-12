using System.Collections.Generic;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;

namespace Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configurate it
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // TODO: Delete this a couple weeks after the plugin has gotten updated, to avoid any erroneous upgrades from happening when they shouldn't, and to avoid accidentally writing new code that touches it.
        public UserConfig[] userConfigs { get; set; }
    }

    public class ConfigurationFactory : IUserConfigurationFactory
    {
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new[]
            {
                new ConfigurationStore
                {
                    ConfigurationType = typeof(UserConfig),
                    Key = ConfigKey
                },
                new ConfigurationStore
                {
                    ConfigurationType = typeof(ImportState),
                    Key = ImportKey
                }
            };
        }

        public const string ConfigKey = "simkl";
        public const string ImportKey = "simklImport";

        public static UserConfig LoadUser(IUserManager users, long embyUserId)
        {
            return (UserConfig)users.GetTypedUserSetting(embyUserId, ConfigKey);
        }
    }
}
