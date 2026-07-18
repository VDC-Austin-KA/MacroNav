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

        // Search / Selection Sets
        SearchSetCreate,
        SearchSetActivate,
        SearchSetDelete,

        // Viewpoints / Navigation
        ViewpointActivate,
        ViewpointSaveCurrent,

        // AutoNAV — Search Set Generation
        AutoNavFunction1SearchSetGen,       // F1: scan model, create discipline sets
        AutoNavFunction2SearchSetGen,       // F2: split by property per discipline
        AutoNavFunction3CustomSearchSetGen, // F3: custom property query

        // AutoNAV — Clash
        AutoNavClashTestGen,                // F4: generate clash tests from search sets
        AutoNavClashRunAndGroup,            // F5: run all tests then group results
        AutoNavClashGroupTest,              // F6/7: group a single named test
        AutoNavClashUngroup,               // ungroup (reset to individual results)

        // Legacy AutoNAV (kept for backwards compatibility with saved macros)
        AutoNavSearchSetGen,
        AutoNavClashTestGenLegacy,

        // File
        FileOpen,
        FileAppend,
    }

    [DataContract]
    public class MacroStep
    {
        [DataMember] public string Id          { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public MacroStepType StepType { get; set; }
        [DataMember] public string DisplayName { get; set; }
        [DataMember] public string Description { get; set; }
        [DataMember] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        [DataMember] public bool IsEnabled     { get; set; } = true;
        [DataMember] public string RecordedAt  { get; set; } = DateTime.Now.ToString("o");

        public string GetParameterSummary()
        {
            if (Parameters == null || Parameters.Count == 0) return string.Empty;
            return string.Join("  |  ", Parameters.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        }

        public MacroStep Clone() => new MacroStep
        {
            Id          = Guid.NewGuid().ToString(),
            StepType    = StepType,
            DisplayName = DisplayName,
            Description = Description,
            Parameters  = new Dictionary<string, string>(Parameters ?? new Dictionary<string, string>()),
            IsEnabled   = IsEnabled,
        };

        public static string StepTypeIcon(MacroStepType t)
        {
            switch (t)
            {
                case MacroStepType.Comment:                        return "💬";
                case MacroStepType.Delay:                          return "⏱";
                case MacroStepType.ClashCreateTest:                return "⚡";
                case MacroStepType.ClashSetSelectionA:
                case MacroStepType.ClashSetSelectionB:             return "🔵";
                case MacroStepType.ClashRunTest:
                case MacroStepType.ClashRunAllTests:               return "▶";
                case MacroStepType.ClashAssignStatus:              return "🏷";
                case MacroStepType.SearchSetCreate:
                case MacroStepType.SearchSetActivate:
                case MacroStepType.SearchSetDelete:                return "📂";
                case MacroStepType.ViewpointActivate:
                case MacroStepType.ViewpointSaveCurrent:           return "📷";
                case MacroStepType.AutoNavFunction1SearchSetGen:
                case MacroStepType.AutoNavFunction2SearchSetGen:
                case MacroStepType.AutoNavFunction3CustomSearchSetGen:
                case MacroStepType.AutoNavSearchSetGen:            return "🤖📂";
                case MacroStepType.AutoNavClashTestGen:
                case MacroStepType.AutoNavClashTestGenLegacy:      return "🤖⚡";
                case MacroStepType.AutoNavClashRunAndGroup:        return "🤖▶";
                case MacroStepType.AutoNavClashGroupTest:          return "🤖🗂";
                case MacroStepType.AutoNavClashUngroup:            return "🤖↩";
                case MacroStepType.FileOpen:
                case MacroStepType.FileAppend:                     return "📁";
                default:                                            return "•";
            }
        }

        public static string StepTypeCategory(MacroStepType t)
        {
            switch (t)
            {
                case MacroStepType.Comment:
                case MacroStepType.Delay:
                    return "Control";
                case MacroStepType.ClashCreateTest:
                case MacroStepType.ClashSetSelectionA:
                case MacroStepType.ClashSetSelectionB:
                case MacroStepType.ClashRunTest:
                case MacroStepType.ClashRunAllTests:
                case MacroStepType.ClashAssignStatus:
                    return "Clash Detective";
                case MacroStepType.SearchSetCreate:
                case MacroStepType.SearchSetActivate:
                case MacroStepType.SearchSetDelete:
                    return "Selection Sets";
                case MacroStepType.ViewpointActivate:
                case MacroStepType.ViewpointSaveCurrent:
                    return "Viewpoints";
                case MacroStepType.AutoNavFunction1SearchSetGen:
                case MacroStepType.AutoNavFunction2SearchSetGen:
                case MacroStepType.AutoNavFunction3CustomSearchSetGen:
                case MacroStepType.AutoNavClashTestGen:
                case MacroStepType.AutoNavClashRunAndGroup:
                case MacroStepType.AutoNavClashGroupTest:
                case MacroStepType.AutoNavClashUngroup:
                case MacroStepType.AutoNavSearchSetGen:
                case MacroStepType.AutoNavClashTestGenLegacy:
                    return "AutoNAV";
                case MacroStepType.FileOpen:
                case MacroStepType.FileAppend:
                    return "File";
                default:
                    return "Other";
            }
        }
    }
}
