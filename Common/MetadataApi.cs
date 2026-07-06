using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace StrmLiteAssistant.Common
{
    public class MetadataApi
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;

        public const int RequestIntervalMs = 100;

        public MetadataApi(IFileSystem fileSystem)
        {
            _logger = Plugin.Instance.Logger;
            _fileSystem = fileSystem;
        }

        public MetadataRefreshOptions GetMetadataFullRefreshOptions()
        {
            return new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableRemoteContentProbe = false,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllImages = true,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false
            };
        }
    }
}
