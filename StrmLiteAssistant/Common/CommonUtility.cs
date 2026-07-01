using System.IO;
using System.Linq;

namespace StrmLiteAssistant.Common
{
    public static class CommonUtility
    {
        public static bool IsDirectoryEmpty(string directoryPath)
        {
            return Directory.Exists(directoryPath) &&
                   !Directory.EnumerateFileSystemEntries(directoryPath).Take(1).Any();
        }
    }
}
