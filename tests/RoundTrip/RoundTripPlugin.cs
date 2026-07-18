using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Plugins;
using MacroNAV;
using MacroNAV.Models;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace MacroNAVTests
{
    // Headless record -> save -> reload -> replay -> verify harness.
    // Invoked out-of-process via NavisworksApplication.ExecuteAddInPlugin so the
    // real recorder/player run against a live Navisworks document. Writes a
    // pass/fail report to the path in MACRONAV_TEST_LOG.
    [Plugin("MacroNAVTest", "ACLP_VDC", DisplayName = "MacroNAV Round Trip Test")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class RoundTripPlugin : AddInPlugin
    {
        private readonly StringBuilder _log = new StringBuilder();
        private int _passed, _failed;

        private void Log(string msg) => _log.AppendLine(msg);

        private void Check(string name, bool condition, string detail = "")
        {
            if (condition) { _passed++; Log($"  PASS  {name}"); }
            else           { _failed++; Log($"  FAIL  {name}   {detail}"); }
        }

        public override int Execute(params string[] parameters)
        {
            var logPath = Environment.GetEnvironmentVariable("MACRONAV_TEST_LOG")
                          ?? Path.Combine(Path.GetTempPath(), "macronav-roundtrip.log");
            try { RunSuite(); }
            catch (Exception ex)
            {
                _failed++;
                Log("FATAL: " + ex);
            }
            finally
            {
                Log("");
                Log($"RESULT  passed={_passed}  failed={_failed}");
                try { File.WriteAllText(logPath, _log.ToString()); } catch { }
            }
            return _failed == 0 ? 0 : 1;
        }

        private void RunSuite()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active document");

            Log("=== SETUP ===");
            var nwDir = Path.GetDirectoryName(typeof(NavApp).Assembly.Location);
            var modelA = Path.Combine(nwDir, @"avatars\dummy\Dummy01.nwd");
            var modelB = Path.Combine(nwDir, @"avatars\Construction_Worker\Construction Worker 01.nwd");

            doc.Clear();
            doc.AppendFile(modelA);
            doc.AppendFile(modelB);
            Log($"Models loaded: {doc.Models.Count}");
            Check("two models appended", doc.Models.Count == 2, $"got {doc.Models.Count}");

            // Two disjoint selection sets, one per model, for the clash test.
            CreateSelectionSet(doc, "MacroNAV_SetA", doc.Models[0].RootItem);
            CreateSelectionSet(doc, "MacroNAV_SetB", doc.Models[1].RootItem);
            Check("selection sets created",
                ClashCompat.FindSelectionSetByName(doc, "MacroNAV_SetA") != null &&
                ClashCompat.FindSelectionSetByName(doc, "MacroNAV_SetB") != null);

            // ---------------------------------------------------------------
            Log("");
            Log("=== RECORD ===");
            var recorder = new MacroRecorder();
            recorder.AutoCapture = false;   // drive captures explicitly
            recorder.StartRecording();

            var homePos = doc.CurrentViewpoint.Value.Position;
            var vpStep = recorder.CaptureCurrentViewpoint("VP-Home");
            Log($"Captured viewpoint at ({homePos.X:F3}, {homePos.Y:F3}, {homePos.Z:F3})");
            Check("viewpoint step captured", vpStep != null);
            Check("viewpoint step carries Camera blob",
                vpStep != null && vpStep.Parameters.ContainsKey("Camera")
                && !string.IsNullOrEmpty(vpStep.Parameters["Camera"]));

            recorder.CaptureComment("round trip marker");

            // Clash test config + run, wired to the two sets.
            var createStep = new MacroStep
            {
                StepType = MacroStepType.ClashCreateTest,
                DisplayName = "Create MacroNAV_Test",
                Parameters = new Dictionary<string, string>
                {
                    ["TestName"]   = "MacroNAV_Test",
                    ["SelectionA"] = "MacroNAV_SetA",
                    ["SelectionB"] = "MacroNAV_SetB",
                    ["Tolerance"]  = "0.0010",
                }
            };
            recorder.InsertStep(recorder.Steps.Count, createStep);
            recorder.CaptureRunClashTest("MacroNAV_Test");
            recorder.CaptureSearchSetActivate("MacroNAV_SetA");
            recorder.StopRecording();

            Log($"Recorded {recorder.Steps.Count} steps");
            Check("recorded 5 steps", recorder.Steps.Count == 5, $"got {recorder.Steps.Count}");

            // ---------------------------------------------------------------
            Log("");
            Log("=== SAVE / RELOAD (JSON round trip) ===");
            var macro = new Macro { Name = "RoundTrip", Steps = recorder.Steps.ToList() };
            var library = new MacroLibrary();
            library.Load();
            library.Delete(macro.Id);
            library.AddOrUpdate(macro);

            var reloaded = new MacroLibrary();
            reloaded.Load();
            var back = reloaded.Macros.FirstOrDefault(m => m.Id == macro.Id);
            Check("macro persisted and reloaded", back != null);
            Check("step count survived JSON",
                back != null && back.Steps.Count == macro.Steps.Count,
                $"got {back?.Steps.Count}");

            var backVp = back?.Steps.FirstOrDefault(s => s.StepType == MacroStepType.ViewpointActivate);
            Check("Camera blob survived JSON",
                backVp != null && backVp.Parameters.ContainsKey("Camera")
                && backVp.Parameters["Camera"] == vpStep.Parameters["Camera"]);

            // ---------------------------------------------------------------
            Log("");
            Log("=== PERTURB ===");
            var moved = doc.CurrentViewpoint.CreateCopy();
            moved.Position = new Point3D(homePos.X + 500.0, homePos.Y + 500.0, homePos.Z + 500.0);
            doc.CurrentViewpoint.CopyFrom(moved);
            doc.CurrentSelection.Clear();
            var movedPos = doc.CurrentViewpoint.Value.Position;
            Log($"Camera moved to ({movedPos.X:F3}, {movedPos.Y:F3}, {movedPos.Z:F3})");
            Check("camera actually moved away",
                Distance(movedPos, homePos) > 1.0, $"delta={Distance(movedPos, homePos):F3}");

            // ---------------------------------------------------------------
            Log("");
            Log("=== REPLAY ===");
            var player = new MacroPlayer();
            var results = new List<StepResult>();
            player.StepCompleted += (s, r) => results.Add(r);

            // PlayAsync is async but every step body is synchronous work marshalled
            // on this thread; block here since the harness owns the main thread.
            player.PlayAsync(back.Steps, stopOnError: false).GetAwaiter().GetResult();

            foreach (var r in results)
                Log($"  [{(r.Success ? "ok  " : "FAIL")}] {r.Step.StepType,-20} {r.Message}");

            Check("all steps reported success",
                results.Count > 0 && results.All(r => r.Success),
                string.Join("; ", results.Where(r => !r.Success).Select(r => r.Message)));

            // ---------------------------------------------------------------
            Log("");
            Log("=== VERIFY ===");
            var restored = doc.CurrentViewpoint.Value.Position;
            var drift = Distance(restored, homePos);
            Log($"Camera restored to ({restored.X:F3}, {restored.Y:F3}, {restored.Z:F3}), drift={drift:F6}");
            Check("viewpoint restored to captured camera", drift < 0.001, $"drift={drift:F6}");

            var clash = doc.GetClash();
            var test = ClashCompat.FindTestByName(clash.TestsData, "MacroNAV_Test");
            Check("clash test created by replay", test != null);

            if (test != null)
            {
                // The key check on the rewritten SelectionSource plumbing: names
                // written by the player must read back through the recorder path.
                var selA = ClashCompat.SerialiseSelectionNames(test.SelectionA, doc);
                var selB = ClashCompat.SerialiseSelectionNames(test.SelectionB, doc);
                Log($"SelectionA resolves to '{selA}'");
                Log($"SelectionB resolves to '{selB}'");
                Check("SelectionA round-tripped", selA == "MacroNAV_SetA", $"got '{selA}'");
                Check("SelectionB round-tripped", selB == "MacroNAV_SetB", $"got '{selB}'");
                Log($"Test status={test.Status}, lastRun={test.LastRun}");
                Check("clash test actually ran", test.LastRun.HasValue, $"status={test.Status}");
            }

            var sel = doc.CurrentSelection.SelectedItems;
            Log($"Current selection count: {sel?.Count ?? 0}");
            Check("selection set activated by replay", sel != null && sel.Count > 0);

            // The library lives in the user's real %AppData%; don't leave test
            // macros behind in it.
            Log("");
            Log("=== CLEANUP ===");
            var cleanup = new MacroLibrary();
            cleanup.Load();
            foreach (var stale in cleanup.Macros.Where(m => m.Name == "RoundTrip").ToList())
                cleanup.Delete(stale.Id);
            var verify = new MacroLibrary();
            verify.Load();
            var leftover = verify.Macros.Count(m => m.Name == "RoundTrip");
            Check("test macros removed from library", leftover == 0, $"{leftover} left");
        }

        private static double Distance(Point3D a, Point3D b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static void CreateSelectionSet(Document doc, string name, ModelItem root)
        {
            var items = new ModelItemCollection();
            items.Add(root);
            using (doc.BeginTransaction("MacroNAV test: create set"))
            {
                var set = new SelectionSet(items) { DisplayName = name };
                doc.SelectionSets.AddCopy(set);
            }
        }
    }
}
