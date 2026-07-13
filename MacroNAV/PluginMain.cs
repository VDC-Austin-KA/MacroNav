using System;
using Autodesk.Navisworks.Api.Plugins;
using System.Windows;

namespace MacroNAV
{
    [Plugin("MacroNAV",
        "ACLP_VDC",
        ToolTip = "MacroNAV: Record, edit, and replay Navisworks workflows",
        DisplayName = "MacroNAV")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class PluginMain : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                var window = new MacroRecorderWindow();
                window.ShowDialog();
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error launching MacroNAV:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "MacroNAV Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
