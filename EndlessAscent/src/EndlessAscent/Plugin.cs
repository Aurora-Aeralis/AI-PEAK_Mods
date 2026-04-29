using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace AeralisFoundation.EndlessAscent;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.aeralisfoundation.peak.endlessascent";
    public const string PluginName = "Endless Ascent";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
    internal static ConfigEntry<bool> AdvanceAscent { get; private set; } = null!;
    internal static ConfigEntry<int> LevelStep { get; private set; } = null!;

    private Harmony? harmony;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Continue into another island after a successful run.");
        AdvanceAscent = Config.Bind("Run", "AdvanceAscent", true, "Increase the PEAK ascent value before loading the next island.");
        LevelStep = Config.Bind("Run", "LevelStep", 1, "Generated level index step used for each endless continuation.");

        harmony = new Harmony(PluginGuid);
        Patch("Character", "RPCEndGame", prefix: nameof(CharacterRpcEndGamePrefix));
        Patch("PeakHandler", "EndScreenComplete", prefix: nameof(PeakHandlerEndScreenCompletePrefix));
        Patch("GameOverHandler", "BeginAirportLoadRPC", prefix: nameof(GameOverHandlerBeginAirportLoadRpcPrefix));
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    private void Patch(string typeName, string methodName, string? prefix = null, string? postfix = null)
    {
        var type = AccessTools.TypeByName(typeName);
        var original = type == null ? null : AccessTools.Method(type, methodName);
        if (original == null)
        {
            Log.LogWarning($"Could not patch {typeName}.{methodName}; method not found.");
            return;
        }

        harmony!.Patch(original,
            prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
    }

    private static void CharacterRpcEndGamePrefix() => EndlessRun.MarkVictoryIfWon();
    private static bool PeakHandlerEndScreenCompletePrefix() => EndlessRun.TryReplaceAirportTransition("end screen complete");
    private static bool GameOverHandlerBeginAirportLoadRpcPrefix() => EndlessRun.TryReplaceAirportTransition("airport transition");
}

internal static class EndlessRun
{
    private static bool pendingVictory;
    private static int completedLevels;

    internal static void MarkVictoryIfWon()
    {
        if (!Plugin.Enabled.Value) return;
        pendingVictory = HasWinningCharacter();
        if (pendingVictory) Plugin.Log.LogInfo("Endless Ascent captured a successful run completion.");
    }

    internal static bool TryReplaceAirportTransition(string source)
    {
        if (!Plugin.Enabled.Value || !pendingVictory) return true;

        try
        {
            return LoadNextIsland(source) ? false : true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Endless Ascent could not load the next island; falling back to vanilla airport load. {e}");
            pendingVictory = false;
            return true;
        }
    }

    private static bool LoadNextIsland(string source)
    {
        var loadingHandler = GetSingleton("LoadingScreenHandler");
        if (loadingHandler == null)
        {
            Plugin.Log.LogWarning("Endless Ascent could not find LoadingScreenHandler; vanilla airport load will continue.");
            pendingVictory = false;
            return false;
        }

        if (IsLoading(loadingHandler))
        {
            Plugin.Log.LogInfo("Endless Ascent skipped a duplicate transition because a loading screen is already active.");
            pendingVictory = false;
            return true;
        }

        var scene = ResolveNextScene(out var levelIndex);
        if (string.IsNullOrEmpty(scene))
        {
            Plugin.Log.LogWarning("Endless Ascent could not resolve the next island scene; vanilla airport load will continue.");
            pendingVictory = false;
            return false;
        }

        AddSceneSwitchingStatus();
        ApplyCurrentRunSettings();
        var ascent = ApplyNextAscent();
        if (!StartIslandLoad(loadingHandler, scene))
        {
            pendingVictory = false;
            return false;
        }

        completedLevels++;
        pendingVictory = false;
        Plugin.Log.LogInfo($"Endless Ascent loaded {scene} from {source}. Chain={completedLevels}, LevelIndex={levelIndex}, Ascent={ascent}.");
        return true;
    }

    private static bool HasWinningCharacter()
    {
        var characterType = AccessTools.TypeByName("Character");
        var allCharacters = AccessTools.Field(characterType, "AllCharacters")?.GetValue(null) as IEnumerable;
        var checkWin = AccessTools.Method(characterType, "CheckWinCondition");
        if (allCharacters == null || checkWin == null) return false;

        foreach (var character in allCharacters)
            if (character != null && checkWin.Invoke(null, new[] { character }) is true)
                return true;
        return false;
    }

    private static string ResolveNextScene(out int levelIndex)
    {
        levelIndex = 0;
        var baker = GetSingleton("MapBaker");
        if (baker == null) return "";

        var bakerType = baker.GetType();
        var total = (AccessTools.PropertyGetter(bakerType, "AllLevels")?.Invoke(baker, null) as Array)?.Length ?? 0;
        var step = Math.Max(1, Plugin.LevelStep.Value);
        levelIndex = GetCurrentLevelIndex() + step + completedLevels * step;
        if (total > 0) levelIndex = ((levelIndex % total) + total) % total;

        var scene = AccessTools.Method(bakerType, "GetLevel")?.Invoke(baker, new object[] { levelIndex }) as string;
        return string.IsNullOrEmpty(scene) ? "WilIsland" : scene;
    }

    private static int GetCurrentLevelIndex()
    {
        try
        {
            var serviceType = AccessTools.TypeByName("NextLevelService");
            var gameHandlerType = AccessTools.TypeByName("GameHandler");
            var getService = gameHandlerType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "GetService" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
            var service = serviceType == null ? null : getService?.MakeGenericMethod(serviceType).Invoke(null, null);
            return service == null ? 0 : (int)(AccessTools.PropertyGetter(serviceType, "NextLevelIndexOrFallback")?.Invoke(service, null) ?? 0);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Endless Ascent could not read NextLevelService; using level index 0. {e.Message}");
            return 0;
        }
    }

    private static int ApplyNextAscent()
    {
        var ascents = AccessTools.TypeByName("Ascents");
        var get = AccessTools.PropertyGetter(ascents, "currentAscent");
        var set = AccessTools.PropertySetter(ascents, "currentAscent");
        var current = get == null ? 0 : (int)(get.Invoke(null, null) ?? 0);
        var next = Plugin.AdvanceAscent.Value ? Math.Max(0, current + 1) : current;
        set?.Invoke(null, new object[] { next });
        return next;
    }

    private static void ApplyCurrentRunSettings()
    {
        var runSettings = AccessTools.TypeByName("RunSettings");
        var gameUtils = AccessTools.TypeByName("GameUtils");
        var data = AccessTools.Method(runSettings, "GetSerializedRunSettings")?.Invoke(null, null);
        if (data is byte[] bytes) AccessTools.Method(gameUtils, "ApplySerializedRunSettings")?.Invoke(null, new object[] { bytes });
    }

    private static bool StartIslandLoad(object loadingHandler, string scene)
    {
        var loadingType = loadingHandler.GetType();
        var process = AccessTools.Method(loadingType, "LoadSceneProcess", new[] { typeof(string), typeof(bool), typeof(bool), typeof(float) })
            ?.Invoke(loadingHandler, new object[] { scene, true, true, 0f }) as IEnumerator;
        if (process == null) return false;

        var screenType = AccessTools.TypeByName("LoadingScreen+LoadingScreenType") ?? AccessTools.TypeByName("LoadingScreen/LoadingScreenType");
        var load = screenType == null ? null : AccessTools.Method(loadingType, "Load", new[] { screenType, typeof(Action), typeof(IEnumerator[]) });
        if (load == null) return false;

        load.Invoke(loadingHandler, new object?[] { Enum.ToObject(screenType, 1), null, new[] { process } });
        return true;
    }

    private static bool IsLoading(object loadingHandler)
    {
        try { return AccessTools.PropertyGetter(loadingHandler.GetType(), "loading")?.Invoke(loadingHandler, null) is true; }
        catch { return false; }
    }

    private static void AddSceneSwitchingStatus()
    {
        var statusType = AccessTools.TypeByName("SceneSwitchingStatus");
        var gameHandlerType = AccessTools.TypeByName("GameHandler");
        var addStatus = gameHandlerType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "AddStatus" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
        if (statusType != null) addStatus?.MakeGenericMethod(statusType).Invoke(null, new[] { Activator.CreateInstance(statusType) });
    }

    private static object? GetSingleton(string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null) return null;
        return (AccessTools.PropertyGetter(type, "Instance") ?? AccessTools.Method(type, "get_Instance"))?.Invoke(null, null);
    }
}
