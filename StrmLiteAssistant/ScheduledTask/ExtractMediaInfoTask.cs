using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmLiteAssistant.Common;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static StrmLiteAssistant.Options.MediaInfoExtractOptions;

namespace StrmLiteAssistant.ScheduledTask
{
    public class ExtractMediaInfoTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("MediaInfoExtract - Scheduled Task Execute");

            var persistMediaInfoMode = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.PersistMediaInfoMode;
            _logger.Info("Persist MediaInfo Mode: " + persistMediaInfoMode);
            var mediaInfoRestoreMode = persistMediaInfoMode == PersistMediaInfoOption.Restore.ToString();

            var items = Plugin.LibraryApi.FetchPreExtractTaskItems();

            double total = items.Count;
            var current = 0;
            var skip = 0;
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                await QueueManager.MasterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    QueueManager.MasterSemaphore.Release();
                    _logger.Info("MediaInfoExtract - Scheduled Task Cancelled");
                    return;
                }

                var taskItem = item;
                tasks.Add(Task.Run(async () =>
                {
                    bool? result = null;
                    try
                    {
                        result = await Plugin.LibraryApi
                            .OrchestrateMediaInfoProcessAsync(taskItem, "MediaInfoExtract Task", cancellationToken)
                            .ConfigureAwait(false);

                        if (result is null)
                        {
                            Interlocked.Increment(ref skip);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Info($"MediaInfoExtract - Item cancelled: {taskItem.Name} - {taskItem.Path}");
                    }
                    catch (Exception e)
                    {
                        _logger.Error($"MediaInfoExtract - Item failed: {taskItem.Name} - {taskItem.Path}");
                        _logger.Error(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        if (result is true && !mediaInfoRestoreMode &&
                            Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount == 1)
                        {
                            try
                            {
                                await Task.Delay(
                                    Plugin.Instance.GetPluginOptions().GeneralOptions.CooldownDurationSeconds * 1000,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch
                            {
                                // ignored
                            }
                        }

                        QueueManager.MasterSemaphore.Release();
                        var currentCount = Interlocked.Increment(ref current);
                        if (total > 0) progress.Report(currentCount / total * 100);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            progress.Report(100.0);
            _logger.Info($"MediaInfoExtract - Number of items skipped: {skip}");
            _logger.Info("MediaInfoExtract - Scheduled Task Complete");
        }

        public string Category => Plugin.Instance.Name;
        public string Key => "MediaInfoExtractTask";
        public string Description => "提取视频和音频的媒体信息，以及视频截图";
        public string Name => "Extract MediaInfo";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
    }
}
