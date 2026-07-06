using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.MediaInfo;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmLiteAssistant.Options
{
    public class MediaInfoExtractOptions : EditableOptionsBase
    {
        [DisplayName("媒体信息提取")]
        public override string EditorTitle => "媒体信息提取";

        [DisplayName("包含分集和附加内容")]
        [Description("提取电影或剧集的分集和附加内容的媒体信息，默认关闭。")]
        [Required]
        public bool IncludeExtra { get; set; } = false;

        [Browsable(false)]
        [Required]
        public bool EnableImageCapture => false;

        [Browsable(false)]
        [Required]
        public int ImageCapturePosition { get; set; } = 10;

        [Browsable(false)]
        [Required]
        public string ImageCaptureExcludeMediaContainers { get; set; } =
            string.Join(",", new[] { MediaContainers.MpegTs, MediaContainers.Ts, MediaContainers.M2Ts });

        public enum PersistMediaInfoOption
        {
            None,
            Default,
            Restore
        }

        [Browsable(false)]
        public List<EditorRadioOption> PersistMediaInfoOptionList { get; set; } = new List<EditorRadioOption>();

        [DisplayName("")]
        [SelectItemsSource(nameof(PersistMediaInfoOptionList))]
        [SelectShowRadioGroup]
        public string PersistMediaInfoMode { get; set; } = PersistMediaInfoOption.None.ToString();

        [DisplayName("可选媒体信息 JSON 根目录")]
        [Description("在此根文件夹下存储或加载媒体信息 JSON 文件，默认为空。")]
        [EditFolderPicker]
        [VisibleCondition(nameof(PersistMediaInfoMode), ValueCondition.IsNotEqual, PersistMediaInfoOption.None)]
        public string MediaInfoJsonRootFolder { get; set; } = string.Empty;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; } = new List<EditorSelectOption>();

        [DisplayName("媒体库范围")]
        [Description("媒体库范围，留空包含所有。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        public string LibraryScope { get; set; } = string.Empty;
    }
}
