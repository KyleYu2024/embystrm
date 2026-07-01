using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmLiteAssistant.Options
{
    public class GeneralOptions : EditableOptionsBase
    {
        [DisplayName("通用")]
        public override string EditorTitle => "通用";

        [DisplayName("追更模式")]
        [Description("电影洗版或剧集有更新后实时提取媒体信息以及剧集元数据刷新，默认关闭。")]
        [Required]
        public bool CatchupMode { get; set; } = false;

        public enum CatchupTask
        {
            [Description("媒体信息提取")]
            MediaInfo,
            [Description("剧集元数据刷新")]
            EpisodeRefresh
        }

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> CatchupTaskList { get; set; } = new List<EditorSelectOption>();

        [DisplayName("追更任务范围")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(CatchupTaskList))]
        [VisibleCondition(nameof(CatchupMode), SimpleCondition.IsTrue)]
        public string CatchupTaskScope { get; set; } = string.Join(",",
            CatchupTask.MediaInfo.ToString(), CatchupTask.EpisodeRefresh.ToString());

        [DisplayName("主最大并发线程数")]
        [Description("媒体信息提取任务共享，必须在 1 至 20 之间，默认为 1。")]
        [Required, MinValue(1), MaxValue(20)]
        public int MaxConcurrentCount { get; set; } = 1;

        [DisplayName("主线程冷却时间（秒）")]
        [Description("单线程模式有效，必须在 0 至 60 之间，默认为 0。")]
        [VisibleCondition(nameof(MaxConcurrentCount), ValueCondition.IsEqual, 1)]
        [Required, MinValue(0), MaxValue(60)]
        public int CooldownDurationSeconds { get; set; } = 0;

        [DisplayName("次最大并发线程数")]
        [Description("剧集元数据刷新、本地任务共享，必须在 1 至 20 之间，默认为 1。")]
        [Required, MinValue(1), MaxValue(20)]
        public int Tier2MaxConcurrentCount { get; set; } = 1;
    }
}
