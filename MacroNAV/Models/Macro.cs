using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace MacroNAV.Models
{
    [DataContract]
    public class Macro
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; } = "New Macro";
        [DataMember] public string Description { get; set; } = string.Empty;
        [DataMember] public string Tags { get; set; } = string.Empty;
        [DataMember] public List<MacroStep> Steps { get; set; } = new List<MacroStep>();
        [DataMember] public string CreatedAt { get; set; } = DateTime.Now.ToString("o");
        [DataMember] public string LastModified { get; set; } = DateTime.Now.ToString("o");

        public int EnabledStepCount => Steps?.Count(s => s.IsEnabled) ?? 0;
        public int TotalStepCount => Steps?.Count ?? 0;

        public void Touch() => LastModified = DateTime.Now.ToString("o");
    }
}
