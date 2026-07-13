using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using MacroNAV.Models;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace MacroNAV
{
    public class MacroRecorder
    {
        private bool _isRecording;
        private readonly List<MacroStep> _steps = new List<MacroStep>();

        public bool IsRecording => _isRecording;
        public IReadOnlyList<MacroStep> Steps => _steps.AsReadOnly();

        public event EventHandler<MacroStep> StepAdded;
        public event EventHandler RecordingStarted;
        public event EventHandler RecordingStopped;

        // ── Recording lifecycle ───────────────────────────────────────────────

        public void StartRecording()
        {
            _isRecording = true;
            SubscribeEvents();
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }

        public void StopRecording()
        {
            _isRecording = false;
            UnsubscribeEvents();
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSteps() => _steps.Clear();

        // ── Navisworks event hooks ────────────────────────────────────────────
        // These fire automatically during a recording session. They currently
        // serve as entry points for future auto-detection; capture is driven
        // explicitly by the Quick Capture panel buttons.

        private void SubscribeEvents()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return;
            try { doc.SelectionSets.Changed += OnSelectionSetsChanged; } catch { }
            try { doc.SavedViewpoints.Changed += OnSavedViewpointsChanged; } catch { }
        }

        private void UnsubscribeEvents()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return;
            try { doc.SelectionSets.Changed -= OnSelectionSetsChanged; } catch { }
            try { doc.SavedViewpoints.Changed -= OnSavedViewpointsChanged; } catch { }
        }

        private void OnSelectionSetsChanged(object sender, EventArgs e) { }
        private void OnSavedViewpointsChanged(object sender, EventArgs e) { }

        // ── Manual capture: Clash Detective ───────────────────────────────────

        public MacroStep CaptureClashTestConfig(string testName)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return null;

            string selA = string.Empty, selB = string.Empty,
                   tol = "0.0010", type = "HardClash";

            try
            {
                var clash = doc.GetClash();
                var test = ClashCompat.FindTestByName(clash.TestsData, testName);
                if (test != null)
                {
                    selA = ClashCompat.SerialiseSelectionNames(test.SelectionA);
                    selB = ClashCompat.SerialiseSelectionNames(test.SelectionB);
                    tol  = test.Tolerance.ToString("F4");
                    type = test.Type.ToString();
                }
            }
            catch { /* clash module may not be loaded */ }

            return AddStep(new MacroStep
            {
                StepType    = MacroStepType.ClashCreateTest,
                DisplayName = $"Configure Clash Test: {testName}",
                Parameters  = new Dictionary<string, string>
                {
                    ["TestName"]   = testName,
                    ["SelectionA"] = selA,
                    ["SelectionB"] = selB,
                    ["Tolerance"]  = tol,
                    ["Type"]       = type,
                }
            });
        }

        public MacroStep CaptureRunClashTest(string testName) => AddStep(new MacroStep
        {
            StepType    = MacroStepType.ClashRunTest,
            DisplayName = $"Run Clash Test: {testName}",
            Parameters  = new Dictionary<string, string> { ["TestName"] = testName }
        });

        public MacroStep CaptureRunAllClashTests() => AddStep(new MacroStep
        {
            StepType    = MacroStepType.ClashRunAllTests,
            DisplayName = "Run All Clash Tests"
        });

        // ── Manual capture: Viewpoints ────────────────────────────────────────

        public MacroStep CaptureCurrentViewpoint(string name = null)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return null;

            var pos  = doc.CurrentViewpoint.Position;
            var look = doc.CurrentViewpoint.AlignDirection;
            var up   = doc.CurrentViewpoint.AlignUp;
            var vpName = name ?? $"Viewpoint {DateTime.Now:HH:mm:ss}";

            return AddStep(new MacroStep
            {
                StepType    = MacroStepType.ViewpointActivate,
                DisplayName = $"Go to: {vpName}",
                Parameters  = new Dictionary<string, string>
                {
                    ["Name"]    = vpName,
                    ["UseSaved"] = "false",
                    ["PosX"]  = pos.X.ToString("F6"),
                    ["PosY"]  = pos.Y.ToString("F6"),
                    ["PosZ"]  = pos.Z.ToString("F6"),
                    ["LookX"] = look.X.ToString("F6"),
                    ["LookY"] = look.Y.ToString("F6"),
                    ["LookZ"] = look.Z.ToString("F6"),
                    ["UpX"]   = up.X.ToString("F6"),
                    ["UpY"]   = up.Y.ToString("F6"),
                    ["UpZ"]   = up.Z.ToString("F6"),
                    ["Fov"]   = doc.CurrentViewpoint.FieldOfView.ToString("F4"),
                }
            });
        }

        public MacroStep CaptureActivateSavedViewpoint(string vpName) => AddStep(new MacroStep
        {
            StepType    = MacroStepType.ViewpointActivate,
            DisplayName = $"Activate Viewpoint: {vpName}",
            Parameters  = new Dictionary<string, string>
            {
                ["Name"]     = vpName,
                ["UseSaved"] = "true"
            }
        });

        // ── Manual capture: Selection Sets ────────────────────────────────────

        public MacroStep CaptureSearchSetActivate(string name) => AddStep(new MacroStep
        {
            StepType    = MacroStepType.SearchSetActivate,
            DisplayName = $"Activate Selection Set: {name}",
            Parameters  = new Dictionary<string, string> { ["Name"] = name }
        });

        // ── Manual capture: Misc ──────────────────────────────────────────────

        public MacroStep CaptureComment(string text) => AddStep(new MacroStep
        {
            StepType    = MacroStepType.Comment,
            DisplayName = $"// {text}",
            Parameters  = new Dictionary<string, string> { ["Text"] = text }
        });

        public MacroStep CaptureDelay(int milliseconds) => AddStep(new MacroStep
        {
            StepType    = MacroStepType.Delay,
            DisplayName = $"Wait {milliseconds}ms",
            Parameters  = new Dictionary<string, string>
                { ["Milliseconds"] = milliseconds.ToString() }
        });

        public MacroStep CaptureAutoNavSearchSetGen(string discipline, string method)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavSearchSetGen,
                DisplayName = $"AutoNAV: Generate Search Sets ({discipline})",
                Parameters  = new Dictionary<string, string>
                    { ["Discipline"] = discipline, ["Method"] = method }
            });

        public MacroStep CaptureAutoNavClashTestGen(string disciplines)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavClashTestGen,
                DisplayName = "AutoNAV: Generate Clash Tests",
                Parameters  = new Dictionary<string, string>
                    { ["Disciplines"] = disciplines }
            });

        // ── Step management ───────────────────────────────────────────────────

        public void InsertStep(int index, MacroStep step)
        {
            _steps.Insert(Math.Max(0, Math.Min(index, _steps.Count)), step);
            StepAdded?.Invoke(this, step);
        }

        public void RemoveStep(MacroStep step) => _steps.Remove(step);

        public void MoveStep(int from, int to)
        {
            if (from < 0 || from >= _steps.Count) return;
            to = Math.Max(0, Math.Min(to, _steps.Count - 1));
            var s = _steps[from];
            _steps.RemoveAt(from);
            _steps.Insert(to, s);
        }

        public List<MacroStep> GetStepsCopy() => new List<MacroStep>(_steps);

        // ── Internal ──────────────────────────────────────────────────────────

        private MacroStep AddStep(MacroStep step)
        {
            _steps.Add(step);
            StepAdded?.Invoke(this, step);
            return step;
        }
    }
}
