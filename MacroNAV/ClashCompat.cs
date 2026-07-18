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

        // Clash tests owned by the document are read-only: mutating one straight
        // out of the collection throws "Object is Read-Only". Edits must be made
        // on a detached copy which is then written back via CommitTest.
        //
        // Returns a mutable ClashTest for `name` — a copy of the existing test if
        // there is one, otherwise a fresh blank test.
        public static ClashTest GetEditableTest(DocumentClashTests dct, string name)
        {
            var existing = FindTestByName(dct, name);
            if (existing != null) return (ClashTest)existing.CreateCopy();
            return new ClashTest { DisplayName = name };
        }

        // Writes an edited detached test back into the document, replacing the
        // test of the same name in place if it exists.
        public static void CommitTest(DocumentClashTests dct, string name, ClashTest edited)
        {
            var existing = FindTestByName(dct, name);
            if (existing == null) { AddCopyAtRoot(dct, edited); return; }

            var index = GetTopLevelItems(dct).IndexOf(existing);
            if (index < 0) AddCopyAtRoot(dct, edited);
            else           ReplaceAtRoot(dct, index, edited);
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

        // A ClashSelection references selection sets indirectly, as opaque
        // SelectionSource handles. Resolving one back to a named SavedItem (and
        // creating one from a SavedItem) both go through DocumentSelectionSets.

        // Returns the display names of every selection set wired into a
        // ClashSelection, pipe-delimited, ready to store in a MacroStep.
        public static string SerialiseSelectionNames(ClashSelection sel, Document doc)
        {
            if (sel?.Selection == null || doc == null) return string.Empty;
            var names = new List<string>();
            foreach (var source in sel.Selection.SelectionSources)
            {
                SavedItem item = null;
                try { item = doc.SelectionSets.ResolveSelectionSource(source); }
                catch { }
                if (item != null && !string.IsNullOrEmpty(item.DisplayName))
                    names.Add(item.DisplayName);
            }
            return string.Join("|", names);
        }

        // Clears the selection sources on a ClashSelection and re-populates them
        // by looking up names in the document's selection-set tree.
        public static void ApplySelectionSetNames(ClashSelection sel,
            Document doc, IEnumerable<string> names)
        {
            if (sel?.Selection == null || doc == null) return;
            sel.Selection.SelectionSources.Clear();
            foreach (var name in names)
            {
                var found = FindSelectionSetByName(doc, name.Trim());
                if (found == null) continue;
                try { sel.Selection.SelectionSources.Add(doc.SelectionSets.CreateSelectionSource(found)); }
                catch { }
            }
        }

        // ── Document helpers ──────────────────────────────────────────────────

        // SavedItem trees expose no search helper, so walk Children depth-first.
        private static T FindByName<T>(GroupItem root, string name) where T : SavedItem
        {
            if (root == null) return null;
            foreach (SavedItem child in root.Children)
            {
                if (child is T match && match.DisplayName == name) return match;
                if (child is GroupItem group)
                {
                    var nested = FindByName<T>(group, name);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        public static SelectionSet FindSelectionSetByName(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return FindByName<SelectionSet>(doc.SelectionSets.RootItem, name);
        }

        public static SavedViewpoint FindViewpointByName(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return FindByName<SavedViewpoint>(doc.SavedViewpoints.RootItem, name);
        }
    }
}
