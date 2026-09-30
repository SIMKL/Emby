using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Simkl.Services
{
    // A device login is started on one request and polled on the next, but Emby builds a new endpoint instance per
    // request, so the pending logins live in a static map keyed by the Emby user's internal id, the way the importer
    // keeps its run state.
    public class PendingLogin
    {
        public string DeviceCode { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public int Interval { get; set; }
        public int Failures { get; set; }
    }

    public static class DeviceLoginStore
    {
        private static readonly ConcurrentDictionary<long, PendingLogin> Pending = new ConcurrentDictionary<long, PendingLogin>();

        public static PendingLogin Start(long userId, string deviceCode, DateTime expiresUtc, int interval)
        {
            var login = new PendingLogin { DeviceCode = deviceCode, ExpiresUtc = expiresUtc, Interval = interval };
            Pending[userId] = login;
            return login;
        }

        public static PendingLogin Get(long userId)
        {
            return Pending.TryGetValue(userId, out var login) ? login : null;
        }

        // Drops this attempt only: a stale poll cannot cancel a login the user has since restarted. With no attempt it
        // drops whatever is there (an explicit Cancel).
        public static bool Cancel(long userId, PendingLogin attempt = null)
        {
            if (attempt == null)
            {
                return Pending.TryRemove(userId, out _);
            }

            return ((ICollection<KeyValuePair<long, PendingLogin>>)Pending)
                .Remove(new KeyValuePair<long, PendingLogin>(userId, attempt));
        }
    }
}
