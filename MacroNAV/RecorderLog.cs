using System;
using System.IO;
using System.Text;

namespace MacroNAV
{
    // Auto-capture runs inside Navisworks event handlers, where a thrown
    // exception would tear down the host's event dispatch. The handlers
    // therefore swallow everything -- which historically made "recording
    // stopped working" impossible to diagnose. Everything swallowed lands here
    // instead, so a user can send a log rather than a description.
    //
    // Writes to %AppData%\MacroNAV\recorder.log, capped at MaxBytes.
    internal static class RecorderLog
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new object();

        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroNAV", "recorder.log");

        public static void Info(string message) => Write("INFO ", message, null);
        public static void Warn(string message, Exception ex = null) => Write("WARN ", message, ex);

        public static void Session(string message)
        {
            Write("", "", null);
            Write("=====", message + "  (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ")", null);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ')
                  .Append(level).Append(' ').Append(message);
                if (ex != null) sb.AppendLine().Append("        ").Append(ex.GetType().Name)
                                  .Append(": ").Append(ex.Message);

                lock (Gate)
                {
                    var path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));

                    // Cheap rotation so the log cannot grow without bound.
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                        File.Delete(path);

                    File.AppendAllText(path, sb.ToString() + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never be the thing that breaks recording.
            }
        }

        public static string GetLogPath() => LogPath;
    }
}
