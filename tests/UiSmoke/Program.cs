using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacroNAVTests.UiSmoke
{
    // Loads the real MacroNAV WPF window outside Navisworks and forces every
    // template to realize. XAML template faults (e.g. a TwoWay binding against a
    // read-only property) only throw when the template is instantiated, so a
    // compile plus a headless API test cannot catch them -- this can.
    internal static class Program
    {
        private static readonly List<string> BindingErrors = new List<string>();

        [STAThread]
        private static int Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveNavisworks;
            return Run();
        }

        // Kept out of Main so the JIT does not need MacroNAV's types before the
        // assembly resolver above is installed.
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Run()
        {
            // Surface binding failures that WPF only writes to the trace log.
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new CollectingListener());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int exit = 0;

            app.Startup += (s, e) =>
            {
                try
                {
                    var window = new MacroNAV.MacroRecorderWindow();

                    // Off-screen, but a real Show() so ListBox item containers and
                    // their DataTemplates actually realize.
                    // Kept on-screen but transparent: ComboBox popups are separate
                    // top-level windows and do not realize their containers when
                    // positioned off-screen, which the contrast check needs.
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = 0;
                    window.Top = 0;
                    window.ShowInTaskbar = false;
                    window.Opacity = 0.01;
                    window.Show();

                    window.UpdateLayout();
                    Drain(DispatcherPriority.ContextIdle);

                    Console.WriteLine("Window constructed and laid out.");
                    Console.WriteLine("  ActualWidth : " + window.ActualWidth);
                    Console.WriteLine("  ActualHeight: " + window.ActualHeight);

                    if (!CheckComboContrast(window)) exit = 1;

                    window.Close();
                    Drain(DispatcherPriority.ContextIdle);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FATAL during window load:");
                    Console.WriteLine(ex);
                    exit = 1;
                }
                finally
                {
                    app.Shutdown();
                }
            };

            app.Run();

            Console.WriteLine();
            if (BindingErrors.Count > 0)
            {
                Console.WriteLine("BINDING ERRORS (" + BindingErrors.Count + "):");
                foreach (var b in BindingErrors) Console.WriteLine("  " + b);
                exit = 1;
            }
            else
            {
                Console.WriteLine("No binding errors reported.");
            }

            Console.WriteLine(exit == 0 ? "RESULT  PASS" : "RESULT  FAIL");
            return exit;
        }

        // Opens every ComboBox and measures the rendered item colours. The
        // dropdown popup is a separate visual tree that does not inherit the
        // window background, which is how light text ended up on a system-white
        // popup. Contrast is asserted, not eyeballed.
        private static bool CheckComboContrast(Window window)
        {
            Console.WriteLine();
            Console.WriteLine("ComboBox dropdown contrast:");

            var combos = Descendants<System.Windows.Controls.ComboBox>(window).ToList();
            if (combos.Count == 0)
            {
                Console.WriteLine("  no ComboBoxes found - cannot verify");
                return false;
            }

            bool ok = true;
            foreach (var combo in combos)
            {
                var label = string.IsNullOrEmpty(combo.Name) ? "(unnamed)" : combo.Name;

                combo.IsDropDownOpen = true;

                // Seed AFTER opening: the DropDownOpened handlers call
                // Items.Clear() and repopulate from the active document, which
                // does not exist outside Navisworks, so seeding first is wiped.
                bool seeded = false;
                if (combo.Items.Count == 0)
                {
                    combo.Items.Add("Sample item");
                    combo.Items.Add("Second item");
                    seeded = true;
                }
                // Container generation is asynchronous; pump until it settles.
                for (int i = 0; i < 5; i++)
                {
                    combo.UpdateLayout();
                    Drain(DispatcherPriority.ContextIdle);
                    if (combo.ItemContainerGenerator.ContainerFromIndex(0) != null) break;
                }

                var item = combo.ItemContainerGenerator.ContainerFromIndex(0)
                           as System.Windows.Controls.ComboBoxItem;
                if (item == null)
                {
                    Console.WriteLine($"  {label,-18} FAIL  no item container realized " +
                        $"(items={combo.Items.Count}, visible={combo.IsVisible}, " +
                        $"loaded={combo.IsLoaded}, open={combo.IsDropDownOpen})");
                    ok = false;
                }
                else
                {
                    var fg = AsColor(item.Foreground) ?? Colors.Transparent;
                    var bg = EffectiveBackground(item);
                    double ratio = Contrast(fg, bg);
                    // WCAG AA for normal text.
                    bool pass = ratio >= 4.5;
                    if (!pass) ok = false;
                    Console.WriteLine($"  {label,-18} {(pass ? "ok  " : "FAIL")}  " +
                                      $"fg={fg} bg={bg} contrast={ratio:F2}:1");
                }

                combo.IsDropDownOpen = false;
                Drain(DispatcherPriority.Loaded);
                if (seeded) combo.Items.Clear();
            }
            return ok;
        }

        // Walks up until it finds an opaque background, mirroring what the eye
        // sees behind the text.
        private static Color EffectiveBackground(DependencyObject start)
        {
            var node = start;
            while (node != null)
            {
                if (node is System.Windows.Controls.Control c)
                {
                    var col = AsColor(c.Background);
                    if (col.HasValue && col.Value.A > 0) return col.Value;
                }
                if (node is System.Windows.Controls.Border b)
                {
                    var col = AsColor(b.Background);
                    if (col.HasValue && col.Value.A > 0) return col.Value;
                }
                node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
            }
            // Nothing opaque found: the system popup default is white, which is
            // exactly the failure mode being guarded against.
            return Colors.White;
        }

        private static Color? AsColor(Brush brush)
            => brush is SolidColorBrush s ? s.Color : (Color?)null;

        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        private static double Luminance(Color c)
        {
            Func<double, double> ch = v =>
            {
                v /= 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            };
            return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) yield return hit;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        private static void Drain(DispatcherPriority priority)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(priority,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static Assembly ResolveNavisworks(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            foreach (var dir in new[]
            {
                @"C:\Program Files\Autodesk\Navisworks Manage 2025",
                AppDomain.CurrentDomain.BaseDirectory,
                // MacroNAV.dll is referenced with Private=False, so it is not
                // copied next to this exe -- load it from its build output.
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    @"..\..\..\MacroNAV\bin\Release-NW2025")),
            })
            {
                var path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        }

        private sealed class CollectingListener : TraceListener
        {
            public override void Write(string message) { }

            public override void WriteLine(string message)
            {
                if (!string.IsNullOrEmpty(message)) BindingErrors.Add(message);
            }
        }
    }
}
