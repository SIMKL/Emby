using MediaBrowser.Model.Notifications;
using MediaBrowser.Controller.Notifications;

using System.Collections.Generic;

namespace Simkl.Services {
    public class SimklNotificationsFactory : INotificationTypeFactory {
        public const string NOTIFICATION_MOVIE_TYPE = "SimklScrobblingMovie";

        public IEnumerable<NotificationTypeInfo> GetNotificationTypes() {
            return new List<NotificationTypeInfo> {
                new NotificationTypeInfo {
                    Type = NOTIFICATION_MOVIE_TYPE,
                    Name = "Simkl Scrobbling Movie",
                    Category = "Simkl Scrobbling",
                    Enabled = true,
                    IsBasedOnUserEvent = false
                }
            };
        }
    }
}