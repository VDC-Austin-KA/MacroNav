using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace MacroNAV
{
    // Papers over Clash API differences between Navisworks 2025 and 2027.
    //
    // NW2025:
    //   - DocumentClashTests.Tests  is the flat IList<SavedItem> of tests.
    //   - Tests.AddNewClashTest()   creates a new blank test in-place.
    //   - TestsAddCopy(test)        copies an existing test to the root.
    //   - TestsReplaceWithCopy(i,t) replaces by index.
    //   - ClashResult.AssignedTo / ApprovedBy  are plain strings.
    //
    // NW2027:
    //   - Tests was removed; root items live in TestsRoot.Children.
    //   - AddNewClashTest() is now called on TestsRoot directly.
    //   - TestsAddCopy / TestsReplaceWithCopy require an explicit parent GroupItem.
    //   - ClashResult.AssignedTo / ApprovedBy  are Assignee objects.
    //
    // Switch at compile time via DefineConstants set in MacroNAV.csproj:
    //   Release-NW2025  ->  NW2025
    //   Release-NW2027  ->  NW2027
    internal static class ClashCompat
    {
        // ── Collections ───────────────────────────────────────────────────────

        public static IList<SavedItem> GetTopLevelItems(DocumentClashTests dct)
        {
#if NW2027
            return dct.Value.TestsRoot.Children;
#else
            return dct.Tests;
#endif
        }

        public static GroupItem GetTestsRoot(DocumentClashTests dct)
        {
#if NW2027
            return dct.Value.TestsRoot;
#else
            return null; // unused in NW2025
#endif
        }

        public static IEnumerable<ClashTest> EnumerateTests(DocumentClashTests dct)
            => GetTopLevelItems(dct).OfType<ClashTest>();

        public static ClashTest FindTestByName(DocumentClashTests dct, string name)
            => EnumerateTests(dct).FirstOrDefault(t => t.DisplayName == name);

        public static int TestCount(DocumentClashTests dct)
            => GetTopLevelItems(dct).Count;

        // ── Create / copy ─────────────────────────────────────────────────────

        // Creates a brand-new blank ClashTest and returns it.
        public static ClashTest AddNewTest(DocumentClashTests dct)
        {
#if NW2027
            dct.Value.TestsRoot.AddNewClashTest();
            return dct.Value.TestsRoot.Children.OfType<ClashTest>().Last();
#else
            dct.Tests.AddNewClashTest();
            return dct.Tests.OfType<ClashTest>().Last();
#endif
        }

        // Adds a copy of an existing ClashTest at the root level.
        public static void AddCopyAtRoot(DocumentClashTests dct, ClashTest test)
        {
#if NW2027
            dct.TestsAddCopy(dct.Value.TestsRoot, test);
#else
            dct.TestsAddCopy(test);
#endif
        }

        // Replaces the test at index with a copy of the supplied test.
        public static void ReplaceAtRoot(DocumentClashTests dct, int index, ClashTest test)
        {
#if NW2027
            dct.TestsReplaceWithCopy(dct.Value.TestsRoot, index, test);
#else
            dct.TestsReplaceWithCopy(index, test);
#endif
        }

        // ── Result metadata ───────────────────────────────────────────────────

        public static string GetAssignedTo(ClashResult result)
        {
#if NW2025
            return result.AssignedTo ?? string.Empty;
#else
            return result.AssignedTo?.DisplayName ?? string.Empty;
#endif
        }

        public static string GetApprovedBy(ClashResult result)
        {
#if NW2025
            return result.ApprovedBy ?? string.Empty;
#else
            return result.ApprovedBy?.DisplayName ?? string.Empty;
#endif
        }

        // ── Selection helpers ─────────────────────────────────────────────────

        // Returns the display names of every selection set wired into a
        // ClashSelectionSource, pipe-delimited, ready to store in a MacroStep.
        public static string SerialiseSelectionNames(ClashSelectionSource src)
        {
            if (src?.Selection?.SelectionSets == null) return string.Empty;
            return string.Join("|", src.Selection.SelectionSets
                .Select(ss => ss.DisplayName ?? ss.Guid.ToString()));
        }

        // Clears the selection sets on a ClashSelectionSource and re-populates
        // them by looking up names in the document's selection-set tree.
        public static void ApplySelectionSetNames(ClashSelectionSource src,
            Document doc, IEnumerable<string> names)
        {
            src.Selection.SelectionSets.Clear();
            foreach (var name in names)
            {
                var found = FindSelectionSetByName(doc, name.Trim());
                if (found != null)
                    src.Selection.SelectionSets.Add(found);
            }
        }

        // ── Document helpers ──────────────────────────────────────────────────

        public static SelectionSet FindSelectionSetByName(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return doc.SelectionSets.RootItem
                .FindFirst(si => si is SelectionSet ss && ss.DisplayName == name, true)
                as SelectionSet;
        }

        public static SavedViewpoint FindViewpointByName(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return doc.SavedViewpoints.RootItem
                .FindFirst(si => si is SavedViewpoint vp && vp.DisplayName == name, true)
                as SavedViewpoint;
        }
    }
}
