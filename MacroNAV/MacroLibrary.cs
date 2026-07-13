using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using MacroNAV.Models;

namespace MacroNAV
{
    public class MacroLibrary
    {
        private static readonly string LibraryFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroNAV");

        private static readonly string LibraryFile = Path.Combine(LibraryFolder, "macros.json");

        public List<Macro> Macros { get; private set; } = new List<Macro>();

        public void Load()
        {
            if (!File.Exists(LibraryFile)) { Macros = new List<Macro>(); return; }
            try
            {
                var ser = new DataContractJsonSerializer(typeof(List<Macro>));
                using (var fs = File.OpenRead(LibraryFile))
                    Macros = (List<Macro>)ser.ReadObject(fs);
            }
            catch { Macros = new List<Macro>(); }
        }

        public void Save()
        {
            Directory.CreateDirectory(LibraryFolder);
            var ser = new DataContractJsonSerializer(typeof(List<Macro>));
            using (var fs = File.Create(LibraryFile))
                ser.WriteObject(fs, Macros);
        }

        public void AddOrUpdate(Macro macro)
        {
            macro.Touch();
            var idx = Macros.FindIndex(m => m.Id == macro.Id);
            if (idx >= 0) Macros[idx] = macro;
            else Macros.Add(macro);
            Save();
        }

        public void Delete(string id)
        {
            Macros.RemoveAll(m => m.Id == id);
            Save();
        }

        public string ExportToJson(Macro macro)
        {
            var ser = new DataContractJsonSerializer(typeof(Macro));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, macro);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public Macro ImportFromJson(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(Macro));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (Macro)ser.ReadObject(ms);
        }

        public string GetLibraryFolder() => LibraryFolder;
    }
}
