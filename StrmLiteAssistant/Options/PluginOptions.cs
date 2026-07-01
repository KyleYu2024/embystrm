using Emby.Web.GenericEdit;
using System.ComponentModel;

namespace StrmLiteAssistant.Options
{
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "Strm Lite Assistant";
        public override string EditorDescription => string.Empty;

        [DisplayName("通用")]
        public GeneralOptions GeneralOptions { get; set; } = new GeneralOptions();

        [DisplayName("媒体信息提取")]
        public MediaInfoExtractOptions MediaInfoExtractOptions { get; set; } = new MediaInfoExtractOptions();

        [DisplayName("元数据增强")]
        public MetadataEnhanceOptions MetadataEnhanceOptions { get; set; } = new MetadataEnhanceOptions();
    }
}
