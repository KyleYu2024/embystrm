using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
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
    public class PersistMediaInfoTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;

        public PersistMediaInfoTask(IFileSystem fileSystem)
        {
            _logger = Plugin.Instance.Logger;
            _fileSystem = fileSystem;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("MediaInfoPersist - Scheduled Task Execute");

            if (Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.PersistMediaInfoMode ==
                PersistMediaInfoOption.None.ToString())
            {
                _logger.Info("MediaInfoPersist - Persist mode is disabled, task skipped.");
                progress.Report(100.0);
                return;
            }

            var items = Plugin.LibraryApi.FetchPostExtractTaskItems(false);
            var directoryService = new DirectoryService(_logger, _fileSystem);

            double total = items.Count;
            var current = 0;
            var skip = 0;
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                await QueueManager.Tier2Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    QueueManager.Tier2Semaphore.Release();
                    _logger.Info("MediaInfoPersist - Scheduled Task Cancelled");
                    return;
                }

                var taskItem = item;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await Plugin.MediaInfoApi.SerializeMediaInfo(taskItem.InternalId,
                            directoryService, false, "Persist MediaInfo Task").ConfigureAwait(false);
                        if (!result) Interlocked.Increment(ref skip);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Info($"MediaInfoPersist - Item cancelled: {taskItem.Name} - {taskItem.Path}");
                    }
                    catch (Exception e)
                    {
                        _logger.Error($"MediaInfoPersist - Item failed: {taskItem.Name} - {taskItem.Path}");
                        _logger.Error(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        QueueManager.Tier2Semaphore.Release();
                        var currentCount = Interlocked.Increment(ref current);
                        if (total > 0) progress.Report(currentCount / total * 100);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            progress.Report(100.0);
            _logger.Info($"MediaInfoPersist - Number of items skipped: {skip}");
            _logger.Info("MediaInfoPersist - Scheduled Task Complete");
        }

        public string Category => Plugin.Instance.Name;
        public string Key => "MediaInfoPersistTask";
        public string Description => "导出媒体信息，章节片头片尾标记至 JSON 文件";
        public string Name => "Persist MediaInfo";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
    }
}
