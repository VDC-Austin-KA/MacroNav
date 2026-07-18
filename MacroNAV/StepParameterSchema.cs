using System.Collections.Generic;
using MacroNAV.Models;

namespace MacroNAV
{
    // Describes every valid parameter key for each MacroStepType so the Step
    // Editor can show inline hints and the user knows exactly what can be tweaked.
    public static class StepParameterSchema
    {
        public class ParameterDef
        {
            public string Key         { get; set; }
            public string Description { get; set; }
            public string Example     { get; set; }
            public string DefaultVal  { get; set; }
            public bool   Required    { get; set; }
        }

        private static readonly Dictionary<MacroStepType, List<ParameterDef>> _schema
            = new Dictionary<MacroStepType, List<ParameterDef>>
        {
            [MacroStepType.Comment] = new List<ParameterDef>
            {
                new ParameterDef { Key="Text", Description="Comment text displayed in the step list", Required=true, Example="Start of MEP clash workflow" },
            },

            [MacroStepType.Delay] = new List<ParameterDef>
            {
                new ParameterDef { Key="Milliseconds", Description="Time to pause before next step (ms)", Required=true, Example="2000", DefaultVal="1000" },
            },

            [MacroStepType.ClashCreateTest] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName",   Description="Display name of the clash test to create or update", Required=true,  Example="MEP-Structural Hard Clashes" },
                new ParameterDef { Key="SelectionA", Description="Pipe-separated list of selection set names for Set A",  Required=false, Example="Mechanical|Plumbing|Electrical" },
                new ParameterDef { Key="SelectionB", Description="Pipe-separated list of selection set names for Set B",  Required=false, Example="Structural" },
                new ParameterDef { Key="Tolerance",  Description="Clash tolerance in model units (e.g. feet or meters)", Required=false, Example="0.0010", DefaultVal="0.0010" },
                new ParameterDef { Key="Type",        Description="Clash type: HardClash, Clearance, or Duplicates",     Required=false, Example="HardClash", DefaultVal="HardClash" },
            },

            [MacroStepType.ClashSetSelectionA] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName",   Description="Name of the target clash test",            Required=true },
                new ParameterDef { Key="SelectionA", Description="Pipe-separated selection set names for A", Required=true, Example="Mechanical|Plumbing" },
            },

            [MacroStepType.ClashSetSelectionB] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName",   Description="Name of the target clash test",            Required=true },
                new ParameterDef { Key="SelectionB", Description="Pipe-separated selection set names for B", Required=true, Example="Structural" },
            },

            [MacroStepType.ClashRunTest] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName", Description="Name of the clash test to run", Required=true },
            },

            [MacroStepType.ClashRunAllTests] = new List<ParameterDef>(),

            [MacroStepType.ClashAssignStatus] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName",   Description="Clash test to apply assignment to", Required=true },
                new ParameterDef { Key="Status",      Description="Result status: Active, Reviewed, Approved, or Resolved", Required=true, Example="Reviewed" },
                new ParameterDef { Key="AssignedTo", Description="Username or display name to assign to", Required=false },
            },

            [MacroStepType.SearchSetCreate] = new List<ParameterDef>
            {
                new ParameterDef { Key="Name",        Description="Name of the search/selection set to create", Required=true },
                new ParameterDef { Key="SearchXml",   Description="Raw NWSearch XML query string",               Required=false },
            },

            [MacroStepType.SearchSetActivate] = new List<ParameterDef>
            {
                new ParameterDef { Key="Name", Description="Name of the selection set to activate (highlight)", Required=true, Example="MEP-Mechanical" },
            },

            [MacroStepType.SearchSetDelete] = new List<ParameterDef>
            {
                new ParameterDef { Key="Name", Description="Name of the selection set to delete", Required=true },
            },

            [MacroStepType.ViewpointActivate] = new List<ParameterDef>
            {
                new ParameterDef { Key="Name",     Description="Saved viewpoint name (if UseSaved=true) or a label",      Required=false, Example="Overview - Level 3" },
                new ParameterDef { Key="UseSaved", Description="true = restore a named saved viewpoint; false = use stored raw position", Required=true, DefaultVal="true" },
                new ParameterDef { Key="PosX",  Description="Camera X position (model units)", Required=false },
                new ParameterDef { Key="PosY",  Description="Camera Y position (model units)", Required=false },
                new ParameterDef { Key="PosZ",  Description="Camera Z position (model units)", Required=false },
                new ParameterDef { Key="LookX", Description="Camera look-direction X component", Required=false },
                new ParameterDef { Key="LookY", Description="Camera look-direction Y component", Required=false },
                new ParameterDef { Key="LookZ", Description="Camera look-direction Z component", Required=false },
                new ParameterDef { Key="UpX",   Description="Camera up-vector X component",    Required=false },
                new ParameterDef { Key="UpY",   Description="Camera up-vector Y component",    Required=false },
                new ParameterDef { Key="UpZ",   Description="Camera up-vector Z component",    Required=false },
                new ParameterDef { Key="Fov",   Description="Field-of-view angle (degrees)",   Required=false, DefaultVal="45" },
            },

            [MacroStepType.ViewpointSaveCurrent] = new List<ParameterDef>
            {
                new ParameterDef { Key="Name", Description="Name under which to save the current camera position", Required=true, Example="Overview Before Clash Run" },
            },

            [MacroStepType.FileOpen] = new List<ParameterDef>
            {
                new ParameterDef { Key="Path", Description="Absolute path to the .nwd or .nwf file to open", Required=true, Example="C:\\Projects\\Model.nwf" },
            },

            [MacroStepType.FileAppend] = new List<ParameterDef>
            {
                new ParameterDef { Key="Path", Description="Absolute path to the file to append to the scene",  Required=true, Example="C:\\Projects\\Structural.nwc" },
            },

            [MacroStepType.AutoNavFunction1SearchSetGen] = new List<ParameterDef>
            {
                new ParameterDef { Key="Note", Description="(Optional) free-text note about this recording", Required=false },
            },

            [MacroStepType.AutoNavFunction2SearchSetGen] = new List<ParameterDef>
            {
                new ParameterDef { Key="Disciplines",   Description="Comma-separated discipline names that were processed", Required=false, Example="Mechanical,Plumbing,Electrical" },
                new ParameterDef { Key="PropCategory",  Description="NW property category used to split sets",               Required=false, Example="Item" },
                new ParameterDef { Key="PropName",      Description="NW property name used to split sets",                   Required=false, Example="Category" },
            },

            [MacroStepType.AutoNavFunction3CustomSearchSetGen] = new List<ParameterDef>
            {
                new ParameterDef { Key="Discipline",  Description="Discipline the custom set belongs to",           Required=true,  Example="Mechanical" },
                new ParameterDef { Key="PropCategory", Description="NW property category for the custom query",     Required=true,  Example="Item" },
                new ParameterDef { Key="PropName",     Description="NW property name for the custom query",         Required=true,  Example="System Name" },
            },

            [MacroStepType.AutoNavClashTestGen] = new List<ParameterDef>
            {
                new ParameterDef { Key="Note", Description="(Optional) free-text note about this recording", Required=false },
            },

            [MacroStepType.AutoNavClashRunAndGroup] = new List<ParameterDef>
            {
                new ParameterDef { Key="PrimaryGroupBy", Description="Primary grouping mode (e.g. Element, Level, Zone)", Required=false, DefaultVal="Element" },
                new ParameterDef { Key="SubGroupBy",     Description="Secondary grouping mode",                            Required=false, DefaultVal="None" },
            },

            [MacroStepType.AutoNavClashGroupTest] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName",       Description="Name of the clash test to group",                    Required=true },
                new ParameterDef { Key="PrimaryGroupBy", Description="Primary grouping mode (e.g. Element, Level, Zone)", Required=false, DefaultVal="Element" },
                new ParameterDef { Key="SubGroupBy",     Description="Secondary grouping mode",                            Required=false, DefaultVal="None" },
            },

            [MacroStepType.AutoNavClashUngroup] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName", Description="Name of the clash test to ungroup (reset to individual results)", Required=true },
            },

            // Applies a naming template straight through AutoNAV's ClashGrouper,
            // so playback never loads AutoNAV's rename tree.
            [MacroStepType.AutoNavRenameGroups] = new List<ParameterDef>
            {
                new ParameterDef { Key="TestName", Required=false, DefaultVal="*",
                    Description="Clash test to rename, or * for every test in the document" },
                new ParameterDef { Key="Template", Required=false, DefaultVal=AutoNavRenameTemplates.Default,
                    Description="Naming template. Tokens: " + "{TestName} {Level} {Area} {SelectionA} {SelectionB} {Month} {Day} {Year} {#}" },
                new ParameterDef { Key="Statuses", Required=false, DefaultVal="New|Active",
                    Description="Clash statuses to include, pipe-delimited (New|Active|Reviewed|Approved|Resolved)" },
            },

            [MacroStepType.AutoNavGroupWallsFloors] = new List<ParameterDef>(),

            [MacroStepType.AutoNavRunAllClashTests] = new List<ParameterDef>(),
        };

        public static List<ParameterDef> For(MacroStepType type)
        {
            return _schema.TryGetValue(type, out var defs) ? defs : new List<ParameterDef>();
        }

        public static string HintText(MacroStepType type)
        {
            var defs = For(type);
            if (defs.Count == 0) return "(no parameters for this step type)";
            var lines = new System.Text.StringBuilder();
            foreach (var d in defs)
            {
                string req = d.Required ? " [required]" : " [optional]";
                string ex  = string.IsNullOrEmpty(d.Example) ? "" : $"  e.g. \"{d.Example}\"";
                lines.AppendLine($"  {d.Key}{req} — {d.Description}{ex}");
            }
            return lines.ToString().TrimEnd();
        }
    }
}
