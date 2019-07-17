using MediaBrowser.Model.Notifications;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Entities;

using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities.Movies;

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

        public static NotificationRequest GetNotificationRequest(BaseItem item, long userId) {
            NotificationRequest nr = new NotificationRequest {
                Date = DateTime.UtcNow,
                UserIds = new long[] {userId},
                SendToUserMode = SendToUserType.Custom
            };

            // TODO: Set url parameter to simkl's movie url
            if (item is Movie) {
                int ? year = item.ProductionYear;

                nr.NotificationType = NOTIFICATION_MOVIE_TYPE;
                nr.Name = "Movie Scrobbled to Simkl";
                nr.Description = "The movie " + item.Name;
                if (year != null) nr.Description += " (" + year + ")";
                nr.Description += " has been scrobbled to your account";
            }

            return nr;
        }
    }
}