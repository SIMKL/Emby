using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

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

        // The login is server-owned end to end: the device flow, the renewal and the logout write these token fields, the
        // settings page never does. Emby keeps each user's config as its own typed setting, so there is no central array to
        // lock the way the Jellyfin plugin does; this lock serialises the read-modify-write across the few server-side writers.
        private static readonly object SaveLock = new object();

        public static UserConfig LoadUser(IUserManager users, long embyUserId)
        {
            return (UserConfig)users.GetTypedUserSetting(embyUserId, ConfigKey);
        }

        // Records a completed device login, replacing whatever was there; clears the reconnect-prompt stamp so a fresh
        // login is never immediately nudged.
        public static void StoreLogin(IUserManager users, long embyUserId, string access, string refresh, DateTime expiresAt, string scope)
        {
            lock (SaveLock)
            {
                var config = LoadUser(users, embyUserId) ?? new UserConfig();
                config.userToken = access;
                config.refreshToken = refresh ?? "";
                config.tokenExpiresAt = expiresAt;
                config.tokenScope = scope ?? "";
                config.lastReloginPromptAt = default(DateTime);
                users.SetTypedUserSetting(embyUserId, ConfigKey, config);
            }
        }

        // Writes a renewed token over whoever still holds the old one. Matching by the old access value means a logout or a
        // second login that already replaced it wins the race: this write then touches nobody and the renewed token is dropped.
        public static void ReplaceToken(IUserManager users, string oldAccess, string newAccess, string refresh, DateTime expiresAt, string scope)
        {
            if (string.IsNullOrEmpty(oldAccess))
            {
                return;
            }

            lock (SaveLock)
            {
                foreach (var user in users.GetUserList(new UserQuery()))
                {
                    var config = LoadUser(users, user.InternalId);
                    if (config == null || config.userToken != oldAccess)
                    {
                        continue;
                    }

                    config.userToken = newAccess;
                    config.refreshToken = refresh ?? "";
                    config.tokenExpiresAt = expiresAt;
                    config.tokenScope = scope ?? "";
                    users.SetTypedUserSetting(user.InternalId, ConfigKey, config);
                }
            }
        }

        public static void LogOut(IUserManager users, long embyUserId)
        {
            lock (SaveLock)
            {
                var config = LoadUser(users, embyUserId);
                if (config == null)
                {
                    return;
                }

                config.ClearTokens();
                users.SetTypedUserSetting(embyUserId, ConfigKey, config);
            }
        }

        // Claims the once-per-interval reconnect prompt for a legacy user; true for exactly one caller, so two sessions
        // starting at once do not both toast. Requires the stored token to still be the one the caller saw.
        public static bool MarkReloginPrompted(IUserManager users, long embyUserId, string userToken, TimeSpan interval)
        {
            lock (SaveLock)
            {
                var config = LoadUser(users, embyUserId);
                if (config == null || config.userToken != userToken)
                {
                    return false;
                }

                if (config.lastReloginPromptAt != default(DateTime) && DateTime.UtcNow - config.lastReloginPromptAt < interval)
                {
                    return false;
                }

                config.lastReloginPromptAt = DateTime.UtcNow;
                users.SetTypedUserSetting(embyUserId, ConfigKey, config);
                return true;
            }
        }
    }
}
