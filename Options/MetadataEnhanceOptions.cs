using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmLiteAssistant.Options
{
    public class MetadataEnhanceOptions : EditableOptionsBase
    {
        [DisplayName("元数据增强")]
        public override string EditorTitle => "元数据增强";

        public enum EpisodeRefreshOption
        {
            [Description("无简介")]
            NoOverview,
            [Description("无图")]
            NoImage,
            [Description("非中文简介")]
            NonChineseOverview,
            [Description("默认剧集名")]
            DefaultEpisodeName,
            [Description("替换截图")]
            ReplaceCapturedImage
        }

        [Browsable(false)]
        public List<EditorSelectOption> EpisodeRefreshOptionList { get; set; } = new List<EditorSelectOption>();

        [DisplayName("剧集刷新范围")]
        [Description("计划任务和追更模式剧集元数据刷新的范围，涵盖所有剧集媒体库，默认为无图无简介。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(EpisodeRefreshOptionList))]
        public string EpisodeRefreshScope { get; set; } = string.Join(",",
            EpisodeRefreshOption.NoOverview.ToString(), EpisodeRefreshOption.NoImage.ToString());

        [DisplayName("剧集刷新回溯天数")]
        [Description("计划任务剧集元数据刷新的回溯天数，默认为365天。")]
        [Required, MinValue(1)]
        public int EpisodeRefreshLookbackDays { get; set; } = 365;
    }
}
