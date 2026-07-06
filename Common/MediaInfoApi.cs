using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmLiteAssistant.Common
{
    public class MediaInfoApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IItemRepository _itemRepository;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly IProviderManager _providerManager;

        private const string MediaInfoFileExtension = "-mediainfo.json";

        private readonly MethodInfo _getStaticMediaSources;

        internal class MediaSourceWithChapters
        {
            public MediaSourceInfo MediaSourceInfo { get; set; }
            public List<ChapterInfo> Chapters { get; set; } = new List<ChapterInfo>();
            public string EmbeddedImage { get; set; }
        }

        public MediaInfoApi(ILibraryManager libraryManager, IFileSystem fileSystem, IProviderManager providerManager,
            IMediaSourceManager mediaSourceManager, IItemRepository itemRepository, IJsonSerializer jsonSerializer,
            ILibraryMonitor libraryMonitor)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _providerManager = providerManager;
            _mediaSourceManager = mediaSourceManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;

            try
            {
                _getStaticMediaSources = ResolveGetStaticMediaSources(mediaSourceManager.GetType());
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            if (_getStaticMediaSources is null)
            {
                _logger.Warn($"{nameof(MediaInfoApi)} - GetStaticMediaSources Init Failed");
            }

            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var libraryMonitorImpl =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.IO.LibraryMonitor");
                var alwaysIgnoreExtensions = libraryMonitorImpl.GetField("_alwaysIgnoreExtensions",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var currentArray = (string[])alwaysIgnoreExtensions.GetValue(libraryMonitor);
                var newArray = new string[currentArray.Length + 1];
                Array.Copy(currentArray, newArray, currentArray.Length);
                newArray[newArray.Length - 1] = ".json";
                alwaysIgnoreExtensions.SetValue(libraryMonitor, newArray);
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }

                _logger.Warn($"{nameof(MediaInfoApi)} - LibraryMonitor Init Failed");
            }
        }

        private static MethodInfo ResolveGetStaticMediaSources(Type mediaSourceManagerType)
        {
            var signatures = new[]
            {
                new[]
                {
                    typeof(BaseItem), typeof(bool), typeof(bool), typeof(bool), typeof(LibraryOptions),
                    typeof(DeviceProfile), typeof(User)
                },
                new[]
                {
                    typeof(BaseItem), typeof(bool), typeof(bool), typeof(LibraryOptions), typeof(DeviceProfile),
                    typeof(User)
                },
                new[] { typeof(BaseItem), typeof(bool), typeof(bool), typeof(DeviceProfile), typeof(User) },
                new[] { typeof(BaseItem), typeof(bool), typeof(DeviceProfile), typeof(User) }
            };

            foreach (var signature in signatures)
            {
                var method = mediaSourceManagerType.GetMethod("GetStaticMediaSources",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, signature, null);
                if (method != null) return method;
            }

            return null;
        }

        private List<MediaSourceInfo> GetStaticMediaSourcesByRef(BaseItem item, bool enableAlternateMediaSources,
            LibraryOptions libraryOptions)
        {
            var parameters = _getStaticMediaSources.GetParameters();
            object[] arguments;

            switch (parameters.Length)
            {
                case 7:
                    arguments = new object[] { item, enableAlternateMediaSources, false, false, libraryOptions, null, null };
                    break;
                case 6:
                    arguments = new object[] { item, enableAlternateMediaSources, false, libraryOptions, null, null };
                    break;
                case 5:
                    arguments = new object[] { item, enableAlternateMediaSources, false, null, null };
                    break;
                case 4:
                    arguments = new object[] { item, enableAlternateMediaSources, null, null };
                    break;
                default:
                    throw new MissingMethodException(_mediaSourceManager.GetType().FullName, "GetStaticMediaSources");
            }

            return ((IEnumerable<MediaSourceInfo>)_getStaticMediaSources.Invoke(_mediaSourceManager, arguments))
                .ToList();
        }

        public List<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enableAlternateMediaSources)
        {
            var options = _libraryManager.GetLibraryOptions(item);

            if (_getStaticMediaSources is null)
            {
                return item.GetMediaSources(false, false, options);
            }

            return GetStaticMediaSourcesByRef(item, enableAlternateMediaSources, options);
        }

        public MetadataRefreshOptions GetMediaInfoRefreshOptions()
        {
            return new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false
            };
        }

        public static string GetMediaInfoJsonPath(BaseItem item)
        {
            var jsonRootFolder = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MediaInfoJsonRootFolder;

            var relativePath = item.ContainingFolderPath;
            if (!string.IsNullOrEmpty(jsonRootFolder) && Path.IsPathRooted(item.ContainingFolderPath))
            {
                relativePath = Path.GetRelativePath(Path.GetPathRoot(item.ContainingFolderPath)!,
                    item.ContainingFolderPath);
            }

            var mediaInfoJsonPath = !string.IsNullOrEmpty(jsonRootFolder)
                ? Path.Combine(jsonRootFolder, relativePath, item.FileNameWithoutExtension + MediaInfoFileExtension)
                : Path.Combine(item.ContainingFolderPath!, item.FileNameWithoutExtension + MediaInfoFileExtension);

            return mediaInfoJsonPath;
        }

        private async Task<bool> SerializeMediaInfo(BaseItem item, IDirectoryService directoryService, bool overwrite,
            string source)
        {
            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (overwrite || file?.Exists != true)
            {
                try
                {
                    var options = _libraryManager.GetLibraryOptions(item);
                    var mediaSources = item.GetMediaSources(false, false, options);
                    var chapters = _itemRepository.GetChapters(item);
                    var mediaSourcesWithChapters = mediaSources.Select(mediaSource =>
                            new MediaSourceWithChapters
                                { MediaSourceInfo = mediaSource, Chapters = chapters })
                        .ToList();

                    foreach (var jsonItem in mediaSourcesWithChapters)
                    {
                        jsonItem.MediaSourceInfo.Id = null;
                        jsonItem.MediaSourceInfo.ItemId = null;
                        jsonItem.MediaSourceInfo.Path = null;

                        foreach (var subtitle in jsonItem.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle &&
                                     m.Protocol == MediaProtocol.File))
                        {
                            subtitle.Path = _fileSystem.GetFileInfo(subtitle.Path).Name;
                        }

                        foreach (var chapter in jsonItem.Chapters)
                        {
                            chapter.ImageTag = null;
                        }

                        if (item is Audio)
                        {
                            var primaryImageInfo = item.GetImageInfo(ImageType.Primary, 0);
                            if (primaryImageInfo != null && _fileSystem.FileExists(primaryImageInfo.Path))
                            {
                                var imageBytes = await _fileSystem
                                    .ReadAllBytesAsync(primaryImageInfo.Path, CancellationToken.None)
                                    .ConfigureAwait(false);
                                var base64String = Convert.ToBase64String(imageBytes);
                                jsonItem.EmbeddedImage = base64String;
                            }
                        }
                    }

                    var parentDirectory = Path.GetDirectoryName(mediaInfoJsonPath);
                    if (!string.IsNullOrEmpty(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    _jsonSerializer.SerializeToFile(mediaSourcesWithChapters, mediaInfoJsonPath);

                    _logger.Info("MediaInfoPersist - Serialization Success (" + source + "): " + mediaInfoJsonPath);

                    return true;
                }
                catch (Exception e)
                {
                    _logger.Error("MediaInfoPersist - Serialization Failed (" + source + "): " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        public async Task<bool> SerializeMediaInfo(long itemId, IDirectoryService directoryService, bool overwrite,
            string source)
        {
            var workItem = _libraryManager.GetItemById(itemId);

            if (!Plugin.LibraryApi.HasMediaInfo(workItem))
            {
                _logger.Info("MediaInfoPersist - Serialization Skipped - No MediaInfo (" + source + ")");
                return false;
            }

            if (!Plugin.LibraryApi.IsLibraryInScope(workItem)) return false;

            var ds = directoryService ?? new DirectoryService(_logger, _fileSystem);

            return await SerializeMediaInfo(workItem, ds, overwrite, source).ConfigureAwait(false);
        }

        public async Task<bool> DeserializeMediaInfo(BaseItem item, IDirectoryService directoryService, string source,
            bool ignoreFileChange)
        {
            var workItem = _libraryManager.GetItemById(item.InternalId);

            if (Plugin.LibraryApi.HasMediaInfo(workItem)) return true;

            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true)
            {
                try
                {
                    var mediaSourceWithChapters =
                        (await _jsonSerializer
                            .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                            .ConfigureAwait(false)).ToArray()[0];

                    if (mediaSourceWithChapters?.MediaSourceInfo?.RunTimeTicks.HasValue is true &&
                        (ignoreFileChange || !Plugin.LibraryApi.HasFileChanged(item, directoryService)))
                    {
                        foreach (var subtitle in mediaSourceWithChapters.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle &&
                                     m.Protocol == MediaProtocol.File))
                        {
                            subtitle.Path = Path.Combine(workItem.ContainingFolderPath,
                                _fileSystem.GetFileInfo(subtitle.Path).Name);
                        }

                        _itemRepository.SaveMediaStreams(item.InternalId,
                            mediaSourceWithChapters.MediaSourceInfo.MediaStreams, CancellationToken.None);

                        if (workItem is Audio && !string.IsNullOrEmpty(mediaSourceWithChapters.EmbeddedImage))
                        {
                            var imageBytes = Convert.FromBase64String(mediaSourceWithChapters.EmbeddedImage);
                            var tempPath = Path.Combine(Plugin.Instance.ApplicationPaths.TempDirectory,
                                Guid.NewGuid() + ".jpg");
                            await _fileSystem.WriteAllBytesAsync(tempPath, imageBytes, CancellationToken.None)
                                .ConfigureAwait(false);

                            var libraryOptions = _libraryManager.GetLibraryOptions(workItem);
                            await _providerManager.SaveImage(workItem, libraryOptions, tempPath, ImageType.Primary,
                                    null, Array.Empty<long>(), directoryService, false, CancellationToken.None)
                                .ConfigureAwait(false);
                        }

                        workItem.Size = mediaSourceWithChapters.MediaSourceInfo.Size.GetValueOrDefault();
                        workItem.RunTimeTicks = mediaSourceWithChapters.MediaSourceInfo.RunTimeTicks;
                        workItem.Container = mediaSourceWithChapters.MediaSourceInfo.Container;
                        workItem.TotalBitrate = mediaSourceWithChapters.MediaSourceInfo.Bitrate.GetValueOrDefault();

                        var videoStream = mediaSourceWithChapters.MediaSourceInfo.MediaStreams
                            .Where(s => s.Type == MediaStreamType.Video && s.Width.HasValue && s.Height.HasValue)
                            .OrderByDescending(s => (long)s.Width.Value * s.Height.Value)
                            .FirstOrDefault();

                        if (videoStream != null)
                        {
                            workItem.Width = videoStream.Width.GetValueOrDefault();
                            workItem.Height = videoStream.Height.GetValueOrDefault();
                        }

                        _libraryManager.UpdateItems(new List<BaseItem> { workItem }, null,
                            ItemUpdateType.MetadataImport, false, false, null, CancellationToken.None);

                        if (workItem is Video video)
                        {
                            await DeserializeChapterInfo(video, mediaSourceWithChapters.Chapters, directoryService,
                                source).ConfigureAwait(false);

                        }

                        _logger.Info("MediaInfoPersist - Deserialization Success (" + source + "): " + mediaInfoJsonPath);

                        return true;
                    }

                    _logger.Info("MediaInfoPersist - Deserialization Skipped (" + source + "): " + mediaInfoJsonPath);
                }
                catch (Exception e)
                {
                    _logger.Error("MediaInfoPersist - Deserialization Failed (" + source + "): " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        public void DeleteMediaInfoJson(BaseItem item, IDirectoryService directoryService, string source)
        {
            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);

            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists is true)
            {
                try
                {
                    _logger.Info($"MediaInfoPersist - Attempting to delete ({source}): {mediaInfoJsonPath}");
                    _fileSystem.DeleteFile(mediaInfoJsonPath);

                    var jsonRoot = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MediaInfoJsonRootFolder;

                    if (!string.IsNullOrWhiteSpace(jsonRoot))
                    {
                        jsonRoot = _fileSystem.GetFullPath(jsonRoot).TrimEnd(Path.DirectorySeparatorChar);

                        var currentDir =
                            _fileSystem.GetFullPath(_fileSystem.GetDirectoryName(mediaInfoJsonPath) ?? string.Empty);

                        while (!string.IsNullOrEmpty(currentDir) &&
                               !string.Equals(currentDir, jsonRoot, StringComparison.OrdinalIgnoreCase) &&
                               CommonUtility.IsDirectoryEmpty(currentDir))
                        {
                            _logger.Info(
                                $"MediaInfoPersist - Attempting to delete empty folder ({source}): {currentDir}");
                            _fileSystem.DeleteDirectory(currentDir, false);
                            currentDir = Path.GetDirectoryName(currentDir);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
        }

        public async Task<bool> DeserializeChapterInfo(Video item, List<ChapterInfo> chapters,
            IDirectoryService directoryService, string source)
        {
            var thumbnailResult = false;

            if (!item.IsShortcut)
            {
                var localThumbnailSets =
                    Video.GetLocalThumbnailSetInfos(item.Path, item.Id, false, directoryService);

                if (localThumbnailSets.Length > 0)
                {
                    var options = _libraryManager.GetLibraryOptions(item);
                    var dummyLibraryOptions = new LibraryOptions
                    {
                        ThumbnailImagesIntervalSeconds = 10,
                        CacheImages = options.CacheImages
                    };

                    thumbnailResult = await Plugin.VideoThumbnailApi.RefreshThumbnailImages(item,
                        dummyLibraryOptions, directoryService, chapters, false, false,
                        CancellationToken.None);

                    if (thumbnailResult)
                    {
                        _logger.Info("ChapterInfoPersist - Video Thumbnail Restore Success (" + source + "): " +
                                     localThumbnailSets[0].Path);
                    }
                }
            }

            _itemRepository.SaveChapters(item.InternalId, true, chapters);

            return thumbnailResult;
        }
    }
}
