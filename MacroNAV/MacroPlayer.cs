using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using MacroNAV.Models;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace MacroNAV
{
    public class MacroPlayer
    {
        public event EventHandler<MacroStep>    StepStarted;
        public event EventHandler<StepResult>   StepCompleted;
        public event EventHandler               PlaybackCompleted;

        private CancellationTokenSource _cts;
        public bool IsPlaying => _cts != null && !_cts.IsCancellationRequested;

        private Assembly _autoNavAssembly;
        private bool _autoNavSearched;

        public async Task PlayAsync(IEnumerable<MacroStep> steps, bool stopOnError = true)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            try
            {
                foreach (var step in steps.Where(s => s.IsEnabled))
                {
                    if (token.IsCancellationRequested) break;
                    StepStarted?.Invoke(this, step);
                    var result = await ExecuteStepAsync(step, token);
                    StepCompleted?.Invoke(this, result);
                    if (!result.Success && stopOnError) break;
                }
            }
            finally
            {
                _cts = null;
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Stop() => _cts?.Cancel();

        private async Task<StepResult> ExecuteStepAsync(MacroStep step, CancellationToken token)
        {
            try
            {
                switch (step.StepType)
                {
                    case MacroStepType.Comment:
                        return StepResult.Ok(step, "(comment)");

                    case MacroStepType.Delay:
                    {
                        int ms = int.TryParse(step.Parameters.Get("Milliseconds"), out int v) ? v : 1000;
                        await Task.Delay(ms, token);
                        return StepResult.Ok(step, $"Waited {ms} ms");
                    }

                    case MacroStepType.ClashCreateTest:      return ExecClashCreateTest(step);
                    case MacroStepType.ClashSetSelectionA:   return ExecClashSetSelection(step, "A");
                    case MacroStepType.ClashSetSelectionB:   return ExecClashSetSelection(step, "B");
                    case MacroStepType.ClashRunTest:         return ExecClashRunTest(step);
                    case MacroStepType.ClashRunAllTests:     return ExecClashRunAllTests(step);
                    case MacroStepType.ViewpointActivate:    return ExecViewpointActivate(step);
                    case MacroStepType.ViewpointSaveCurrent: return ExecViewpointSaveCurrent(step);
                    case MacroStepType.SearchSetActivate:    return ExecSearchSetActivate(step);
                    case MacroStepType.FileOpen:             return ExecFileOpen(step);
                    case MacroStepType.FileAppend:           return ExecFileAppend(step);

                    case MacroStepType.AutoNavFunction1SearchSetGen:
                        return ExecAutoNavStaticMethod(step,
                            "AutoNAV.SearchSetGenerator", "GenerateFunction1SearchSets",
                            "AutoNAV F1: Generate Discipline Search Sets");

                    case MacroStepType.AutoNavFunction2SearchSetGen:
                    {
                        string discs    = step.Parameters.Get("Disciplines") ?? string.Empty;
                        string propCat  = step.Parameters.Get("PropCategory") ?? "Item";
                        string propName = step.Parameters.Get("PropName") ?? "Category";
                        var discList    = discs.Split(new[]{','}, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(d => d.Trim()).ToList();
                        return ExecAutoNavStaticMethod(step,
                            "AutoNAV.SearchSetGenerator", "GenerateFunction2SearchSets",
                            $"AutoNAV F2: {propName}",
                            discList, propCat, propName);
                    }

                    case MacroStepType.AutoNavFunction3CustomSearchSetGen:
                    {
                        string disc     = step.Parameters.Get("Discipline") ?? string.Empty;
                        string propCat  = step.Parameters.Get("PropCategory") ?? "Item";
                        string propName = step.Parameters.Get("PropName") ?? "Category";
                        return ExecAutoNavStaticMethod(step,
                            "AutoNAV.SearchSetGenerator", "GenerateCustomSearchSets",
                            $"AutoNAV F3: {disc}/{propName}",
                            disc, propCat, propName);
                    }

                    case MacroStepType.AutoNavClashTestGen:
                    case MacroStepType.AutoNavClashTestGenLegacy:
                        return ExecAutoNavInstanceMethod(step,
                            "AutoNAV.ClashTestGeneratorEngine", "GenerateClashTests",
                            "AutoNAV F4: Generate Clash Tests");

                    case MacroStepType.AutoNavClashRunAndGroup:
                        return ExecAutoNavInstanceMethod(step,
                            "AutoNAV.ClashTestGeneratorEngine", "RunClashTestsAndGroupResults",
                            "AutoNAV F5: Run Tests & Group");

                    case MacroStepType.AutoNavClashGroupTest:
                    {
                        string testName = step.Parameters.Get("TestName");
                        string primary  = step.Parameters.Get("PrimaryGroupBy") ?? "Element";
                        string sub      = step.Parameters.Get("SubGroupBy") ?? "None";
                        return ExecAutoNavClashGroup(step, testName, primary, sub);
                    }

                    case MacroStepType.AutoNavClashUngroup:
                        return ExecAutoNavClashUngroup(step, step.Parameters.Get("TestName"));

                    case MacroStepType.AutoNavRenameGroups:
                        return ExecAutoNavRenameGroups(step);

                    case MacroStepType.AutoNavGroupWallsFloors:
                    {
                        var res = ExecAutoNavStaticMethodWithResult(step,
                            "AutoNAV.ClashGrouper", "GroupAllTestsByWallsAndFloors");
                        return res.Item1
                            ? StepResult.Ok(step, "AutoNAV: grouped by walls/floors. " + res.Item2)
                            : StepResult.Fail(step, res.Item2);
                    }

                    case MacroStepType.AutoNavRunAllClashTests:
                    {
                        var res = ExecAutoNavStaticMethodWithResult(step,
                            "AutoNAV.ClashTestGeneratorEngine", "RunAllClashTests");
                        return res.Item1
                            ? StepResult.Ok(step, $"AutoNAV: ran all clash tests ({res.Item2}).")
                            : StepResult.Fail(step, res.Item2);
                    }

                    case MacroStepType.AutoNavSearchSetGen:
                        return StepResult.Ok(step, "Legacy step — open AutoNAV and run Function 1/2/3.");

                    default:
                        return StepResult.Ok(step, $"Step type {step.StepType} acknowledged.");
                }
            }
            catch (OperationCanceledException) { return StepResult.Fail(step, "Cancelled"); }
            catch (Exception ex)               { return StepResult.Fail(step, ex.Message); }
        }

        private StepResult ExecClashCreateTest(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, "Clash module unavailable: " + ex.Message); }

            var testName = step.Parameters.Get("TestName") ?? step.DisplayName;
            step.Parameters.TryGetValue("SelectionA", out string selAStr);
            step.Parameters.TryGetValue("SelectionB", out string selBStr);
            step.Parameters.TryGetValue("Tolerance",  out string tolStr);

            using (doc.BeginTransaction("MacroNAV: Configure Clash Test"))
            {
                var dct  = clash.TestsData;
                var test = ClashCompat.GetEditableTest(dct, testName);
                test.DisplayName = testName;
                if (double.TryParse(tolStr, out double tol)) test.Tolerance = tol;
                if (!string.IsNullOrEmpty(selAStr))
                    ClashCompat.ApplySelectionSetNames(test.SelectionA, doc,
                        selAStr.Split(new[]{'|'}, StringSplitOptions.RemoveEmptyEntries));
                if (!string.IsNullOrEmpty(selBStr))
                    ClashCompat.ApplySelectionSetNames(test.SelectionB, doc,
                        selBStr.Split(new[]{'|'}, StringSplitOptions.RemoveEmptyEntries));
                ClashCompat.CommitTest(dct, testName, test);
            }
            return StepResult.Ok(step, $"Clash test '{testName}' configured");
        }

        private StepResult ExecClashSetSelection(MacroStep step, string ab)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, ex.Message); }

            var testName = step.Parameters.Get("TestName");
            if (ClashCompat.FindTestByName(clash.TestsData, testName) == null)
                return StepResult.Fail(step, $"Test '{testName}' not found");

            string selStr = step.Parameters.Get("Selection" + ab) ?? string.Empty;
            using (doc.BeginTransaction($"MacroNAV: Set Selection {ab}"))
            {
                // Edit a detached copy — tests in the document are read-only.
                var dct  = clash.TestsData;
                var test = ClashCompat.GetEditableTest(dct, testName);
                var sel  = ab == "A" ? test.SelectionA : test.SelectionB;
                ClashCompat.ApplySelectionSetNames(sel, doc,
                    selStr.Split(new[]{'|'}, StringSplitOptions.RemoveEmptyEntries));
                ClashCompat.CommitTest(dct, testName, test);
            }
            return StepResult.Ok(step, $"Set Selection{ab} on '{testName}'");
        }

        private StepResult ExecClashRunTest(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, ex.Message); }

            var testName = step.Parameters.Get("TestName");
            var test = ClashCompat.FindTestByName(clash.TestsData, testName);
            if (test == null) return StepResult.Fail(step, $"Test '{testName}' not found");
            // TestsRunTest runs exactly this test; no need to mark it stale and
            // re-run the whole set.
            clash.TestsData.TestsRunTest(test);
            return StepResult.Ok(step, $"Ran clash test '{testName}'");
        }

        private StepResult ExecClashRunAllTests(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, ex.Message); }
            clash.TestsData.TestsRunAllTests();
            return StepResult.Ok(step, "All clash tests run");
        }

        private StepResult ExecViewpointActivate(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");

            bool useSaved = step.Parameters.Get("UseSaved") == "true";
            var  name     = step.Parameters.Get("Name");

            if (useSaved)
            {
                var saved = ClashCompat.FindViewpointByName(doc, name);
                if (saved == null) return StepResult.Fail(step, $"Viewpoint '{name}' not found");
                doc.CurrentViewpoint.CopyFrom(saved.Viewpoint);
                return StepResult.Ok(step, $"Activated saved viewpoint '{name}'");
            }

            var vp = doc.CurrentViewpoint.CreateCopy();

            // Recorded steps carry a lossless camera blob. Fall back to explicit
            // vectors so hand-authored and older macros still play.
            if (step.Parameters.TryGetValue("Camera", out string camera)
                && !string.IsNullOrEmpty(camera)
                && vp.TrySetCamera(camera))
            {
                doc.CurrentViewpoint.CopyFrom(vp);
                return StepResult.Ok(step, "Viewpoint restored");
            }

            if (!step.Parameters.TryGetValue("PosX", out string pxStr))
                return StepResult.Fail(step, "No position data stored in step");

            vp.Position = new Point3D(
                double.Parse(pxStr),
                double.Parse(step.Parameters.Get("PosY") ?? "0"),
                double.Parse(step.Parameters.Get("PosZ") ?? "0"));
            vp.AlignDirection(new Vector3D(
                double.Parse(step.Parameters.Get("LookX") ?? "0"),
                double.Parse(step.Parameters.Get("LookY") ?? "1"),
                double.Parse(step.Parameters.Get("LookZ") ?? "0")));
            vp.AlignUp(new Vector3D(
                double.Parse(step.Parameters.Get("UpX") ?? "0"),
                double.Parse(step.Parameters.Get("UpY") ?? "0"),
                double.Parse(step.Parameters.Get("UpZ") ?? "1")));
            if (step.Parameters.TryGetValue("Fov", out string fovStr) && double.TryParse(fovStr, out double fov))
                vp.HeightField = fov;
            doc.CurrentViewpoint.CopyFrom(vp);
            return StepResult.Ok(step, "Viewpoint restored");
        }

        private StepResult ExecViewpointSaveCurrent(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            var name = step.Parameters.Get("Name") ?? $"Saved {DateTime.Now:HH:mm:ss}";
            using (doc.BeginTransaction("MacroNAV: Save Viewpoint"))
            {
                var vp = new SavedViewpoint(doc.CurrentViewpoint.CreateCopy());
                vp.DisplayName = name;
                doc.SavedViewpoints.AddCopy(vp);
            }
            return StepResult.Ok(step, $"Saved viewpoint '{name}'");
        }

        private StepResult ExecSearchSetActivate(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            var name = step.Parameters.Get("Name");
            var ss   = ClashCompat.FindSelectionSetByName(doc, name);
            if (ss == null) return StepResult.Fail(step, $"Selection set '{name}' not found");
            doc.CurrentSelection.CopyFrom(ss.GetSelectedItems());
            return StepResult.Ok(step, $"Activated selection set '{name}'");
        }

        private StepResult ExecFileOpen(MacroStep step)
        {
            var path = step.Parameters.Get("Path");
            if (string.IsNullOrEmpty(path)) return StepResult.Fail(step, "No path specified");
            if (!System.IO.File.Exists(path)) return StepResult.Fail(step, $"File not found: {path}");
            var target = NavApp.ActiveDocument;
            if (target == null) return StepResult.Fail(step, "No active document");
            target.OpenFile(path);
            return StepResult.Ok(step, $"Opened: {path}");
        }

        private StepResult ExecFileAppend(MacroStep step)
        {
            var path = step.Parameters.Get("Path");
            if (string.IsNullOrEmpty(path)) return StepResult.Fail(step, "No path specified");
            if (!System.IO.File.Exists(path)) return StepResult.Fail(step, $"File not found: {path}");
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            doc.AppendFile(path);
            return StepResult.Ok(step, $"Appended: {path}");
        }

        private Assembly FindAutoNavAssembly()
        {
            if (_autoNavSearched) return _autoNavAssembly;
            _autoNavSearched = true;
            _autoNavAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "AutoNAV");
            return _autoNavAssembly;
        }

        // Renames clash groups by applying a naming template, driving AutoNAV's
        // ClashGrouper engine directly.
        //
        // This deliberately bypasses AutoNAV's rename tab, which first loads the
        // dashboard and enumerates every test and every group before you can act.
        // RegroupAndRenameExisting takes a test, a status filter and a template,
        // so a macro can just apply the rename and report the count.
        //
        // Parameters:
        //   TestName  test to rename, or "*" / empty for every test
        //   Template  naming template; defaults to AutoNAV's standard template
        //   Statuses  pipe-delimited ClashResultStatus filter (default New|Active)
        private StepResult ExecAutoNavRenameGroups(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");

            var asm = FindAutoNavAssembly();
            if (asm == null)
                return StepResult.Fail(step, "AutoNAV plugin is not loaded. Open AutoNAV before replaying this step.");

            var grouper = asm.GetType("AutoNAV.ClashGrouper");
            if (grouper == null) return StepResult.Fail(step, "AutoNAV type 'ClashGrouper' not found.");

            var method = grouper.GetMethod("RegroupAndRenameExisting",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
                return StepResult.Fail(step, "AutoNAV method 'RegroupAndRenameExisting' not found.");

            var template = step.Parameters.Get("Template");
            if (string.IsNullOrWhiteSpace(template))
                template = AutoNavRenameTemplates.Default;

            var statuses = BuildStatusSet(step.Parameters.Get("Statuses"));
            if (statuses == null)
                return StepResult.Fail(step, "Could not build clash status filter.");

            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, "Clash module unavailable: " + ex.Message); }

            var wanted = step.Parameters.Get("TestName");
            var targets = (string.IsNullOrWhiteSpace(wanted) || wanted == "*")
                ? ClashCompat.EnumerateTests(clash.TestsData).ToList()
                : ClashCompat.EnumerateTests(clash.TestsData)
                    .Where(t => t.DisplayName == wanted).ToList();

            if (targets.Count == 0)
                return StepResult.Fail(step, wanted == "*" || string.IsNullOrWhiteSpace(wanted)
                    ? "No clash tests in the document."
                    : $"Test '{wanted}' not found.");

            int done = 0;
            var failures = new List<string>();
            foreach (var test in targets)
            {
                try
                {
                    method.Invoke(null, new object[] { test, statuses, template });
                    done++;
                }
                catch (TargetInvocationException tie)
                {
                    failures.Add($"{test.DisplayName}: {tie.InnerException?.Message ?? tie.Message}");
                }
                catch (Exception ex) { failures.Add($"{test.DisplayName}: {ex.Message}"); }
            }

            var summary = $"Renamed groups in {done}/{targets.Count} test(s) using \"{template}\"";
            if (failures.Count > 0)
                return StepResult.Fail(step, summary + " — failed: " + string.Join("; ", failures));
            return StepResult.Ok(step, summary);
        }

        // AutoNAV filters by ClashResultStatus. Default to the statuses that
        // matter for a rename pass: unreviewed and active clashes.
        private static object BuildStatusSet(string pipeDelimited)
        {
            try
            {
                var names = string.IsNullOrWhiteSpace(pipeDelimited)
                    ? new[] { "New", "Active" }
                    : pipeDelimited.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

                var setType = typeof(HashSet<>).MakeGenericType(typeof(ClashResultStatus));
                var set = Activator.CreateInstance(setType);
                var add = setType.GetMethod("Add");

                foreach (var raw in names)
                {
                    var name = raw.Trim();
                    if (Enum.TryParse(name, true, out ClashResultStatus status))
                        add.Invoke(set, new object[] { status });
                }
                return set;
            }
            catch { return null; }
        }

        // Same as ExecAutoNavStaticMethod but surfaces the return value, which
        // AutoNAV uses to report what it did.
        private Tuple<bool, string> ExecAutoNavStaticMethodWithResult(
            MacroStep step, string typeName, string methodName)
        {
            var asm = FindAutoNavAssembly();
            if (asm == null)
                return Tuple.Create(false, "AutoNAV plugin is not loaded. Open AutoNAV before replaying this step.");
            var type = asm.GetType(typeName);
            if (type == null) return Tuple.Create(false, $"AutoNAV type '{typeName}' not found.");
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) return Tuple.Create(false, $"AutoNAV method '{methodName}' not found.");
            try
            {
                var result = method.Invoke(null, null);
                return Tuple.Create(true, result?.ToString() ?? string.Empty);
            }
            catch (TargetInvocationException tie)
            {
                return Tuple.Create(false, tie.InnerException?.Message ?? tie.Message);
            }
        }

        private StepResult ExecAutoNavStaticMethod(MacroStep step, string typeName,
            string methodName, string desc, params object[] args)
        {
            var asm = FindAutoNavAssembly();
            if (asm == null)
                return StepResult.Fail(step, "AutoNAV plugin is not loaded. Open AutoNAV before replaying this step.");
            var type = asm.GetType(typeName);
            if (type == null) return StepResult.Fail(step, $"AutoNAV type '{typeName}' not found.");
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) return StepResult.Fail(step, $"AutoNAV method '{methodName}' not found.");
            method.Invoke(null, args.Length > 0 ? args : null);
            return StepResult.Ok(step, desc);
        }

        private StepResult ExecAutoNavInstanceMethod(MacroStep step, string typeName,
            string methodName, string desc)
        {
            var asm = FindAutoNavAssembly();
            if (asm == null) return StepResult.Fail(step, "AutoNAV plugin is not loaded.");
            var type = asm.GetType(typeName);
            if (type == null) return StepResult.Fail(step, $"AutoNAV type '{typeName}' not found.");
            var instance = Activator.CreateInstance(type);
            var method   = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) return StepResult.Fail(step, $"AutoNAV method '{methodName}' not found.");
            method.Invoke(instance, null);
            return StepResult.Ok(step, desc);
        }

        private StepResult ExecAutoNavClashGroup(MacroStep step, string testName,
            string primaryGroupBy, string subGroupBy)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, ex.Message); }

            var test = ClashCompat.FindTestByName(clash.TestsData, testName);
            if (test == null) return StepResult.Fail(step, $"Test '{testName}' not found");

            var asm = FindAutoNavAssembly();
            if (asm == null) return StepResult.Fail(step, "AutoNAV plugin is not loaded.");

            var grouperType = asm.GetType("AutoNAV.ClashGrouper");
            if (grouperType == null) return StepResult.Fail(step, "AutoNAV.ClashGrouper not found.");

            var primaryEnumType = asm.GetType("AutoNAV.ClashGrouper+PrimaryGroupingMode")
                                ?? asm.GetType("AutoNAV.PrimaryGroupingMode");
            var subEnumType     = asm.GetType("AutoNAV.ClashGrouper+SubGroupingMode")
                                ?? asm.GetType("AutoNAV.SubGroupingMode");

            object primaryVal = primaryEnumType != null
                ? Enum.Parse(primaryEnumType, primaryGroupBy, ignoreCase: true) : (object)0;
            object subVal     = subEnumType != null
                ? Enum.Parse(subEnumType, subGroupBy, ignoreCase: true) : (object)0;

            var groupMethod = grouperType.GetMethod("GroupClashes",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (groupMethod == null) return StepResult.Fail(step, "ClashGrouper.GroupClashes not found.");

            var parms = groupMethod.GetParameters();
            var callArgs = new object[parms.Length];
            callArgs[0] = test;
            if (parms.Length > 1) callArgs[1] = primaryVal;
            if (parms.Length > 2) callArgs[2] = subVal;
            if (parms.Length > 3) callArgs[3] = true;
            if (parms.Length > 4) callArgs[4] = "";

            groupMethod.Invoke(null, callArgs);
            return StepResult.Ok(step, $"Grouped clash test '{testName}'");
        }

        private StepResult ExecAutoNavClashUngroup(MacroStep step, string testName)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");
            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, ex.Message); }

            var test = ClashCompat.FindTestByName(clash.TestsData, testName);
            if (test == null) return StepResult.Fail(step, $"Test '{testName}' not found");

            var asm = FindAutoNavAssembly();
            if (asm == null) return StepResult.Fail(step, "AutoNAV plugin is not loaded.");

            var grouperType = asm.GetType("AutoNAV.ClashGrouper");
            if (grouperType == null) return StepResult.Fail(step, "AutoNAV.ClashGrouper not found.");

            var method = grouperType.GetMethod("UnGroupClashes",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) return StepResult.Fail(step, "ClashGrouper.UnGroupClashes not found.");

            method.Invoke(null, new object[] { test });
            return StepResult.Ok(step, $"Ungrouped test '{testName}'");
        }
    }

    public class StepResult
    {
        public MacroStep Step    { get; private set; }
        public bool      Success { get; private set; }
        public string    Message { get; private set; }

        public static StepResult Ok(MacroStep s, string msg)
            => new StepResult { Step = s, Success = true,  Message = msg };
        public static StepResult Fail(MacroStep s, string msg)
            => new StepResult { Step = s, Success = false, Message = msg };
    }

    internal static class DictExtensions
    {
        public static string Get(this Dictionary<string, string> d, string key)
            => d != null && d.TryGetValue(key, out var v) ? v : null;
    }
}
