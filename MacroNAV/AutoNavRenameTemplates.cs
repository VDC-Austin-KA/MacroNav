using System.Collections.Generic;

namespace MacroNAV
{
    // AutoNAV's built-in clash-group naming templates, mirrored here so a macro
    // can pick one without opening AutoNAV's rename tab (which loads the
    // dashboard and every test/group before it will let you choose).
    //
    // Tokens are resolved by AutoNAV.ClashGrouper.ApplyNamingTemplate from its
    // NamingContext: Month, Day, Year, Level, Area, TestName, SelectionA,
    // SelectionB. {#} is the per-name counter used to keep names unique.
    public static class AutoNavRenameTemplates
    {
        public const string LevelArea =
            "{Level}_{Area} | {TestName} - {SelectionA} vs {SelectionB} {#}";

        public const string DatedLevelArea =
            "{Month}/{Day}_{Level}_{Area} | {TestName} - {SelectionA} vs {SelectionB} {#}";

        public const string TestLevelSelections =
            "{TestName} | {Level}_ {SelectionA} vs {SelectionB} {#}";

        public const string TestLevelArea =
            "{TestName} | {Level}_{Area} {#}";

        // Used when a rename step carries no template.
        public const string Default = LevelArea;

        // Label -> template, for the step editor dropdown.
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Presets =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Level + Area (default)",        LevelArea),
                new KeyValuePair<string, string>("Dated, Level + Area",           DatedLevelArea),
                new KeyValuePair<string, string>("Test + Level + Selections",     TestLevelSelections),
                new KeyValuePair<string, string>("Test + Level + Area (compact)", TestLevelArea),
            };

        public static readonly IReadOnlyList<string> Tokens = new List<string>
        {
            "{TestName}", "{Level}", "{Area}", "{SelectionA}", "{SelectionB}",
            "{Month}", "{Day}", "{Year}", "{#}",
        };
    }
}
