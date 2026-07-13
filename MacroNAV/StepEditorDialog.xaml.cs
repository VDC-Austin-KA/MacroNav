using System;
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

            // Populate step type combo
            foreach (MacroStepType t in Enum.GetValues(typeof(MacroStepType)))
                CboStepType.Items.Add(new ComboBoxItem { Content = t.ToString(), Tag = t });

            // Select current type
            foreach (ComboBoxItem item in CboStepType.Items)
                if ((MacroStepType)item.Tag == step.StepType)
                { CboStepType.SelectedItem = item; break; }

            TxtDisplayName.Text = step.DisplayName;
            TxtDescription.Text = step.Description;
            ChkEnabled.IsChecked = step.IsEnabled;

            // Load parameters into grid
            var rows = new ObservableCollection<ParamRow>();
            if (step.Parameters != null)
                foreach (var kvp in step.Parameters)
                    rows.Add(new ParamRow { Key = kvp.Key, Value = kvp.Value });
            ParamGrid.ItemsSource = rows;
        }

        private void CboStepType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboStepType.SelectedItem is ComboBoxItem item)
                TxtDisplayName.Text = GenerateDefaultName((MacroStepType)item.Tag);
        }

        private string GenerateDefaultName(MacroStepType t)
        {
            // Only override if the field is still empty or matches old default
            if (!string.IsNullOrWhiteSpace(TxtDisplayName?.Text)) return TxtDisplayName.Text;
            switch (t)
            {
                case MacroStepType.Comment: return "// Comment";
                case MacroStepType.Delay: return "Wait 1000ms";
                case MacroStepType.ClashCreateTest: return "Create Clash Test";
                case MacroStepType.ClashRunTest: return "Run Clash Test";
                case MacroStepType.ClashRunAllTests: return "Run All Clash Tests";
                case MacroStepType.ViewpointActivate: return "Go to Viewpoint";
                case MacroStepType.SearchSetActivate: return "Activate Selection Set";
                default: return t.ToString();
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
                Id = _original.Id,
                StepType = selectedType,
                DisplayName = TxtDisplayName.Text.Trim(),
                Description = TxtDescription.Text?.Trim() ?? string.Empty,
                Parameters = dict,
                IsEnabled = ChkEnabled.IsChecked == true,
            };

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }

    public class ParamRow
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
