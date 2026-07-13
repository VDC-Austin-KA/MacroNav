using System;
using System.Collections.Generic;
using System.Linq;
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
        public event EventHandler<MacroStep> StepStarted;
        public event EventHandler<StepResult> StepCompleted;
        public event EventHandler PlaybackCompleted;

        private CancellationTokenSource _cts;
        public bool IsPlaying => _cts != null && !_cts.IsCancellationRequested;

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

        // ── Dispatch ──────────────────────────────────────────────────────────

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

                    case MacroStepType.ClashCreateTest:    return ExecClashCreateTest(step);
                    case MacroStepType.ClashRunTest:       return ExecClashRunTest(step);
                    case MacroStepType.ClashRunAllTests:   return ExecClashRunAllTests(step);
                    case MacroStepType.ViewpointActivate:  return ExecViewpointActivate(step);
                    case MacroStepType.SearchSetActivate:  return ExecSearchSetActivate(step);

                    case MacroStepType.AutoNavSearchSetGen:
                    case MacroStepType.AutoNavClashTestGen:
                        return StepResult.Ok(step, "AutoNAV documentation step — open AutoNAV to execute.");

                    default:
                        return StepResult.Ok(step, $"Step type {step.StepType} acknowledged.");
                }
            }
            catch (OperationCanceledException) { return StepResult.Fail(step, "Cancelled"); }
            catch (Exception ex)               { return StepResult.Fail(step, ex.Message); }
        }

        // ── Clash Detective ───────────────────────────────────────────────────

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
                // Find existing test or create a new one.
                var dct  = clash.TestsData;
                var test = ClashCompat.FindTestByName(dct, testName)
                           ?? ClashCompat.AddNewTest(dct);

                test.DisplayName = testName;

                if (double.TryParse(tolStr, out double tol))
                    test.Tolerance = tol;

                if (!string.IsNullOrEmpty(selAStr))
                    ClashCompat.ApplySelectionSetNames(test.SelectionA, doc,
                        selAStr.Split('|'));

                if (!string.IsNullOrEmpty(selBStr))
                    ClashCompat.ApplySelectionSetNames(test.SelectionB, doc,
                        selBStr.Split('|'));
            }

            return StepResult.Ok(step, $"Clash test '{testName}' configured");
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

            // Mark the specific test as dirty so RunAllTests re-runs only it.
            using (doc.BeginTransaction("MacroNAV: Queue Clash Test"))
                test.TestStatus = ClashTestStatus.New;

            clash.TestsData.RunAllTests();
            return StepResult.Ok(step, $"Ran clash test '{testName}'");
        }

        private StepResult ExecClashRunAllTests(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");

            DocumentClash clash;
            try { clash = doc.GetClash(); }
            catch (Exception ex) { return StepResult.Fail(step, ex.Message); }

            clash.TestsData.RunAllTests();
            return StepResult.Ok(step, "All clash tests run");
        }

        // ── Viewpoints ────────────────────────────────────────────────────────

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

            // Restore from raw stored position.
            if (!step.Parameters.TryGetValue("PosX", out string pxStr))
                return StepResult.Fail(step, "No position data stored in step");

            double px = double.Parse(pxStr);
            double py = double.Parse(step.Parameters["PosY"]);
            double pz = double.Parse(step.Parameters["PosZ"]);
            double lx = double.Parse(step.Parameters.Get("LookX") ?? "0");
            double ly = double.Parse(step.Parameters.Get("LookY") ?? "1");
            double lz = double.Parse(step.Parameters.Get("LookZ") ?? "0");
            double ux = double.Parse(step.Parameters.Get("UpX")   ?? "0");
            double uy = double.Parse(step.Parameters.Get("UpY")   ?? "0");
            double uz = double.Parse(step.Parameters.Get("UpZ")   ?? "1");

            var vp = doc.CurrentViewpoint.CreateCopy();
            vp.Position      = new Point3D(px, py, pz);
            vp.AlignDirection = new Vector3D(lx, ly, lz);
            vp.AlignUp        = new Vector3D(ux, uy, uz);

            if (step.Parameters.TryGetValue("Fov", out string fovStr)
                && double.TryParse(fovStr, out double fov))
                vp.FieldOfView = fov;

            doc.CurrentViewpoint.CopyFrom(vp);
            return StepResult.Ok(step, "Viewpoint restored");
        }

        // ── Selection Sets ────────────────────────────────────────────────────

        private StepResult ExecSearchSetActivate(MacroStep step)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return StepResult.Fail(step, "No active document");

            var name = step.Parameters.Get("Name");
            var ss   = ClashCompat.FindSelectionSetByName(doc, name);
            if (ss == null) return StepResult.Fail(step, $"Selection set '{name}' not found");

            doc.CurrentSelection.CopyFrom(ss.GetSelection());
            return StepResult.Ok(step, $"Activated selection set '{name}'");
        }
    }

    // ── Result ────────────────────────────────────────────────────────────────

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

    // ── Dictionary helper ─────────────────────────────────────────────────────

    internal static class DictExtensions
    {
        public static string Get(this Dictionary<string, string> d, string key)
            => d != null && d.TryGetValue(key, out var v) ? v : null;
    }
}
