using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmLiteAssistant.Common;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmLiteAssistant.ScheduledTask
{
    public class RefreshEpisodeTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("EpisodeRefresh - Scheduled Task Execute");

            var itemsToRefresh = Plugin.LibraryApi.FetchEpisodeRefreshTaskItems();
            double total = itemsToRefresh.Count;
            var current = 0;

            foreach (var item in itemsToRefresh)
            {
                await QueueManager.Tier2Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    QueueManager.Tier2Semaphore.Release();
                    _logger.Info("EpisodeRefresh - Scheduled Task Cancelled");
                    return;
                }

                try
                {
                    await Plugin.LibraryApi.RefreshEpisodeMetadata(item, cancellationToken).ConfigureAwait(false);
                    QueueManager.EnqueueTheIntroDbRefresh(item, "EpisodeRefresh Task");
                }
                catch (OperationCanceledException)
                {
                    _logger.Info("EpisodeRefresh - Item cancelled: " + item.Name + " - " + item.Path);
                }
                catch (Exception e)
                {
                    _logger.Error("EpisodeRefresh - Item failed: " + item.Name + " - " + item.Path);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
                finally
                {
                    QueueManager.Tier2Semaphore.Release();
                    var currentCount = Interlocked.Increment(ref current);
                    if (total > 0) progress.Report(currentCount / total * 100);
                }
            }

            progress.Report(100.0);
            _logger.Info("EpisodeRefresh - Scheduled Task Complete");
        }

        public string Category => Plugin.Instance.Name;
        public string Key => "EpisodeRefreshTask";
        public string Description => "刷新缺少简介或图片的剧集元数据";
        public string Name => "Refresh Episode";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => true;
    }
}
