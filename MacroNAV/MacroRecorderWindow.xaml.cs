using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MacroNAV.Models;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace MacroNAV
{
    public partial class MacroRecorderWindow : Window
    {
        private readonly MacroLibrary _library = new MacroLibrary();
        private readonly MacroRecorder _recorder = new MacroRecorder();
        private readonly MacroPlayer _player = new MacroPlayer();
        private readonly DispatcherTimer _blinkTimer = new DispatcherTimer();

        private Macro _activeMacro;
        private ObservableCollection<StepViewModel> _stepVMs = new ObservableCollection<StepViewModel>();
        private bool _suppressMetaChange;

        // Single live instance. Because the window is shown modelessly from
        // PluginMain.Execute(), this static reference keeps it alive after
        // Execute() returns (otherwise it would be garbage-collected) and lets
        // a second invocation re-focus the existing window instead of opening
        // a duplicate.
        public static MacroRecorderWindow Instance { get; private set; }

        public MacroRecorderWindow()
        {
            InitializeComponent();

            Instance = this;
            Closed += OnWindowClosed;

            _library.Load();
            RefreshMacroList();

            // Recorder events
            _recorder.StepAdded += (s, step) => Dispatcher.Invoke(() => AppendStep(step));
            _recorder.RecordingStarted += (s, e) => Dispatcher.Invoke(OnRecordingStarted);
            _recorder.RecordingStopped += (s, e) => Dispatcher.Invoke(OnRecordingStopped);

            // Player events
            _player.StepStarted += (s, step) => Dispatcher.Invoke(() =>
                SetStatus($"Running: {step.DisplayName}"));
            _player.StepCompleted += (s, r) => Dispatcher.Invoke(() =>
                SetStatus(r.Success ? $"✓ {r.Message}" : $"✗ {r.Message}"));
            _player.PlaybackCompleted += (s, e) => Dispatcher.Invoke(OnPlaybackCompleted);

            // Blink timer for recording indicator
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(600);
            _blinkTimer.Tick += (s, e) =>
            {
                RecordingDot.Visibility = RecordingDot.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
            };

            StepListBox.ItemsSource = _stepVMs;
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            // Ensure recording is torn down and event handlers detached when the
            // window closes, and release the static reference so a fresh window
            // can be opened next time.
            try { _recorder.StopRecording(); } catch { }
            _blinkTimer.Stop();
            Instance = null;
        }

        // ── Macro Library ───────────────────────────────────

        private void RefreshMacroList()
        {
            MacroListBox.ItemsSource = null;
            MacroListBox.ItemsSource = _library.Macros
                .OrderByDescending(m => m.LastModified).ToList();
        }

        private void MacroListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MacroListBox.SelectedItem is Macro m)
                LoadMacroIntoEditor(m);
        }

        private void LoadMacroIntoEditor(Macro macro)
        {
            _activeMacro = macro;
            _suppressMetaChange = true;
            TxtMacroName.Text = macro.Name;
            TxtMacroDesc.Text = macro.Description;
            _suppressMetaChange = false;

            _stepVMs.Clear();
            _recorder.ClearSteps();
            foreach (var step in macro.Steps)
            {
                // Seed recorder so edits go back to the same list
                _recorder.InsertStep(_stepVMs.Count, step);
                _stepVMs.Add(new StepViewModel(step));
            }
            UpdateStepNumbers();
            UpdateStepCount();
        }

        private void BtnNewMacro_Click(object sender, RoutedEventArgs e)
        {
            var macro = new Macro { Name = "New Macro" };
            _library.AddOrUpdate(macro);
            RefreshMacroList();
            MacroListBox.SelectedItem = _library.Macros.FirstOrDefault(m => m.Id == macro.Id);
            TxtMacroName.Focus();
        }

        private void BtnDeleteMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_activeMacro == null) return;
            var r = MessageBox.Show($"Delete macro '{_activeMacro.Name}'?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            _library.Delete(_activeMacro.Id);
            _activeMacro = null;
            _stepVMs.Clear();
            _recorder.ClearSteps();
            RefreshMacroList();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Macro",
                Filter = "JSON Macro (*.json)|*.json",
                InitialDirectory = _library.GetLibraryFolder()
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = System.IO.File.ReadAllText(dlg.FileName);
                var macro = _library.ImportFromJson(json);
                macro.Id = Guid.NewGuid().ToString();
                _library.AddOrUpdate(macro);
                RefreshMacroList();
                SetStatus($"Imported: {macro.Name}");
            }
            catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message); }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_activeMacro == null) { SetStatus("No macro selected."); return; }
            CommitActiveStepsToMacro();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Macro",
                Filter = "JSON Macro (*.json)|*.json",
                FileName = SanitizeFileName(_activeMacro.Name) + ".json",
                InitialDirectory = _library.GetLibraryFolder()
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                System.IO.File.WriteAllText(dlg.FileName, _library.ExportToJson(_activeMacro));
                SetStatus($"Exported to {dlg.FileName}");
            }
            catch (Exception ex) { MessageBox.Show("Export failed: " + ex.Message); }
        }

        private void TxtMacroMeta_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressMetaChange || _activeMacro == null) return;
            _activeMacro.Name = TxtMacroName.Text;
            _activeMacro.Description = TxtMacroDesc.Text;
            _library.AddOrUpdate(_activeMacro);
            RefreshMacroList();
        }

        // ── Step List ───────────────────────────────────────────────

        private void AppendStep(MacroStep step)
        {
            _stepVMs.Add(new StepViewModel(step));
            UpdateStepNumbers();
            UpdateStepCount();
            StepListBox.ScrollIntoView(_stepVMs.Last());
            CommitActiveStepsToMacro();
        }

        private void UpdateStepNumbers()
        {
            for (int i = 0; i < _stepVMs.Count; i++)
                _stepVMs[i].StepNumber = i + 1;
        }

        private void UpdateStepCount()
        {
            if (_activeMacro == null) { StepCountText.Text = ""; return; }
            int en = _stepVMs.Count(v => v.IsEnabled);
            StepCountText.Text = $"{en} / {_stepVMs.Count} steps enabled";
        }

        private void StepListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void StepListBox_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => EditSelectedStep();

        private void StepEnabled_Changed(object sender, RoutedEventArgs e)
        {
            CommitActiveStepsToMacro();
            UpdateStepCount();
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = StepListBox.SelectedIndex;
            if (idx <= 0) return;
            MoveStep(idx, idx - 1);
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = StepListBox.SelectedIndex;
            if (idx < 0 || idx >= _stepVMs.Count - 1) return;
            MoveStep(idx, idx + 1);
        }

        private void MoveStep(int from, int to)
        {
            var vm = _stepVMs[from];
            _stepVMs.RemoveAt(from);
            _stepVMs.Insert(to, vm);
            _recorder.MoveStep(from, to);
            UpdateStepNumbers();
            StepListBox.SelectedIndex = to;
            CommitActiveStepsToMacro();
        }

        private void BtnEditStep_Click(object sender, RoutedEventArgs e) => EditSelectedStep();

        private void EditSelectedStep()
        {
            if (StepListBox.SelectedItem is not StepViewModel vm) return;
            var dlg = new StepEditorDialog(vm.Step.Clone()) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var edited = dlg.ResultStep;
            vm.ApplyFrom(edited);
            _recorder.Steps[StepListBox.SelectedIndex].DisplayName = edited.DisplayName;
            _recorder.Steps[StepListBox.SelectedIndex].Parameters = edited.Parameters;
            _recorder.Steps[StepListBox.SelectedIndex].IsEnabled = edited.IsEnabled;
            _recorder.Steps[StepListBox.SelectedIndex].Description = edited.Description;
            UpdateStepNumbers();
            CommitActiveStepsToMacro();
        }

        private void BtnInsertStep_Click(object sender, RoutedEventArgs e)
        {
            var blank = new MacroStep { StepType = MacroStepType.Comment, DisplayName = "// New step" };
            var dlg = new StepEditorDialog(blank, isNew: true) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            int insertAt = StepListBox.SelectedIndex >= 0 ? StepListBox.SelectedIndex : _stepVMs.Count;
            _recorder.InsertStep(insertAt, dlg.ResultStep);
            _stepVMs.Insert(insertAt, new StepViewModel(dlg.ResultStep));
            UpdateStepNumbers();
            StepListBox.SelectedIndex = insertAt;
            CommitActiveStepsToMacro();
        }

        private void BtnDuplicateStep_Click(object sender, RoutedEventArgs e)
        {
            if (StepListBox.SelectedItem is not StepViewModel vm) return;
            int idx = StepListBox.SelectedIndex + 1;
            var clone = vm.Step.Clone();
            _recorder.InsertStep(idx, clone);
            _stepVMs.Insert(idx, new StepViewModel(clone));
            UpdateStepNumbers();
            StepListBox.SelectedIndex = idx;
            CommitActiveStepsToMacro();
        }

        private void BtnDeleteStep_Click(object sender, RoutedEventArgs e)
        {
            if (StepListBox.SelectedItem is not StepViewModel vm) return;
            int idx = StepListBox.SelectedIndex;
            _recorder.RemoveStep(vm.Step);
            _stepVMs.RemoveAt(idx);
            UpdateStepNumbers();
            UpdateStepCount();
            CommitActiveStepsToMacro();
        }

        private void BtnEnableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var vm in _stepVMs) vm.IsEnabled = true;
            CommitActiveStepsToMacro();
            UpdateStepCount();
        }

        private void BtnDisableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var vm in _stepVMs) vm.IsEnabled = false;
            CommitActiveStepsToMacro();
            UpdateStepCount();
        }

        // ── Recording ─────────────────────────────────────

        private void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_activeMacro == null)
            {
                // Auto-create a macro so the user is never blocked from recording.
                var macro = new Macro { Name = "Recording " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
                _library.AddOrUpdate(macro);
                RefreshMacroList();
                MacroListBox.SelectedItem = _library.Macros.FirstOrDefault(m => m.Id == macro.Id);
            }
            _recorder.StartRecording();
        }

        private void BtnStopRecord_Click(object sender, RoutedEventArgs e)
            => _recorder.StopRecording();

        private void OnRecordingStarted()
        {
            BtnRecord.IsEnabled = false;
            BtnStopRecord.IsEnabled = true;
            RecordingDot.Visibility = Visibility.Visible;
            RecordingLabel.Visibility = Visibility.Visible;
            _blinkTimer.Start();
            SetStatus("Recording — work in Navisworks. Selection sets, new saved viewpoints, "
                    + "model changes and AutoNAV F1–F7 are captured automatically. "
                    + "Use Quick Capture for clash runs and the current camera view.");
        }

        private void OnRecordingStopped()
        {
            BtnRecord.IsEnabled = true;
            BtnStopRecord.IsEnabled = false;
            RecordingDot.Visibility = Visibility.Collapsed;
            RecordingLabel.Visibility = Visibility.Collapsed;
            _blinkTimer.Stop();
            CommitActiveStepsToMacro();
            SetStatus($"Recording stopped. {_stepVMs.Count} steps captured.");
        }

        // ── Playback ───────────────────────────────────────────

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_activeMacro == null || _stepVMs.Count == 0)
            { SetStatus("No steps to play."); return; }

            BtnPlay.IsEnabled = false;
            BtnStopPlay.IsEnabled = true;
            BtnRecord.IsEnabled = false;
            SetStatus("Playing macro...");

            var steps = _recorder.Steps.ToList();
            await _player.PlayAsync(steps);
        }

        private void BtnStopPlay_Click(object sender, RoutedEventArgs e) => _player.Stop();

        private void OnPlaybackCompleted()
        {
            BtnPlay.IsEnabled = true;
            BtnStopPlay.IsEnabled = false;
            BtnRecord.IsEnabled = true;
            SetStatus("Playback complete.");
        }

        // ── Quick Capture Panel ───────────────────────────

        private void EnsureRecording()
        {
            if (!_recorder.IsRecording && _activeMacro != null)
                _recorder.StartRecording();
        }

        private void CboClashTests_DropDownOpened(object sender, EventArgs e)
            => PopulateClashTests();

        private void PopulateClashTests()
        {
            CboClashTests.Items.Clear();
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                var clash = doc.GetClash();
                if (clash?.TestsData?.Tests == null) return;
                foreach (var t in clash.TestsData.Tests)
                    CboClashTests.Items.Add(t.DisplayName);
            }
            catch { }
        }

        private void CboViewpoints_DropDownOpened(object sender, EventArgs e)
        {
            CboViewpoints.Items.Clear();
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                CollectViewpoints(doc.SavedViewpoints.RootItem, CboViewpoints.Items);
            }
            catch { }
        }

        private void CollectViewpoints(Autodesk.Navisworks.Api.GroupItem group,
            System.Collections.IList list)
        {
            foreach (var item in group.Children)
            {
                if (item is Autodesk.Navisworks.Api.SavedViewpoint vp)
                    list.Add(vp.DisplayName);
                else if (item is Autodesk.Navisworks.Api.GroupItem g)
                    CollectViewpoints(g, list);
            }
        }

        private void CboSelectionSets_DropDownOpened(object sender, EventArgs e)
        {
            CboSelectionSets.Items.Clear();
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;
                CollectSelectionSets(doc.SelectionSets.RootItem, CboSelectionSets.Items);
            }
            catch { }
        }

        private void CollectSelectionSets(Autodesk.Navisworks.Api.GroupItem group,
            System.Collections.IList list)
        {
            foreach (var item in group.Children)
            {
                if (item is Autodesk.Navisworks.Api.SelectionSet ss)
                    list.Add(ss.DisplayName);
                else if (item is Autodesk.Navisworks.Api.GroupItem g)
                    CollectSelectionSets(g, list);
            }
        }

        private void BtnCaptureClashConfig_Click(object sender, RoutedEventArgs e)
        {
            var name = CboClashTests.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a clash test first."); return; }
            EnsureRecording();
            _recorder.CaptureClashTestConfig(name);
            SetStatus($"Captured clash test config: {name}");
        }

        private void BtnCaptureRunTest_Click(object sender, RoutedEventArgs e)
        {
            var name = CboClashTests.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a clash test first."); return; }
            EnsureRecording();
            _recorder.CaptureRunClashTest(name);
            SetStatus($"Captured run clash test: {name}");
        }

        private void BtnCaptureRunAll_Click(object sender, RoutedEventArgs e)
        {
            EnsureRecording();
            _recorder.CaptureRunAllClashTests();
            SetStatus("Captured: Run All Clash Tests");
        }

        private void BtnCaptureCurrentView_Click(object sender, RoutedEventArgs e)
        {
            EnsureRecording();
            var step = _recorder.CaptureCurrentViewpoint();
            SetStatus(step != null ? $"Captured current viewpoint" : "No active document.");
        }

        private void BtnCaptureSavedViewpoint_Click(object sender, RoutedEventArgs e)
        {
            var name = CboViewpoints.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a saved viewpoint first."); return; }
            EnsureRecording();
            _recorder.CaptureActivateSavedViewpoint(name);
            SetStatus($"Captured viewpoint: {name}");
        }

        private void BtnCaptureSelectionSet_Click(object sender, RoutedEventArgs e)
        {
            var name = CboSelectionSets.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a selection set first."); return; }
            EnsureRecording();
            _recorder.CaptureSearchSetActivate(name);
            SetStatus($"Captured selection set: {name}");
        }

        private void BtnAddComment_Click(object sender, RoutedEventArgs e)
        {
            var text = TxtComment.Text.Trim();
            if (string.IsNullOrEmpty(text)) { SetStatus("Enter comment text."); return; }
            EnsureRecording();
            _recorder.CaptureComment(text);
            TxtComment.Clear();
            SetStatus("Comment added.");
        }

        private void BtnAddDelay_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtDelayMs.Text, out int ms) || ms <= 0)
            { SetStatus("Enter a valid delay in milliseconds."); return; }
            EnsureRecording();
            _recorder.CaptureDelay(ms);
            SetStatus($"Delay {ms}ms added.");
        }

        private void BtnCaptureAutoNavSS_Click(object sender, RoutedEventArgs e)
        {
            EnsureRecording();
            _recorder.CaptureAutoNavSearchSetGen("All", "Name-Contains");
            SetStatus("AutoNAV Search Set step added — edit parameters as needed.");
        }

        private void BtnCaptureAutoNavCT_Click(object sender, RoutedEventArgs e)
        {
            EnsureRecording();
            _recorder.CaptureAutoNavClashTestGen();
            SetStatus("AutoNAV Clash Test step added — edit parameters as needed.");
        }

        // ── Helpers ───────────────────────────────────────────────

        private void CommitActiveStepsToMacro()
        {
            if (_activeMacro == null) return;
            _activeMacro.Steps = _recorder.Steps.ToList();
            _library.AddOrUpdate(_activeMacro);
        }

        private void SetStatus(string msg) => StatusText.Text = msg;

        private static string SanitizeFileName(string name)
            => string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars()));
    }

    // ── Step ViewModel ──────────────────────────────────

    public class StepViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public MacroStep Step { get; }

        private int _stepNumber;
        public int StepNumber
        {
            get => _stepNumber;
            set { _stepNumber = value; OnPropChanged(nameof(StepNumber)); }
        }

        public string DisplayName
        {
            get => Step.DisplayName;
            set { Step.DisplayName = value; OnPropChanged(nameof(DisplayName)); }
        }

        public bool IsEnabled
        {
            get => Step.IsEnabled;
            set { Step.IsEnabled = value; OnPropChanged(nameof(IsEnabled)); }
        }

        public string ParamSummary => Step.GetParameterSummary();
        public string StepTypeIcon => MacroStep.StepTypeIcon(Step.StepType);

        public StepViewModel(MacroStep step) { Step = step; }

        public void ApplyFrom(MacroStep edited)
        {
            Step.DisplayName = edited.DisplayName;
            Step.Description = edited.Description;
            Step.Parameters = edited.Parameters;
            Step.IsEnabled = edited.IsEnabled;
            Step.StepType = edited.StepType;
            OnPropChanged(nameof(DisplayName));
            OnPropChanged(nameof(IsEnabled));
            OnPropChanged(nameof(ParamSummary));
            OnPropChanged(nameof(StepTypeIcon));
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropChanged(string n)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
    }
}
