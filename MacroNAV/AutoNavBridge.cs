using System;
using System.Collections.Generic;
using MacroNAV.Models;

namespace MacroNAV
{
    // Static bridge that any loaded plugin (AutoNAV, etc.) can call via reflection
    // to record its operations into MacroNAV's active recorder.
    //
    // AutoNAV calls this through MacroNAVBridge.cs without a compile-time reference:
    //   var t = Type.GetType("MacroNAV.AutoNavBridge, MacroNAV");
    //   t?.GetMethod("RecordFunction1SearchSetGen")?.Invoke(null, null);
    //
    // MacroRecorderWindow calls Register/Unregister to attach the active recorder.
    public static class AutoNavBridge
    {
        private static MacroRecorder _recorder;

        public static void Register(MacroRecorder recorder)   => _recorder = recorder;
        public static void Unregister()                        => _recorder = null;
        public static bool IsActive => _recorder?.IsRecording ?? false;

        // ── AutoNAV Function 1 ────────────────────────────────────────────────
        // Called after SearchSetGenerator.GenerateFunction1SearchSets()
        public static void RecordFunction1SearchSetGen()
        {
            if (!IsActive) return;
            _recorder.CaptureAutoNavFunction1();
        }

        // ── AutoNAV Function 2 ────────────────────────────────────────────────
        // Called after SearchSetGenerator.GenerateFunction2SearchSets()
        public static void RecordFunction2SearchSetGen(string disciplines, string propCategory, string propName)
        {
            if (!IsActive) return;
            _recorder.CaptureAutoNavFunction2(disciplines, propCategory, propName);
        }

        // ── AutoNAV Function 3 ────────────────────────────────────────────────
        // Called after SearchSetGenerator.GenerateCustomSearchSets()
        public static void RecordFunction3CustomSearchSetGen(string discipline, string propCategory, string propName)
        {
            if (!IsActive) return;
            _recorder.CaptureAutoNavFunction3(discipline, propCategory, propName);
        }

        // ── AutoNAV Function 4 ────────────────────────────────────────────────
        // Called after ClashTestGeneratorEngine.GenerateClashTests()
        public static void RecordClashTestGen()
        {
            if (!IsActive) return;
            _recorder.CaptureAutoNavClashTestGen();
        }

        // ── AutoNAV Function 5 ────────────────────────────────────────────────
        // Called after ClashTestGeneratorEngine.RunClashTestsAndGroupResults()
        public static void RecordClashRunAndGroup(string primaryGroupBy, string subGroupBy)
        {
            if (!IsActive) return;
            _recorder.CaptureAutoNavClashRunAndGroup(primaryGroupBy, subGroupBy);
        }

        // ── AutoNAV Function 6/7 ─────────────────────────────────────────────
        // Called after ClashGrouper.GroupClashes()
        public static void RecordClashGroup(string testName, string primaryGroupBy, string subGroupBy)
        {
            if (!IsActive) return;
            _recorder.CaptureAutoNavClashGroup(testName, primaryGroupBy, subGroupBy);
        }

        // Called after ClashGrouper.UnGroupClashes()
        public static void RecordClashUngroup(string testName)
        {
            if (!IsActive) return;
            _recorder.CaptureAutoNavClashUngroup(testName);
        }
    }
}
