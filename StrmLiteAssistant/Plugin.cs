using Emby.Media.Common.Extensions;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using StrmLiteAssistant.Common;
using StrmLiteAssistant.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using static StrmLiteAssistant.Options.GeneralOptions;
using static StrmLiteAssistant.Options.MediaInfoExtractOptions;
using static StrmLiteAssistant.Options.OptionUtility;

namespace StrmLiteAssistant
{
    public class Plugin : BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        private readonly Guid _id = new Guid("b5f9d4a2-9c2e-4b6d-8a77-2e97a0f2a6a1");
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;

        private bool _currentCatchupMode;
        private bool _currentPersistMediaInfo;
        private bool _currentMediaInfoRestoreMode;
        private int _currentMasterMaxConcurrentCount;
        private int _currentTier2MaxConcurrentCount;

        public static Plugin Instance { get; private set; }
        public static LibraryApi LibraryApi { get; private set; }
        public static MediaInfoApi MediaInfoApi { get; private set; }
        public static MetadataApi MetadataApi { get; private set; }
        public static VideoThumbnailApi VideoThumbnailApi { get; private set; }

        public readonly ILogger Logger;
        public readonly IApplicationHost ApplicationHost;
        public readonly IApplicationPaths ApplicationPaths;

        public Plugin(IApplicationHost applicationHost, IApplicationPaths applicationPaths, ILogManager logManager,
            IFileSystem fileSystem, ILibraryManager libraryManager, IUserManager userManager,
            IItemRepository itemRepository,
            ILibraryMonitor libraryMonitor, IMediaSourceManager mediaSourceManager, IMediaMountManager mediaMountManager,
            IProviderManager providerManager, IServerConfigurationManager configurationManager,
            IImageExtractionManager imageExtractionManager, IServerApplicationPaths serverApplicationPaths,
            IFfmpegManager ffmpegManager, IMediaEncoder mediaEncoder, IJsonSerializer jsonSerializer,
            IHttpClient httpClient, ISessionManager sessionManager) : base(applicationHost)
        {
            Instance = this;
            Logger = logManager.GetLogger(Name);
            ApplicationHost = applicationHost;
            ApplicationPaths = applicationPaths;
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _fileSystem = fileSystem;

            _currentCatchupMode = GetOptions().GeneralOptions.CatchupMode;
            _currentPersistMediaInfo = GetOptions().MediaInfoExtractOptions.PersistMediaInfoMode !=
                                       PersistMediaInfoOption.None.ToString();
            _currentMediaInfoRestoreMode = GetOptions().MediaInfoExtractOptions.PersistMediaInfoMode ==
                                           PersistMediaInfoOption.Restore.ToString();
            _currentMasterMaxConcurrentCount = GetOptions().GeneralOptions.MaxConcurrentCount;
            _currentTier2MaxConcurrentCount = GetOptions().GeneralOptions.Tier2MaxConcurrentCount;
            OptionUtility.InitializeOptionCache(this);

            LibraryApi = new LibraryApi(libraryManager, providerManager, fileSystem, mediaMountManager, userManager);
            MediaInfoApi = new MediaInfoApi(libraryManager, fileSystem, providerManager, mediaSourceManager,
                itemRepository, jsonSerializer, libraryMonitor);
            MetadataApi = new MetadataApi(fileSystem);
            VideoThumbnailApi = new VideoThumbnailApi(libraryManager, fileSystem, imageExtractionManager, itemRepository,
                mediaMountManager, serverApplicationPaths, libraryMonitor, ffmpegManager);

            if (_currentCatchupMode) QueueManager.Initialize();

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
            CollectionFolder.LibraryOptionsUpdated += OnLibraryOptionsUpdated;
        }

        public override string Description => "Extract and persist media info, then refresh episode metadata.";
        public override Guid Id => _id;
        public sealed override string Name => "Strm Lite Assistant";
        public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        public CultureInfo DefaultUICulture => new CultureInfo("zh-CN");
        public bool DebugMode => false;
        public bool IsModSupported => false;
        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Properties.thumb.png");
        }

        public PluginOptions GetPluginOptions() => GetOptions();

        protected override bool OnOptionsSaving(PluginOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.GeneralOptions.CatchupTaskScope))
            {
                options.GeneralOptions.CatchupTaskScope = CatchupTask.MediaInfo.ToString();
            }

            options.GeneralOptions.CatchupTaskScope = string.Join(",",
                options.GeneralOptions.CatchupTaskScope
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(v => options.GeneralOptions.CatchupTaskList.Any(option => option.Value == v)));

            options.MediaInfoExtractOptions.LibraryScope = string.Join(",",
                options.MediaInfoExtractOptions.LibraryScope
                    ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(v => options.MediaInfoExtractOptions.LibraryList.Any(option => option.Value == v)) ??
                Enumerable.Empty<string>());

            return base.OnOptionsSaving(options);
        }

        protected override void OnOptionsSaved(PluginOptions options)
        {
            if (_currentCatchupMode != options.GeneralOptions.CatchupMode)
            {
                _currentCatchupMode = options.GeneralOptions.CatchupMode;
                if (_currentCatchupMode) QueueManager.Initialize();
                else QueueManager.Dispose();
            }

            if (_currentMasterMaxConcurrentCount != options.GeneralOptions.MaxConcurrentCount)
            {
                _currentMasterMaxConcurrentCount = options.GeneralOptions.MaxConcurrentCount;
                QueueManager.UpdateMasterSemaphore(_currentMasterMaxConcurrentCount);
            }

            if (_currentTier2MaxConcurrentCount != options.GeneralOptions.Tier2MaxConcurrentCount)
            {
                _currentTier2MaxConcurrentCount = options.GeneralOptions.Tier2MaxConcurrentCount;
                QueueManager.UpdateTier2Semaphore(_currentTier2MaxConcurrentCount);
            }

            _currentPersistMediaInfo = options.MediaInfoExtractOptions.PersistMediaInfoMode !=
                                       PersistMediaInfoOption.None.ToString();
            _currentMediaInfoRestoreMode = options.MediaInfoExtractOptions.PersistMediaInfoMode ==
                                           PersistMediaInfoOption.Restore.ToString();
            OptionUtility.UpdateCatchupScope(this);
            LibraryApi.UpdateLibraryPathsInScope();
            base.OnOptionsSaved(options);
        }

        protected override PluginOptions OnBeforeShowUI(PluginOptions options)
        {
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(item => !LibraryApi.ExcludedCollectionTypes.Contains(item.CollectionType))
                .Select(item => new EditorSelectOption
                {
                    Value = item.ItemId,
                    Name = item.Name,
                    IsEnabled = true
                })
                .ToList();

            libraries.Insert(0, new EditorSelectOption { Value = "-1", Name = "收藏", IsEnabled = true });
            options.MediaInfoExtractOptions.LibraryList = libraries;

            options.GeneralOptions.CatchupTaskList = Enum.GetValues(typeof(CatchupTask))
                .Cast<CatchupTask>()
                .Select(item => new EditorSelectOption
                {
                    Value = item.ToString(),
                    Name = EnumExtensions.GetDescription(item),
                    IsEnabled = true
                })
                .ToList();

            options.MediaInfoExtractOptions.PersistMediaInfoOptionList = new List<EditorRadioOption>
            {
                new EditorRadioOption
                {
                    Value = PersistMediaInfoOption.Default,
                    PrimaryText = "媒体信息持久化",
                    SecondaryText = "保存或加载媒体信息和章节片头片尾标记至/自 JSON 文件，以及恢复预览缩略图 BIF。"
                },
                new EditorRadioOption
                {
                    Value = PersistMediaInfoOption.Restore,
                    PrimaryText = "媒体信息恢复模式",
                    SecondaryText = "仅从 JSON 或 BIF 恢复媒体信息、章节片头片尾标记、预览缩略图，跳过实际提取。"
                },
                new EditorRadioOption { Value = PersistMediaInfoOption.None, PrimaryText = "关闭" }
            };

            options.MetadataEnhanceOptions.EpisodeRefreshOptionList = Enum
                .GetValues(typeof(MetadataEnhanceOptions.EpisodeRefreshOption))
                .Cast<MetadataEnhanceOptions.EpisodeRefreshOption>()
                .Select(item => new EditorSelectOption
                {
                    Value = item.ToString(),
                    Name = EnumExtensions.GetDescription(item),
                    IsEnabled = true
                })
                .ToList();

            return base.OnBeforeShowUI(options);
        }

        protected override void OnCreatePageInfo(PluginPageInfo pageInfo)
        {
            pageInfo.Name = Name;
            pageInfo.DisplayName = Name;
            pageInfo.EnableInMainMenu = true;
            pageInfo.MenuIcon = "video_settings";
            base.OnCreatePageInfo(pageInfo);
        }

        private async void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var restored = false;
                if (_currentPersistMediaInfo && e.Item is Video)
                {
                    var directoryService = new DirectoryService(Logger, _fileSystem);
                    var hasMediaInfo = LibraryApi.HasMediaInfo(e.Item);

                    if (!hasMediaInfo)
                    {
                        restored = await MediaInfoApi.DeserializeMediaInfo(e.Item, directoryService,
                            "OnItemAdded Restore", true).ConfigureAwait(false);
                    }
                    else
                    {
                        _ = MediaInfoApi.SerializeMediaInfo(e.Item.InternalId, directoryService, true,
                            "OnItemAdded Overwrite");
                    }
                }

                if (!_currentCatchupMode) return;

                if (IsCatchupTaskSelected(CatchupTask.MediaInfo) && !restored && (e.Item is Video || e.Item is Audio))
                {
                    QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
                }

                if (IsCatchupTaskSelected(CatchupTask.EpisodeRefresh) && e.Item is Episode episode &&
                    LibraryApi.IsPremiereDateInScope(episode, DateTimeOffset.UtcNow.AddDays(-90), false))
                {
                    QueueManager.EpisodeRefreshItemQueue.Enqueue(episode);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex.Message);
                Logger.Debug(ex.StackTrace);
            }
        }

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            if (!_currentMediaInfoRestoreMode && e.Item is Video)
            {
                MediaInfoApi.DeleteMediaInfoJson(e.Item, new DirectoryService(Logger, _fileSystem), "Item Removed Event");
            }
        }

        private void OnLibraryOptionsUpdated(object sender, GenericEventArgs<Tuple<CollectionFolder, LibraryOptions>> e)
        {
            LibraryApi.UpdateLibraryPathsInScope();
        }
    }
}
