using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
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
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = -10000;
                    window.Top = -10000;
                    window.ShowInTaskbar = false;
                    window.Show();

                    window.UpdateLayout();
                    Drain(DispatcherPriority.ContextIdle);

                    Console.WriteLine("Window constructed and laid out.");
                    Console.WriteLine("  ActualWidth : " + window.ActualWidth);
                    Console.WriteLine("  ActualHeight: " + window.ActualHeight);

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
