using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private enum TraceKey { F8 = 289 }
    private static class StringKeyBinding { private static bool GetKeyDownString(string key) => key == "f8"; }
    private static class EnumKeyBinding { private static bool GetKeyDownInt(TraceKey key) => key == TraceKey.F8; }
    private static class NoKeyBinding { public static int GetKeyDown(string key) => 1; }
    private sealed class SensitiveTraceArgument { public override string ToString() => throw new Exception("Must not serialize arbitrary game objects"); }
    private sealed class TracePageFixture
    {
        public int Calls;
        public void ChangePage(int page, bool animated, object details) { Calls++; }
        public int GetPage() => 1;
        public bool CanUserTurnPages() => true;
        public bool IsLeftPageInDeathKnightTab() => false;
        public bool HasPageTransition() => false;
        public void Other() { }
    }
    private sealed class TraceSoundFixture
    {
        public bool IsPlaying() => true;
        public bool IsPlaybackFinished() => false;
        public bool CanPlayClipOnExistingSource() => true;
        public object LoadClipForPlayback() => null;
        public object FindClipDefInPlaybackDef() => null;
        public void NukePlaylistsAndStopPlayingCurrentTracks() { }
        public void Play() { }
        public void LoadAndPlay() { }
        public void OnLoadAndPlaySoundLoaded() { }
    }
    private static void CheckPageDiagnostics()
    {
        foreach (Type type in new[] { typeof(StringKeyBinding), typeof(EnumKeyBinding) })
        {
            var method = CollectionPageDiagnostics.ResolveKeyDown(type, out object[] arguments);
            Check(method != null && (bool)method.Invoke(null, arguments), "F8 capture resolves stripped string and enum input bindings");
        }
        Check(CollectionPageDiagnostics.ResolveKeyDown(typeof(NoKeyBinding), out _) == null,
            "Diagnostics reject unrelated key signatures instead of adding unavailable Unity API references");
        var candidates = CollectionPageDiagnostics.CandidateMethods(typeof(TracePageFixture), false).ToArray();
        Check(candidates.Length == 1 && candidates[0].Name == "ChangePage", "Call capture excludes per-frame page getters and status probes that exhausted the in-game event budget");
        var soundCandidates = CollectionPageDiagnostics.CandidateMethods(typeof(TraceSoundFixture), true).Select(m => m.Name).ToArray();
        Check(soundCandidates.Length == 3 && soundCandidates.Contains("Play") && soundCandidates.Contains("LoadAndPlay")
            && soundCandidates.Contains("OnLoadAndPlaySoundLoaded"),
            "Sound capture keeps playback operations and excludes IsPlaying polling and unrelated playback queries");
        var fixture = new TracePageFixture();
        float savedTime = Time.unscaledTime;
        try
        {
            DebugLog.PageTraceMessages.Clear();
            CollectionPageDiagnostics.Trace(candidates[0], new object[] { 3, true, new SensitiveTraceArgument() });
            Check(DebugLog.PageTraceMessages.Count == 0, "Disabled capture produces no diagnostic call output");
            CollectionPageDiagnostics.Arm();
            FilterButtonFeedback.RunFilterAction(() => CollectionPageDiagnostics.Trace(candidates[0],
                new object[] { 3, true, new SensitiveTraceArgument() }));
            Check(DebugLog.PageTraceMessages.Any(line => line.Contains("page=3") && line.Contains("animated=True")
                && line.Contains("soundAllowed=False") && line.Contains("<SensitiveTraceArgument>")) && fixture.Calls == 0,
                "Diagnostics record arguments and sound guard state without invoking operations or serializing game objects");
            for (int i = 0; i < 220; i++) CollectionPageDiagnostics.Mark("filter refresh");
            Check(!CollectionPageDiagnostics.Recording && DebugLog.PageTraceMessages.Any(line => line.Contains("event limit")),
                "Call capture stops automatically after its event budget");
            CollectionPageDiagnostics.Arm(); Time.unscaledTime += 21f; CollectionPageDiagnostics.OnUpdate();
            Check(!CollectionPageDiagnostics.Recording && DebugLog.PageTraceMessages.Any(line => line.Contains("time limit")),
                "Idle capture stops automatically after its time budget");
            CollectionPageDiagnostics.Arm(); CollectionPageDiagnostics.Stop("collection exit");
            Check(!CollectionPageDiagnostics.Recording, "Leaving the collection stops a capture immediately");
        }
        finally { CollectionPageDiagnostics.Stop("test cleanup"); Time.unscaledTime = savedTime; }
    }
}
