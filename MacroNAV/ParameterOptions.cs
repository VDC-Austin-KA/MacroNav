using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using MacroNAV.Models;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace MacroNAV
{
    // Supplies the allowed values for a step parameter so the editor can offer
    // the same lists Navisworks and AutoNAV show, instead of free text where a
    // typo silently fails at playback.
    //
    // Everything is best-effort: with no document open, or AutoNAV not loaded,
    // the list comes back empty and the editor falls back to typing.
    public static class ParameterOptions
    {
        public static IReadOnlyList<string> For(MacroStepType stepType, string key)
        {
            try { return Resolve(stepType, key) ?? new List<string>(); }
            catch (Exception ex)
            {
                RecorderLog.Warn($"parameter options for {stepType}.{key} failed", ex);
                return new List<string>();
            }
        }

        private static IReadOnlyList<string> Resolve(MacroStepType stepType, string key)
        {
            switch (key)
            {
                case "TestName":
                    // "*" first for the rename sweep.
                    var tests = ClashTestNames();
                    if (stepType == MacroStepType.AutoNavRenameGroups)
                        return new[] { "*" }.Concat(tests).ToList();
                    return tests;

                case "SelectionA":
                case "SelectionB":
                    return SelectionSetNames();

                case "Template":
                    return AutoNavRenameTemplates.Presets.Select(p => p.Value).ToList();

                case "Statuses":
                    // Offer the common combinations plus each single status.
                    return new List<string> { "New|Active", "New", "Active", "Reviewed",
                                              "Approved", "Resolved", "New|Active|Reviewed" };

                case "PrimaryGroupBy":
                case "SubGroupBy":
                    return GroupingModes();

                case "Discipline":
                case "Disciplines":
                    return Disciplines();

                case "Type":
                    return Enum.GetNames(typeof(ClashTestType)).ToList();

                case "Name":
                    // Same key is used for viewpoints and selection sets.
                    if (stepType == MacroStepType.ViewpointActivate ||
                        stepType == MacroStepType.ViewpointSaveCurrent)
                        return ViewpointNames();
                    return SelectionSetNames();

                case "UseSaved":
                    return new List<string> { "true", "false" };

                default:
                    return null;
            }
        }

        private static List<string> ClashTestNames()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return new List<string>();
            try
            {
                return ClashCompat.EnumerateTests(doc.GetClash().TestsData)
                    .Select(t => t.DisplayName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct().OrderBy(n => n).ToList();
            }
            catch { return new List<string>(); }
        }

        private static List<string> SelectionSetNames()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return new List<string>();
            var names = new List<string>();
            Walk(doc.SelectionSets.RootItem, item =>
            {
                if (item is SelectionSet ss && !string.IsNullOrEmpty(ss.DisplayName))
                    names.Add(ss.DisplayName);
            });
            return names.Distinct().OrderBy(n => n).ToList();
        }

        private static List<string> ViewpointNames()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return new List<string>();
            var names = new List<string>();
            Walk(doc.SavedViewpoints.RootItem, item =>
            {
                if (item is SavedViewpoint vp && !string.IsNullOrEmpty(vp.DisplayName))
                    names.Add(vp.DisplayName);
            });
            return names.Distinct().OrderBy(n => n).ToList();
        }

        // Sets and viewpoints are routinely filed in folders, so recurse.
        private static void Walk(GroupItem group, Action<SavedItem> visit)
        {
            if (group == null) return;
            foreach (SavedItem item in group.Children)
            {
                visit(item);
                if (item is GroupItem nested) Walk(nested, visit);
            }
        }

        // AutoNAV's grouping modes, read from its own enum so the list cannot
        // drift from what AutoNAV actually accepts.
        private static List<string> GroupingModes()
        {
            var asm = FindAutoNav();
            var enumType = asm?.GetType("AutoNAV.ClashGrouper+GroupingMode");
            if (enumType != null && enumType.IsEnum)
                return Enum.GetNames(enumType).ToList();

            // AutoNAV not loaded: fall back to the modes it ships with.
            return new List<string>
            {
                "None", "Level", "GridIntersection", "SelectionA", "SelectionB",
                "ModelA", "ModelB", "AssignedTo", "ApprovedBy", "Status",
                "File", "Layer", "First", "Last", "LastUnique", "WallsAndFloors"
            };
        }

        private static List<string> Disciplines()
        {
            var asm = FindAutoNav();
            var type = asm?.GetType("AutoNAV.SearchSetGenerator");
            var method = type?.GetMethod("GetAvailableDisciplines",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) return new List<string>();
            try
            {
                return (method.Invoke(null, null) as IEnumerable<string>)?.ToList()
                       ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        private static Assembly FindAutoNav()
            => AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name.Equals("AutoNAV", StringComparison.OrdinalIgnoreCase));
    }
}
