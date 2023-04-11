/*using MediaBrowser.Model.Notifications;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Entities;

using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dto;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Simkl.Services {
    public class SimklNotificationsFactory : INotificationTypeFactory {
        public const string NOTIFICATION_CATEGORY = "Simkl Scrobbling";
        public const string NOTIFICATION_MOVIE_TYPE = "SimklScrobblingMovie";
        public const string NOTIFICATION_SHOW_TYPE = "SimklScrobblingShow";

        public IEnumerable<NotificationTypeInfo> GetNotificationTypes() {
            return new List<NotificationTypeInfo> {
                new NotificationTypeInfo {
                    Type = NOTIFICATION_MOVIE_TYPE,
                    Name = "Scrobbling Movie",
                    Category = NOTIFICATION_CATEGORY,
                    Enabled = true,
                    IsBasedOnUserEvent = false
                },
                new NotificationTypeInfo {
                    Type = NOTIFICATION_SHOW_TYPE,
                    Name = "Scrobbling TV Show",
                    Category = NOTIFICATION_CATEGORY,
                    Enabled = true,
                    IsBasedOnUserEvent = false
                }
            };
        }

        public static NotificationRequest GetNotificationRequest(BaseItemDto item, long userId) {
            NotificationRequest nr = new NotificationRequest {
                Date = DateTime.UtcNow,
                UserIds = new long[] {userId},
                SendToUserMode = SendToUserType.Custom
            };

            // TODO: Set url parameter to simkl's movie url
            if (item.IsMovie == true || item.Type == "Movie") {
                nr.NotificationType = NOTIFICATION_MOVIE_TYPE;
                nr.Name = "Movie Scrobbled to Simkl";
                nr.Description = "The movie " + item.Name;
                nr.Description += " has been scrobbled to your account";
            }

            if (item.IsSeries == true || item.Type == "Episode") {
                nr.NotificationType = NOTIFICATION_SHOW_TYPE;
                nr.Name = "Episode Scrobbled to Simkl";
                nr.Description = item.SeriesName;
                nr.Description += " S" + item.ParentIndexNumber.ToString() + ":E" + item.IndexNumber;
                nr.Description += " - " + item.Name;
                nr.Description += " has been scrobbled to your account";
            }

            return nr;
        }
    }
}*/