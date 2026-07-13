using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace MacroNAV.Models
{
    public enum MacroStepType
    {
        // Control
        Comment,
        Delay,

        // Clash Detective
        ClashCreateTest,
        ClashSetSelectionA,
        ClashSetSelectionB,
        ClashRunTest,
        ClashRunAllTests,
        ClashAssignStatus,

        // Search Sets
        SearchSetCreate,
        SearchSetActivate,
        SearchSetDelete,

        // Viewpoints / Navigation
        ViewpointActivate,
        ViewpointSaveCurrent,

        // AutoNAV Integration
        AutoNavSearchSetGen,
        AutoNavClashTestGen,

        // File
        FileOpen,
        FileAppend,
    }

    [DataContract]
    public class MacroStep
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public MacroStepType StepType { get; set; }
        [DataMember] public string DisplayName { get; set; }
        [DataMember] public string Description { get; set; }
        [DataMember] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        [DataMember] public bool IsEnabled { get; set; } = true;
        [DataMember] public string RecordedAt { get; set; } = DateTime.Now.ToString("o");

        public string GetParameterSummary()
        {
            if (Parameters == null || Parameters.Count == 0) return string.Empty;
            return string.Join("  |  ", Parameters.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        }

        public MacroStep Clone() => new MacroStep
        {
            Id = Guid.NewGuid().ToString(),
            StepType = StepType,
            DisplayName = DisplayName,
            Description = Description,
            Parameters = new Dictionary<string, string>(Parameters),
            IsEnabled = IsEnabled,
        };

        public static string StepTypeIcon(MacroStepType t)
        {
            switch (t)
            {
                case MacroStepType.Comment: return "💬";
                case MacroStepType.Delay: return "⏱";
                case MacroStepType.ClashCreateTest: return "⚡";
                case MacroStepType.ClashSetSelectionA:
                case MacroStepType.ClashSetSelectionB: return "🔵";
                case MacroStepType.ClashRunTest:
                case MacroStepType.ClashRunAllTests: return "▶";
                case MacroStepType.ClashAssignStatus: return "🏷";
                case MacroStepType.SearchSetCreate:
                case MacroStepType.SearchSetActivate:
                case MacroStepType.SearchSetDelete: return "📂";
                case MacroStepType.ViewpointActivate:
                case MacroStepType.ViewpointSaveCurrent: return "📷";
                case MacroStepType.AutoNavSearchSetGen:
                case MacroStepType.AutoNavClashTestGen: return "🤖";
                case MacroStepType.FileOpen:
                case MacroStepType.FileAppend: return "📁";
                default: return "•";
            }
        }
    }
}
