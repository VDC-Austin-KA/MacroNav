using System;
using Autodesk.Navisworks.Api.Plugins;
using System.Windows;
using System.Windows.Interop;
using NavApp = Autodesk.Navisworks.Api.Application;

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
                // If the window is already open, just bring it forward.
                // A recorder needs to observe the user working *in* Navisworks,
                // so the window MUST be modeless — ShowDialog() would block the
                // Navisworks UI thread and make recording impossible.
                if (MacroRecorderWindow.Instance != null)
                {
                    MacroRecorderWindow.Instance.Activate();
                    return 0;
                }

                var window = new MacroRecorderWindow();

                // Parent the WPF window to the Navisworks main window so it floats
                // above Navisworks and is owned by it (closes with Navisworks, etc.).
                try
                {
                    var helper = new WindowInteropHelper(window);
                    var mainHandle = NavApp.Gui.MainWindow.Handle;
                    if (mainHandle != IntPtr.Zero)
                        helper.Owner = mainHandle;
                }
                catch { /* owner is best-effort; window still works unparented */ }

                // Modeless: Execute() returns immediately and Navisworks stays
                // interactive. MacroRecorderWindow keeps a static reference to
                // itself (Instance) so it is not garbage-collected after we return.
                window.Show();
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
