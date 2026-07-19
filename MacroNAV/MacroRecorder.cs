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
            RecorderLog.Session("recording started");
            SnapshotExistingNames();
            SubscribeEvents();

            // Events are bound to whichever document is active now. Opening or
            // appending a model can swap that document out, which silently
            // killed every subscription and made recording stop dead.
            try { NavApp.ActiveDocumentChanged += OnActiveDocumentChanged; } catch { }

            AutoNavBridge.Register(this);
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }

        public void StopRecording()
        {
            _isRecording = false;
            _vpDebounce.Stop();
            UnsubscribeEvents();
            try { NavApp.ActiveDocumentChanged -= OnActiveDocumentChanged; } catch { }
            AutoNavBridge.Unregister();
            RecorderLog.Info($"recording stopped; {_steps.Count} step(s) captured");
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        // Re-bind to the new document and re-snapshot, so recording survives a
        // file open / append instead of going quiet.
        private void OnActiveDocumentChanged(object sender, EventArgs e)
        {
            if (!_isRecording) return;
            RecorderLog.Info("active document changed - resubscribing");
            try { UnsubscribeEvents(); } catch { }
            try
            {
                SnapshotExistingNames();
                SubscribeEvents();
            }
            catch (Exception ex) { RecorderLog.Warn("resubscribe after document change failed", ex); }
        }

        public void ClearSteps() => _steps.Clear();

        private void SubscribeEvents()
        {
            var doc = NavApp.ActiveDocument;
            if (doc == null) { RecorderLog.Warn("SubscribeEvents: no active document - auto-capture is DEAD"); return; }
            int ok = 0;
            try { doc.SelectionSets.Changed         += OnSelectionSetsChanged;    ok++; } catch (Exception ex) { RecorderLog.Warn("subscribe SelectionSets.Changed failed", ex); }
            try { doc.SavedViewpoints.Changed        += OnSavedViewpointsChanged; ok++; } catch (Exception ex) { RecorderLog.Warn("subscribe SavedViewpoints.Changed failed", ex); }
            try { doc.CurrentSelection.Changed       += OnCurrentSelectionChanged; ok++; } catch (Exception ex) { RecorderLog.Warn("subscribe CurrentSelection.Changed failed", ex); }
            try { doc.Models.CollectionChanged       += OnModelsChanged;           ok++; } catch (Exception ex) { RecorderLog.Warn("subscribe Models.CollectionChanged failed", ex); }
            RecorderLog.Info($"subscribed to {ok}/4 document events; doc='{doc.Title}', " +
                             $"{_knownSetNames.Count} existing set(s), {_knownVpNames.Count} existing viewpoint(s)");
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
                if (doc == null) { RecorderLog.Warn("SelectionSets.Changed: no active document"); return; }

                var names = EnumerateNames(doc.SelectionSets.RootItem, isViewpoint: false);
                int added = 0;
                foreach (var name in names)
                {
                    if (_knownSetNames.Contains(name)) continue;
                    // Isolate each item: one failure must not drop the rest of a
                    // batch (a generator can create many sets at once).
                    try
                    {
                        _knownSetNames.Add(name);
                        // Naming matters here. Playback can only re-select this
                        // set; it cannot recreate it, because Navisworks exposes
                        // no way to serialise a search. Recording the AutoNAV
                        // generator step is what actually reproduces the sets.
                        AddStep(new MacroStep
                        {
                            StepType    = MacroStepType.SearchSetActivate,
                            DisplayName = $"[Auto] Select set: {name}",
                            Description = "Selects this named set on playback. It does NOT create the set — " +
                                          "the set must already exist in the target model. To regenerate sets, " +
                                          "record the AutoNAV search-set generation step instead.",
                            Parameters  = new Dictionary<string, string> { ["Name"] = name }
                        });
                        added++;
                    }
                    catch (Exception ex) { RecorderLog.Warn("capture of selection set '" + name + "' failed", ex); }
                }
                if (added > 0) RecorderLog.Info($"SelectionSets.Changed: captured {added} new set(s); total steps={_steps.Count}");
            }
            catch (Exception ex) { RecorderLog.Warn("OnSelectionSetsChanged failed", ex); }
        }

        // A saved viewpoint was created / renamed / removed. Capture any new ones
        // as an "activate saved viewpoint" step (replayable on the same model).
        private void OnSavedViewpointsChanged(object sender, EventArgs e)
        {
            if (!_autoCapture || !_isRecording) return;
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) { RecorderLog.Warn("SavedViewpoints.Changed: no active document"); return; }
                foreach (var name in EnumerateNames(doc.SavedViewpoints.RootItem, isViewpoint: true))
                {
                    if (_knownVpNames.Contains(name)) continue;
                    try
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
                    catch (Exception ex) { RecorderLog.Warn("capture of viewpoint '" + name + "' failed", ex); }
                }
            }
            catch (Exception ex) { RecorderLog.Warn("OnSavedViewpointsChanged failed", ex); }
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
            // Fired from a timer thread; the Navisworks API is main-thread only,
            // so marshal through the WPF dispatcher that owns the plugin window.
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                dispatcher.Invoke(new Action(() => CaptureCurrentViewpoint("[Auto]")));
            }
            catch { }
        }

        private static string FindMatchingSelectionSetName(Document doc)
        {
            try
            {
                var current = doc.CurrentSelection.SelectedItems;
                if (current == null || current.IsEmpty) return null;

                // Recurse: real projects file discipline sets inside folders, and
                // scanning only the root missed every one of them.
                return FindMatchingSet(doc.SelectionSets.RootItem, current);
            }
            catch (Exception ex) { RecorderLog.Warn("FindMatchingSelectionSetName failed", ex); }
            return null;
        }

        private static string FindMatchingSet(GroupItem group, ModelItemCollection current)
        {
            foreach (SavedItem item in group.Children)
            {
                if (item is SelectionSet ss)
                {
                    ModelItemCollection sel = null;
                    try { sel = ss.GetSelectedItems(); } catch { }
                    if (sel != null && SelectionsEqual(current, sel)) return ss.DisplayName;
                }
                if (item is GroupItem g)
                {
                    var nested = FindMatchingSet(g, current);
                    if (nested != null) return nested;
                }
            }
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
                    selA = ClashCompat.SerialiseSelectionNames(test.SelectionA, doc);
                    selB = ClashCompat.SerialiseSelectionNames(test.SelectionB, doc);
                    tol  = test.Tolerance.ToString("F4");
                    type = test.TestType.ToString();
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
            // The view direction/up are only exposed as setters (AlignDirection /
            // AlignUp); Rotation is a raw quaternion. GetCamera() round-trips the
            // whole camera losslessly, so store that as the authoritative payload
            // and keep Pos/Fov alongside it as readable, editable values.
            var vp   = doc.CurrentViewpoint.Value;
            var pos  = vp.Position;
            var name = label ?? $"Viewpoint {DateTime.Now:HH:mm:ss}";
            return AddStep(new MacroStep
            {
                StepType    = MacroStepType.ViewpointActivate,
                DisplayName = $"Go to: {name}",
                Parameters  = new Dictionary<string, string>
                {
                    ["Name"]     = name,
                    ["UseSaved"] = "false",
                    ["Camera"]   = vp.GetCamera(),
                    ["PosX"]     = pos.X.ToString("F6"),
                    ["PosY"]     = pos.Y.ToString("F6"),
                    ["PosZ"]     = pos.Z.ToString("F6"),
                    ["Fov"]      = vp.HeightField.ToString("F4"),
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

        // Rename clash groups by template. Executed through AutoNAV's ClashGrouper
        // directly, so replaying it does not load AutoNAV's rename tree first.
        public MacroStep CaptureAutoNavRenameGroups(string testName, string template,
            string statuses = "New|Active")
        {
            var target = string.IsNullOrWhiteSpace(testName) ? "*" : testName;
            return AddStep(new MacroStep
            {
                StepType    = MacroStepType.AutoNavRenameGroups,
                DisplayName = target == "*"
                    ? "AutoNAV: Rename groups (all tests)"
                    : $"AutoNAV: Rename groups in {target}",
                Description = "Applies the naming template without opening AutoNAV's rename tab.",
                Parameters  = new Dictionary<string, string>
                {
                    ["TestName"] = target,
                    ["Template"] = string.IsNullOrWhiteSpace(template)
                                   ? AutoNavRenameTemplates.Default : template,
                    ["Statuses"] = statuses,
                }
            });
        }

        public MacroStep CaptureAutoNavGroupWallsFloors() => AddStep(new MacroStep
        {
            StepType    = MacroStepType.AutoNavGroupWallsFloors,
            DisplayName = "AutoNAV: Group all tests by Walls/Floors"
        });

        public MacroStep CaptureAutoNavRunAllClashTests() => AddStep(new MacroStep
        {
            StepType    = MacroStepType.AutoNavRunAllClashTests,
            DisplayName = "AutoNAV: Run all clash tests"
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

            // StepAdded runs UI work in the window. A failure there must not
            // propagate into the capture loops, or one bad step silently aborts
            // the rest of the batch.
            try { StepAdded?.Invoke(this, step); }
            catch (Exception ex) { RecorderLog.Warn("StepAdded handler threw for '" + step.DisplayName + "'", ex); }

            return step;
        }
    }
}
