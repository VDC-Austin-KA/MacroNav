using System;
using System.Collections.Generic;
using System.Timers;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using MacroNAV.Models;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace MacroNAV
{
    public class MacroRecorder
    {
        private bool _isRecording;
        private bool _autoCapture = true;
        private readonly List<MacroStep> _steps = new List<MacroStep>();

        private readonly Timer _vpDebounce;
        private const int VpDebounceMs = 800;

        private string _lastSelectionSetName;

        // Snapshots of what existed when recording started, so the *.Changed
        // events can tell an addition apart from a rename/remove/reorder.
        private readonly HashSet<string> _knownSetNames = new HashSet<string>();
        private readonly HashSet<string> _knownVpNames  = new HashSet<string>();

        public bool IsRecording   => _isRecording;
        public bool AutoCapture   { get => _autoCapture; set => _autoCapture = value; }
        public IReadOnlyList<MacroStep> Steps => _steps.AsReadOnly();

        public event EventHandler<MacroStep> StepAdded;
        public event EventHandler RecordingStarted;
        public event EventHandler RecordingStopped;

        public MacroRecorder()
        {
            _vpDebounce = new Timer(VpDebounceMs) { AutoReset = false };
            _vpDebounce.Elapsed += (_, __) => SnapCurrentViewpoint();
        }

        public void StartRecording()
        {
            _isRecording = true;
            _lastSelectionSetName = null;
            SnapshotExistingNames();
            SubscribeEvents();
            AutoNavBridge.Register(this);
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }

        public void StopRecording()
        {
            _isRecording = false;
            _vpDebounce.Stop();
            UnsubscribeEvents();
            AutoNavBridge.Unregister();
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSteps() => _steps.Clear();

        private void SubscribeEvents()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return;
            try { doc.SelectionSets.Changed         += OnSelectionSetsChanged;    } catch { }
            try { doc.SavedViewpoints.Changed        += OnSavedViewpointsChanged; } catch { }
            try { doc.CurrentSelection.Changed       += OnCurrentSelectionChanged; } catch { }
            try { doc.Models.CollectionChanged       += OnModelsChanged;           } catch { }
        }

        private void UnsubscribeEvents()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return;
            try { doc.SelectionSets.Changed         -= OnSelectionSetsChanged;    } catch { }
            try { doc.SavedViewpoints.Changed        -= OnSavedViewpointsChanged; } catch { }
            try { doc.CurrentSelection.Changed       -= OnCurrentSelectionChanged; } catch { }
            try { doc.Models.CollectionChanged       -= OnModelsChanged;           } catch { }
        }

        // Populate the "known" name sets from the current document so that
        // subsequent Changed events can detect genuinely new items.
        private void SnapshotExistingNames()
        {
            _knownSetNames.Clear();
            _knownVpNames.Clear();
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                foreach (var n in EnumerateNames(doc.SelectionSets.RootItem, isViewpoint: false))
                    _knownSetNames.Add(n);
                foreach (var n in EnumerateNames(doc.SavedViewpoints.RootItem, isViewpoint: true))
                    _knownVpNames.Add(n);
            }
            catch { }
        }

        private static IEnumerable<string> EnumerateNames(GroupItem root, bool isViewpoint)
        {
            var results = new List<string>();
            void Walk(GroupItem group)
            {
                foreach (SavedItem item in group.Children)
                {
                    if (isViewpoint)
                    {
                        if (item is SavedViewpoint vp) results.Add(vp.DisplayName);
                    }
                    else
                    {
                        if (item is SelectionSet ss) results.Add(ss.DisplayName);
                    }
                    if (item is GroupItem g) Walk(g);
                }
            }
            try { Walk(root); } catch { }
            return results;
        }

        // A selection set was created / renamed / removed. Capture any that are
        // new since the last snapshot, then refresh the snapshot.
        private void OnSelectionSetsChanged(object sender, EventArgs e)
        {
            if (!_autoCapture || !_isRecording) return;
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                foreach (var name in EnumerateNames(doc.SelectionSets.RootItem, isViewpoint: false))
                {
                    if (!_knownSetNames.Contains(name))
                    {
                        _knownSetNames.Add(name);
                        AddStep(new MacroStep
                        {
                            StepType    = MacroStepType.SearchSetActivate,
                            DisplayName = $"[Auto] Selection set created: {name}",
                            Description = "Re-selects this named set on playback (must exist in the target model).",
                            Parameters  = new Dictionary<string, string> { ["Name"] = name }
                        });
                    }
                }
            }
            catch { }
        }

        // A saved viewpoint was created / renamed / removed. Capture any new ones
        // as an "activate saved viewpoint" step (replayable on the same model).
        private void OnSavedViewpointsChanged(object sender, EventArgs e)
        {
            if (!_autoCapture || !_isRecording) return;
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                foreach (var name in EnumerateNames(doc.SavedViewpoints.RootItem, isViewpoint: true))
                {
                    if (!_knownVpNames.Contains(name))
                    {
                        _knownVpNames.Add(name);
                        AddStep(new MacroStep
                        {
                            StepType    = MacroStepType.ViewpointActivate,
                            DisplayName = $"[Auto] Saved viewpoint: {name}",
                            Parameters  = new Dictionary<string, string>
                                { ["Name"] = name, ["UseSaved"] = "true" }
                        });
                    }
                }
            }
            catch { }
        }

        private void OnCurrentSelectionChanged(object sender, EventArgs e)
        {
            if (!_autoCapture || !_isRecording) return;
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                string matchedName = FindMatchingSelectionSetName(doc);
                if (matchedName != null && matchedName != _lastSelectionSetName)
                {
                    _lastSelectionSetName = matchedName;
                    AddStep(new MacroStep
                    {
                        StepType    = MacroStepType.SearchSetActivate,
                        DisplayName = $"[Auto] Activated: {matchedName}",
                        Parameters  = new Dictionary<string, string> { ["Name"] = matchedName }
                    });
                }
            }
            catch { }
        }

        private void OnModelsChanged(object sender, EventArgs e)
        {
            if (!_autoCapture || !_isRecording) return;
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                string title = doc.Title ?? "Unknown";
                AddStep(new MacroStep
                {
                    StepType    = MacroStepType.FileOpen,
                    DisplayName = $"[Auto] Model changed: {title}",
                    Parameters  = new Dictionary<string, string> { ["Path"] = title }
                });
            }
            catch { }
        }

        private void SnapCurrentViewpoint()
        {
            if (!_isRecording) return;
            try { NavApp.ActiveDocument?.Dispatcher.Invoke(() => CaptureCurrentViewpoint("[Auto]")); }
            catch { }
        }

        private static string FindMatchingSelectionSetName(Document doc)
        {
            try
            {
                var current = doc.CurrentSelection.SelectedItems;
                if (current == null || current.IsEmpty) return null;
                foreach (SavedItem item in doc.SelectionSets.RootItem.Children)
                {
                    if (item is SelectionSet ss)
                    {
                        var sel = ss.GetSelection();
                        if (SelectionsEqual(current, sel)) return ss.DisplayName;
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool SelectionsEqual(ModelItemCollection a, ModelItemCollection b)
        {
            if (a.Count != b.Count) return false;
            var setA = new HashSet<string>();
            foreach (var item in a) setA.Add(item.InstanceGuid.ToString());
            foreach (var item in b)
                if (!setA.Contains(item.InstanceGuid.ToString())) return false;
            return true;
        }

        public MacroStep CaptureClashTestConfig(string testName)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return null;
            string selA = string.Empty, selB = string.Empty, tol = "0.0010", type = "HardClash";
            try
            {
                var clash = doc.GetClash();
                var test  = ClashCompat.FindTestByName(clash.TestsData, testName);
                if (test != null)
                {
                    selA = ClashCompat.SerialiseSelectionNames(test.SelectionA);
                    selB = ClashCompat.SerialiseSelectionNames(test.SelectionB);
                    tol  = test.Tolerance.ToString("F4");
                    type = test.Type.ToString();
                }
            }
            catch { }
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

        public MacroStep CaptureCurrentViewpoint(string label = null)
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) return null;
            var pos  = doc.CurrentViewpoint.Position;
            var look = doc.CurrentViewpoint.AlignDirection;
            var up   = doc.CurrentViewpoint.AlignUp;
            var name = label ?? $"Viewpoint {DateTime.Now:HH:mm:ss}";
            return AddStep(new MacroStep
            {
                StepType    = MacroStepType.ViewpointActivate,
                DisplayName = $"Go to: {name}",
                Parameters  = new Dictionary<string, string>
                {
                    ["Name"]     = name,
                    ["UseSaved"] = "false",
                    ["PosX"]     = pos.X.ToString("F6"),
                    ["PosY"]     = pos.Y.ToString("F6"),
                    ["PosZ"]     = pos.Z.ToString("F6"),
                    ["LookX"]    = look.X.ToString("F6"),
                    ["LookY"]    = look.Y.ToString("F6"),
                    ["LookZ"]    = look.Z.ToString("F6"),
                    ["UpX"]      = up.X.ToString("F6"),
                    ["UpY"]      = up.Y.ToString("F6"),
                    ["UpZ"]      = up.Z.ToString("F6"),
                    ["Fov"]      = doc.CurrentViewpoint.FieldOfView.ToString("F4"),
                }
            });
        }

        public MacroStep CaptureActivateSavedViewpoint(string vpName) => AddStep(new MacroStep
        {
            StepType    = MacroStepType.ViewpointActivate,
            DisplayName = $"Activate Viewpoint: {vpName}",
            Parameters  = new Dictionary<string, string> { ["Name"] = vpName, ["UseSaved"] = "true" }
        });

        public MacroStep CaptureSearchSetActivate(string name) => AddStep(new MacroStep
        {
            StepType    = MacroStepType.SearchSetActivate,
            DisplayName = $"Activate Selection Set: {name}",
            Parameters  = new Dictionary<string, string> { ["Name"] = name }
        });

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
            Parameters  = new Dictionary<string, string> { ["Milliseconds"] = milliseconds.ToString() }
        });

        public MacroStep CaptureAutoNavFunction1() => AddStep(new MacroStep
        {
            StepType    = MacroStepType.AutoNavFunction1SearchSetGen,
            DisplayName = "AutoNAV F1: Generate Discipline Search Sets",
            Description = "Scans the model and auto-creates one selection set per detected discipline.",
            Parameters  = new Dictionary<string, string>()
        });

        public MacroStep CaptureAutoNavFunction2(string disciplines, string propCategory, string propName)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavFunction2SearchSetGen,
                DisplayName = $"AutoNAV F2: Search Sets by {propName} ({propCategory})",
                Description = "Splits disciplines into child search sets by a specific model property.",
                Parameters  = new Dictionary<string, string>
                {
                    ["Disciplines"]  = disciplines,
                    ["PropCategory"] = propCategory,
                    ["PropName"]     = propName,
                }
            });

        public MacroStep CaptureAutoNavFunction3(string discipline, string propCategory, string propName)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavFunction3CustomSearchSetGen,
                DisplayName = $"AutoNAV F3: Custom Sets — {discipline} / {propName}",
                Description = "Generates custom search sets for a single discipline using a chosen property.",
                Parameters  = new Dictionary<string, string>
                {
                    ["Discipline"]   = discipline,
                    ["PropCategory"] = propCategory,
                    ["PropName"]     = propName,
                }
            });

        public MacroStep CaptureAutoNavClashTestGen() => AddStep(new MacroStep
        {
            StepType    = MacroStepType.AutoNavClashTestGen,
            DisplayName = "AutoNAV F4: Generate Clash Tests",
            Description = "Generates clash tests from existing search sets using AutoNAV's matrix logic.",
            Parameters  = new Dictionary<string, string>()
        });

        public MacroStep CaptureAutoNavClashRunAndGroup(string primaryGroupBy, string subGroupBy)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavClashRunAndGroup,
                DisplayName = "AutoNAV F5: Run Tests & Group Results",
                Description = "Runs all clash tests, then groups results by proximity and discipline.",
                Parameters  = new Dictionary<string, string>
                {
                    ["PrimaryGroupBy"] = primaryGroupBy,
                    ["SubGroupBy"]     = subGroupBy,
                }
            });

        public MacroStep CaptureAutoNavClashGroup(string testName, string primaryGroupBy, string subGroupBy)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavClashGroupTest,
                DisplayName = $"AutoNAV F6: Group — {testName}",
                Description = "Groups clash results in a specific test by proximity.",
                Parameters  = new Dictionary<string, string>
                {
                    ["TestName"]       = testName ?? string.Empty,
                    ["PrimaryGroupBy"] = primaryGroupBy,
                    ["SubGroupBy"]     = subGroupBy,
                }
            });

        public MacroStep CaptureAutoNavClashUngroup(string testName)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavClashUngroup,
                DisplayName = $"AutoNAV: Ungroup — {testName}",
                Description = "Resets clash groups back to individual results.",
                Parameters  = new Dictionary<string, string> { ["TestName"] = testName ?? string.Empty }
            });

        public MacroStep CaptureAutoNavSearchSetGen(string discipline, string method)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavSearchSetGen,
                DisplayName = $"AutoNAV: Generate Search Sets ({discipline})",
                Parameters  = new Dictionary<string, string> { ["Discipline"] = discipline, ["Method"] = method }
            });

        public MacroStep CaptureAutoNavClashTestGenLegacy(string disciplines)
            => AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavClashTestGenLegacy,
                DisplayName = "AutoNAV: Generate Clash Tests",
                Parameters  = new Dictionary<string, string> { ["Disciplines"] = disciplines }
            });

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

        private MacroStep AddStep(MacroStep step)
        {
            if (step.Parameters == null) step.Parameters = new Dictionary<string, string>();
            _steps.Add(step);
            StepAdded?.Invoke(this, step);
            return step;
        }
    }
}
