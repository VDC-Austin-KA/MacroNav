using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using MacroNAV;
using MacroNAV.Models;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace MacroNAVTests
{
    // Diagnoses auto-capture: drives real document changes while recording and
    // reports which ones the recorder actually noticed. Written because
    // auto-capture recorded one selection set and then went silent.
    [Plugin("MacroNAVDiag", "ACLP_VDC", DisplayName = "MacroNAV Recorder Diagnostics")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class RecorderDiagnosticsPlugin : AddInPlugin
    {
        private readonly StringBuilder _log = new StringBuilder();
        private void Log(string m) => _log.AppendLine(m);

        public override int Execute(params string[] parameters)
        {
            var logPath = Environment.GetEnvironmentVariable("MACRONAV_DIAG_LOG")
                          ?? Path.Combine(Path.GetTempPath(), "macronav-diag.log");
            try { Diagnose(); }
            catch (Exception ex) { Log("FATAL: " + ex); }
            finally { try { File.WriteAllText(logPath, _log.ToString()); } catch { } }
            return 0;
        }

        private void Diagnose()
        {
            var doc = NavApp.ActiveDocument;
            var nwDir = Path.GetDirectoryName(typeof(NavApp).Assembly.Location);
            doc.Clear();
            doc.AppendFile(Path.Combine(nwDir, @"avatars\dummy\Dummy01.nwd"));

            // --- Which MacroNAV.dll actually got loaded? -----------------------
            // Plugins\MacroNAV and the harness folder both ship MacroNAV.dll and
            // share an assembly name, so the first one loaded wins. Testing a
            // stale installed copy while believing it is the fresh build is a
            // silent, very misleading failure -- surface it.
            Log("=== LOADED ASSEMBLY ===");
            var macroAsm = typeof(MacroRecorder).Assembly;
            Log("MacroNAV.dll : " + macroAsm.Location);
            try { Log("  built      : " + File.GetLastWriteTime(macroAsm.Location)); } catch { }
            Log("  has RecorderLog (post-hardening build): " +
                (macroAsm.GetType("MacroNAV.RecorderLog") != null));

            // --- Environment facts the recorder depends on ---------------------
            Log("");
            Log("=== ENVIRONMENT ===");
            Log("WPF Application.Current is null : " +
                (System.Windows.Application.Current == null));
            Log("  (SnapCurrentViewpoint returns early when this is true)");

            var s1 = doc.SelectionSets;
            var s2 = doc.SelectionSets;
            Log("doc.SelectionSets same instance across calls : " + ReferenceEquals(s1, s2));
            var v1 = doc.SavedViewpoints;
            var v2 = doc.SavedViewpoints;
            Log("doc.SavedViewpoints same instance across calls: " + ReferenceEquals(v1, v2));
            Log("  (if False, event subscriptions attach to a temporary that can be collected)");

            // --- Do the events fire at all? -----------------------------------
            Log("");
            Log("=== RAW EVENT FIRING ===");
            int rawSetChanged = 0, rawVpChanged = 0;
            EventHandler<SavedItemChangedEventArgs> setH = (s, e) => rawSetChanged++;
            EventHandler<SavedItemChangedEventArgs> vpH = (s, e) => rawVpChanged++;
            doc.SelectionSets.Changed += setH;
            doc.SavedViewpoints.Changed += vpH;

            for (int i = 1; i <= 3; i++) MakeSet(doc, "DIAG_Raw_" + i);
            for (int i = 1; i <= 2; i++) MakeViewpoint(doc, "DIAG_RawVP_" + i);

            Log($"created 3 sets  -> SelectionSets.Changed fired {rawSetChanged}x");
            Log($"created 2 vpts  -> SavedViewpoints.Changed fired {rawVpChanged}x");
            doc.SelectionSets.Changed -= setH;
            doc.SavedViewpoints.Changed -= vpH;

            // Does unsubscribing off a *different* wrapper instance actually work?
            int afterUnsub = rawSetChanged;
            MakeSet(doc, "DIAG_AfterUnsub");
            Log($"after -= , creating another set fired {rawSetChanged - afterUnsub}x " +
                "(expect 0; nonzero means -= failed to detach)");

            // --- What does the recorder capture? ------------------------------
            Log("");
            Log("=== RECORDER AUTO-CAPTURE ===");
            var rec = new MacroRecorder();
            var captured = new List<MacroStep>();
            rec.StepAdded += (s, step) => captured.Add(step);
            rec.StartRecording();
            Log("IsRecording=" + rec.IsRecording + ", AutoCapture=" + rec.AutoCapture);

            for (int i = 1; i <= 5; i++)
            {
                MakeSet(doc, "DIAG_Set_" + i);
                Log($"  after creating DIAG_Set_{i}: recorder has {rec.Steps.Count} step(s)");
            }
            for (int i = 1; i <= 3; i++)
            {
                MakeViewpoint(doc, "DIAG_VP_" + i);
                Log($"  after saving DIAG_VP_{i}: recorder has {rec.Steps.Count} step(s)");
            }

            // Selection changes -> should produce SearchSetActivate steps.
            var set1 = ClashCompat.FindSelectionSetByName(doc, "DIAG_Set_1");
            var set2 = ClashCompat.FindSelectionSetByName(doc, "DIAG_Set_2");
            if (set1 != null) { doc.CurrentSelection.CopyFrom(set1.GetSelectedItems()); Log("  selected DIAG_Set_1 -> " + rec.Steps.Count); }
            if (set2 != null) { doc.CurrentSelection.CopyFrom(set2.GetSelectedItems()); Log("  selected DIAG_Set_2 -> " + rec.Steps.Count); }

            rec.StopRecording();

            Log("");
            Log($"TOTAL CAPTURED: {rec.Steps.Count} (expected 8 creations + selections)");
            foreach (var s in rec.Steps) Log($"   {s.StepType,-20} {s.DisplayName}");

            // --- Reproduce the reported failure -------------------------------
            // A StepAdded subscriber that throws (the real window does UI work
            // there) must not stop the recorder from capturing the rest.
            Log("");
            Log("=== SUBSCRIBER THROWS (reproduces 'only one step recorded') ===");
            RemoveDiagItems(doc);
            var rec2 = new MacroRecorder();
            rec2.StepAdded += (s, step) => throw new InvalidOperationException("simulated UI failure");
            rec2.StartRecording();
            // One transaction creating several sets = one Changed event, which is
            // what AutoNAV does when it generates discipline worksets.
            MakeSetsInOneTransaction(doc, new[] { "DIAG_Bulk_1", "DIAG_Bulk_2", "DIAG_Bulk_3", "DIAG_Bulk_4" });
            rec2.StopRecording();
            Log($"created 4 sets in ONE transaction -> recorder captured {rec2.Steps.Count} (expect 4)");
            foreach (var s in rec2.Steps) Log($"   {s.DisplayName}");

            // --- Does playback actually DO anything? --------------------------
            // Replays each step type and reports the result, so "reports success
            // but changes nothing" cannot hide.
            Log("");
            Log("=== PLAYBACK REALITY CHECK ===");
            var probe = new List<MacroStep>
            {
                new MacroStep { StepType = MacroStepType.SearchSetCreate,
                    DisplayName = "create set", Parameters = new Dictionary<string,string>{["Name"]="DIAG_X"} },
                new MacroStep { StepType = MacroStepType.ClashAssignStatus,
                    DisplayName = "assign status", Parameters = new Dictionary<string,string>() },
                new MacroStep { StepType = MacroStepType.AutoNavSearchSetGen,
                    DisplayName = "legacy generate search sets", Parameters = new Dictionary<string,string>() },
            };
            var probePlayer = new MacroPlayer();
            var probeResults = new List<StepResult>();
            probePlayer.StepCompleted += (s, r) => probeResults.Add(r);
            probePlayer.PlayAsync(probe, stopOnError: false).GetAwaiter().GetResult();
            foreach (var r in probeResults)
                Log($"  [{(r.Success ? "ok  " : "FAIL")}] {r.Step.StepType,-22} {r.Message}");

            var noop = probeResults.Take(2).Count(r => r.Success);
            Log(noop == 0
                ? "  PASS  unimplemented steps now fail instead of reporting success"
                : $"  FAIL  {noop} unimplemented step(s) still reported success");

            Log("");
            Log("=== CLEANUP ===");
            RemoveDiagItems(doc);
        }

        private static void MakeSet(Document doc, string name)
        {
            var items = new ModelItemCollection();
            items.Add(doc.Models[0].RootItem);
            using (doc.BeginTransaction("diag: set " + name))
            {
                var set = new SelectionSet(items) { DisplayName = name };
                doc.SelectionSets.AddCopy(set);
            }
        }

        // Mirrors how a generator plugin creates many sets at once: a single
        // transaction, so the document raises one Changed event for the batch.
        private static void MakeSetsInOneTransaction(Document doc, string[] names)
        {
            var items = new ModelItemCollection();
            items.Add(doc.Models[0].RootItem);
            using (doc.BeginTransaction("diag: bulk sets"))
            {
                foreach (var name in names)
                    doc.SelectionSets.AddCopy(new SelectionSet(items) { DisplayName = name });
            }
        }

        private static void MakeViewpoint(Document doc, string name)
        {
            using (doc.BeginTransaction("diag: vp " + name))
            {
                var vp = new SavedViewpoint(doc.CurrentViewpoint.CreateCopy()) { DisplayName = name };
                doc.SavedViewpoints.AddCopy(vp);
            }
        }

        private static void RemoveDiagItems(Document doc)
        {
            using (doc.BeginTransaction("diag: cleanup"))
            {
                foreach (var item in doc.SelectionSets.RootItem.Children
                             .OfType<SavedItem>().Where(i => i.DisplayName != null
                                 && i.DisplayName.StartsWith("DIAG_")).ToList())
                    doc.SelectionSets.Remove(item);
                foreach (var item in doc.SavedViewpoints.RootItem.Children
                             .OfType<SavedItem>().Where(i => i.DisplayName != null
                                 && i.DisplayName.StartsWith("DIAG_")).ToList())
                    doc.SavedViewpoints.Remove(item);
            }
        }
    }
}
