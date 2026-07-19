using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MacroNAV.Models;

namespace MacroNAV
{
    public partial class StepEditorDialog : Window
    {
        public MacroStep ResultStep { get; private set; }
        private readonly MacroStep _original;

        public StepEditorDialog(MacroStep step, bool isNew = false)
        {
            InitializeComponent();
            _original = step;
            Title = isNew ? "Insert New Step" : "Edit Step";

            foreach (MacroStepType t in Enum.GetValues(typeof(MacroStepType)))
                CboStepType.Items.Add(new ComboBoxItem
                {
                    Content = $"{MacroStep.StepTypeIcon(t)}  {t}",
                    Tag     = t,
                    ToolTip = MacroStep.StepTypeCategory(t)
                });

            foreach (ComboBoxItem item in CboStepType.Items)
                if ((MacroStepType)item.Tag == step.StepType)
                { CboStepType.SelectedItem = item; break; }

            TxtDisplayName.Text  = step.DisplayName;
            TxtDescription.Text  = step.Description;
            ChkEnabled.IsChecked = step.IsEnabled;

            var rows = new ObservableCollection<ParamRow>();
            if (step.Parameters != null)
                foreach (var kvp in step.Parameters)
                    rows.Add(new ParamRow
                    {
                        Key     = kvp.Key,
                        Value   = kvp.Value,
                        Options = ParameterOptions.For(step.StepType, kvp.Key).ToList()
                    });
            ParamGrid.ItemsSource = rows;

            RefreshSchemaHint(step.StepType);
        }

        private void CboStepType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(CboStepType.SelectedItem is ComboBoxItem item)) return;
            var t = (MacroStepType)item.Tag;
            TxtDisplayName.Text = GenerateDefaultName(t);
            RefreshSchemaHint(t);
            SeedDefaultParameters(t);
        }

        private void RefreshSchemaHint(MacroStepType t)
        {
            if (TxtSchemaHint == null) return;
            TxtSchemaHint.Text = StepParameterSchema.HintText(t);
        }

        private void SeedDefaultParameters(MacroStepType t)
        {
            if (!(ParamGrid.ItemsSource is ObservableCollection<ParamRow> rows)) return;
            if (rows.Count > 0) return;
            foreach (var def in StepParameterSchema.For(t))
                if (!string.IsNullOrEmpty(def.DefaultVal) || def.Required)
                    rows.Add(new ParamRow
                    {
                        Key     = def.Key,
                        Value   = def.DefaultVal ?? string.Empty,
                        Options = ParameterOptions.For(t, def.Key).ToList()
                    });
        }

        private string GenerateDefaultName(MacroStepType t)
        {
            if (!string.IsNullOrWhiteSpace(TxtDisplayName?.Text)) return TxtDisplayName.Text;
            switch (t)
            {
                case MacroStepType.Comment:                          return "// Comment";
                case MacroStepType.Delay:                            return "Wait 1000ms";
                case MacroStepType.ClashCreateTest:                  return "Configure Clash Test";
                case MacroStepType.ClashSetSelectionA:               return "Set Selection A";
                case MacroStepType.ClashSetSelectionB:               return "Set Selection B";
                case MacroStepType.ClashRunTest:                     return "Run Clash Test";
                case MacroStepType.ClashRunAllTests:                 return "Run All Clash Tests";
                case MacroStepType.ClashAssignStatus:                return "Assign Clash Status";
                case MacroStepType.SearchSetCreate:                  return "Create Search Set";
                case MacroStepType.SearchSetActivate:                return "Activate Selection Set";
                case MacroStepType.SearchSetDelete:                  return "Delete Search Set";
                case MacroStepType.ViewpointActivate:                return "Go to Viewpoint";
                case MacroStepType.ViewpointSaveCurrent:             return "Save Current Viewpoint";
                case MacroStepType.AutoNavFunction1SearchSetGen:     return "AutoNAV F1: Generate Discipline Search Sets";
                case MacroStepType.AutoNavFunction2SearchSetGen:     return "AutoNAV F2: Property-Based Search Sets";
                case MacroStepType.AutoNavFunction3CustomSearchSetGen: return "AutoNAV F3: Custom Search Sets";
                case MacroStepType.AutoNavClashTestGen:              return "AutoNAV F4: Generate Clash Tests";
                case MacroStepType.AutoNavClashRunAndGroup:          return "AutoNAV F5: Run & Group";
                case MacroStepType.AutoNavClashGroupTest:            return "AutoNAV F6: Group Clash Test";
                case MacroStepType.AutoNavClashUngroup:              return "AutoNAV: Ungroup Clashes";
                case MacroStepType.FileOpen:                         return "Open File";
                case MacroStepType.FileAppend:                       return "Append File";
                default:                                              return t.ToString();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtDisplayName.Text))
            { MessageBox.Show("Display name is required."); return; }

            var selectedType = CboStepType.SelectedItem is ComboBoxItem ci
                ? (MacroStepType)ci.Tag
                : _original.StepType;

            var rows = (ObservableCollection<ParamRow>)ParamGrid.ItemsSource;
            var dict = rows
                .Where(r => !string.IsNullOrEmpty(r.Key))
                .ToDictionary(r => r.Key, r => r.Value ?? string.Empty);

            ResultStep = new MacroStep
            {
                Id          = _original.Id,
                StepType    = selectedType,
                DisplayName = TxtDisplayName.Text.Trim(),
                Description = TxtDescription.Text?.Trim() ?? string.Empty,
                Parameters  = dict,
                IsEnabled   = ChkEnabled.IsChecked == true,
            };
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }

    public class ParamRow
    {
        public string Key   { get; set; }
        public string Value { get; set; }

        // Allowed values for this key, pulled from the document / AutoNAV. Empty
        // means free text. The editor's ComboBox stays editable either way, so a
        // name that is not loaded yet can still be typed.
        public List<string> Options { get; set; } = new List<string>();

        public bool HasOptions => Options != null && Options.Count > 0;
    }
}
