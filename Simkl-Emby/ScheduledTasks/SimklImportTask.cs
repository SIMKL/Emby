using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;

using Simkl.Configuration;
using Simkl.Services;

namespace Simkl.ScheduledTasks
{
    // Dashboard > Scheduled Tasks > "Import Simkl watched history"; runs for users who switched on automatic import
    public class SimklImportTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly HistoryImporter _importer;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public SimklImportTask(HistoryImporter importer, IUserManager userManager, ILogManager logManager)
        {
            _importer = importer;
            _userManager = userManager;
            _logger = logManager.GetLogger("Simkl Import");
        }

        public string Name => "Import Simkl watched history";
        public string Description => "Checks Simkl for users who enabled automatic import on their Simkl page and marks what they watched as played. Runs hourly; each user picks how often their account is checked.";
        public string Category => "Simkl";
        public string Key => "SimklHistoryImport";
        public bool IsEnabled => true;
        public bool IsHidden => false;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[] { new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(1).Ticks } };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var users = _userManager.GetUserList(new UserQuery { IsDisabled = false });
            for (var i = 0; i < users.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var user = users[i];
                var config = ConfigurationFactory.LoadUser(_userManager, user.InternalId);
                if (config != null && config.autoImport)
                {
                    try
                    {
                        await _importer.RunScheduled(user, config, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Automatic import for " + user.Name + " failed", ex);
                    }
                }
                progress.Report((i + 1) * 100.0 / users.Length);
            }
        }
    }
}
