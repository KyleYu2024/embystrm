using System.Text.RegularExpressions;

namespace StrmLiteAssistant.Common
{
    public static class LanguageUtility
    {
        private static readonly Regex ChineseRegex = new Regex(@"[\u4E00-\u9FFF]", RegexOptions.Compiled);
        private static readonly Regex JapaneseRegex = new Regex(@"[\u3040-\u30FF]", RegexOptions.Compiled);
        private static readonly Regex DefaultChineseEpisodeNameRegex =
            new Regex(@"^第\s*\d+\s*集$", RegexOptions.Compiled);

        public static bool IsChinese(string input)
        {
            return !string.IsNullOrEmpty(input) && ChineseRegex.IsMatch(input) &&
                   !JapaneseRegex.IsMatch(input.Replace("\u30FB", string.Empty));
        }

        public static bool IsDefaultChineseEpisodeName(string input)
        {
            return !string.IsNullOrEmpty(input) && DefaultChineseEpisodeNameRegex.IsMatch(input);
        }
    }
}
