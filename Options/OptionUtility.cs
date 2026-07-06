using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;
using static StrmLiteAssistant.Options.GeneralOptions;

namespace StrmLiteAssistant.Options
{
    public static class OptionUtility
    {
        private static HashSet<string> _selectedCatchupTasks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void InitializeOptionCache(Plugin plugin)
        {
            UpdateCatchupScope(plugin);
        }

        public static void UpdateCatchupScope(Plugin plugin)
        {
            var catchupTaskScope = plugin.GetPluginOptions().GeneralOptions.CatchupTaskScope;
            _selectedCatchupTasks = new HashSet<string>(
                catchupTaskScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsCatchupTaskSelected(params CatchupTask[] tasksToCheck)
        {
            return tasksToCheck.Any(f => _selectedCatchupTasks.Contains(f.ToString()));
        }

        public static string[] GetValidLibraryIds(string scope)
        {
            var libraryIds = scope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (libraryIds?.Any() != true) return Array.Empty<string>();

            var parsedIds = libraryIds
                .Select(id => long.TryParse(id, out var result) ? result : (long?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToArray();

            return parsedIds.Any()
                ? BaseItem.LibraryManager
                    .GetInternalItemIds(new InternalItemsQuery { ItemIds = parsedIds })
                    .Select(id => id.ToString())
                    .ToArray()
                : Array.Empty<string>();
        }
    }
}
