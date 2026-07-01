using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmLiteAssistant.Options.GeneralOptions;
using static StrmLiteAssistant.Options.OptionUtility;

namespace StrmLiteAssistant.Common
{
    public static class QueueManager
    {
        private static readonly ILogger Logger = Plugin.Instance.Logger;
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(30);
        private static readonly Random Random = new Random();
        private static DateTime _mediaInfoProcessLastRunTime = DateTime.MinValue;
        private static DateTime _episodeRefreshProcessLastRunTime = DateTime.MinValue;
        private static int _currentMasterMaxConcurrentCount =
            Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount;
        private static int _currentTier2MaxConcurrentCount =
            Plugin.Instance.GetPluginOptions().GeneralOptions.Tier2MaxConcurrentCount;

        public static CancellationTokenSource MediaInfoTokenSource;
        public static CancellationTokenSource EpisodeRefreshTokenSource;
        public static SemaphoreSlim MasterSemaphore = new SemaphoreSlim(_currentMasterMaxConcurrentCount);
        public static SemaphoreSlim Tier2Semaphore = new SemaphoreSlim(_currentTier2MaxConcurrentCount);
        public static ConcurrentQueue<BaseItem> MediaInfoExtractItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<Episode> EpisodeRefreshItemQueue = new ConcurrentQueue<Episode>();
        public static Task MediaInfoProcessTask;
        public static Task EpisodeRefreshProcessTask;

        public static void Initialize()
        {
            if (MediaInfoProcessTask is null)
            {
                MediaInfoExtractItemQueue.Clear();
                MediaInfoProcessTask = MediaInfoProcessItemQueueAsync().ContinueWith(_ => MediaInfoProcessTask = null);
            }

            if (EpisodeRefreshProcessTask is null)
            {
                EpisodeRefreshItemQueue.Clear();
                EpisodeRefreshProcessTask = EpisodeRefreshProcessItemQueueAsync()
                    .ContinueWith(_ => EpisodeRefreshProcessTask = null);
            }
        }

        public static void UpdateMasterSemaphore(int maxConcurrentCount)
        {
            if (_currentMasterMaxConcurrentCount == maxConcurrentCount) return;
            _currentMasterMaxConcurrentCount = maxConcurrentCount;
            var old = MasterSemaphore;
            MasterSemaphore = new SemaphoreSlim(maxConcurrentCount);
            old.Dispose();
        }

        public static void UpdateTier2Semaphore(int maxConcurrentCount)
        {
            if (_currentTier2MaxConcurrentCount == maxConcurrentCount) return;
            _currentTier2MaxConcurrentCount = maxConcurrentCount;
            var old = Tier2Semaphore;
            Tier2Semaphore = new SemaphoreSlim(maxConcurrentCount);
            old.Dispose();
        }

        private static async Task MediaInfoProcessItemQueueAsync()
        {
            Logger.Info("MediaInfoExtract - Catchup Queue Started");
            MediaInfoTokenSource = new CancellationTokenSource();
            var cancellationToken = MediaInfoTokenSource.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                await DelayUntilNextRun(_mediaInfoProcessLastRunTime, cancellationToken).ConfigureAwait(false);

                if (!MediaInfoExtractItemQueue.IsEmpty && IsCatchupTaskSelected(CatchupTask.MediaInfo))
                {
                    var dequeueItems = new List<BaseItem>();
                    while (MediaInfoExtractItemQueue.TryDequeue(out var item)) dequeueItems.Add(item);

                    var mediaInfoItems = Plugin.LibraryApi.FetchExtractQueueItems(
                        dequeueItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList());

                    await ProcessMediaInfoItems(mediaInfoItems, "MediaInfoExtract Catchup", cancellationToken)
                        .ConfigureAwait(false);
                }

                _mediaInfoProcessLastRunTime = DateTime.UtcNow;
            }

            Logger.Info("MediaInfoExtract - Catchup Queue Stopped");
        }

        private static async Task EpisodeRefreshProcessItemQueueAsync()
        {
            Logger.Info("EpisodeRefresh - Catchup Queue Started");
            EpisodeRefreshTokenSource = new CancellationTokenSource();
            var cancellationToken = EpisodeRefreshTokenSource.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                await DelayUntilNextRun(_episodeRefreshProcessLastRunTime, cancellationToken).ConfigureAwait(false);

                if (!EpisodeRefreshItemQueue.IsEmpty && IsCatchupTaskSelected(CatchupTask.EpisodeRefresh))
                {
                    var dequeueItems = new List<Episode>();
                    while (EpisodeRefreshItemQueue.TryDequeue(out var item)) dequeueItems.Add(item);

                    var itemsToRefresh = Plugin.LibraryApi.FetchEpisodeRefreshQueueItems(dequeueItems);
                    await ProcessEpisodeItems(itemsToRefresh, cancellationToken).ConfigureAwait(false);
                }

                _episodeRefreshProcessLastRunTime = DateTime.UtcNow;
            }

            Logger.Info("EpisodeRefresh - Catchup Queue Stopped");
        }

        private static async Task DelayUntilNextRun(DateTime lastRunTime, CancellationToken cancellationToken)
        {
            var remainingTime = ThrottleInterval - (DateTime.UtcNow - lastRunTime);
            if (remainingTime > TimeSpan.Zero)
            {
                try { await Task.Delay(remainingTime, cancellationToken).ConfigureAwait(false); }
                catch { }
            }
        }

        public static async Task ProcessMediaInfoItems(IEnumerable<BaseItem> items, string source,
            CancellationToken cancellationToken)
        {
            var maxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount;
            var cooldownSeconds = maxConcurrentCount == 1
                ? Plugin.Instance.GetPluginOptions().GeneralOptions.CooldownDurationSeconds
                : (int?)null;
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                await MasterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                var taskItem = item;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await Plugin.LibraryApi
                            .OrchestrateMediaInfoProcessAsync(taskItem, source, cancellationToken)
                            .ConfigureAwait(false);

                        if (result is true && cooldownSeconds.HasValue)
                            await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Info("MediaInfoExtract - Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch (Exception e)
                    {
                        Logger.Error("MediaInfoExtract - Item failed: " + taskItem.Name + " - " + taskItem.Path);
                        Logger.Error(e.Message);
                        Logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        MasterSemaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public static async Task ProcessEpisodeItems(IEnumerable<Episode> items, CancellationToken cancellationToken)
        {
            var tier2MaxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions.Tier2MaxConcurrentCount;
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                await Tier2Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                var taskItem = item;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(Random.Next(0,
                                Math.Max(0, tier2MaxConcurrentCount - Tier2Semaphore.CurrentCount) *
                                MetadataApi.RequestIntervalMs), cancellationToken)
                            .ConfigureAwait(false);
                        await Plugin.LibraryApi.RefreshEpisodeMetadata(taskItem, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Info("EpisodeRefresh - Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch (Exception e)
                    {
                        Logger.Error("EpisodeRefresh - Item failed: " + taskItem.Name + " - " + taskItem.Path);
                        Logger.Error(e.Message);
                        Logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        Tier2Semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public static void Dispose()
        {
            MediaInfoTokenSource?.Cancel();
            EpisodeRefreshTokenSource?.Cancel();
        }
    }
}
