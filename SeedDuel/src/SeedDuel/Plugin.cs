using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityRandom = UnityEngine.Random;

namespace AeralisFoundation.SeedDuel;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "aeralisfoundation.peak.seedduel";
    public const string PluginName = "Seed Duel";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
    internal static ConfigEntry<string> RaceCode { get; private set; } = null!;
    internal static ConfigEntry<string> SeedSalt { get; private set; } = null!;
    internal static ConfigEntry<bool> UseDailyCodeWhenBlank { get; private set; } = null!;
    internal static ConfigEntry<bool> ForceMapSeed { get; private set; } = null!;
    internal static ConfigEntry<bool> LockLevelSelection { get; private set; } = null!;
    internal static ConfigEntry<bool> DeterministicMapRng { get; private set; } = null!;
    internal static ConfigEntry<bool> DeterministicLoot { get; private set; } = null!;
    internal static ConfigEntry<string> CopyCodeKey { get; private set; } = null!;

    private Harmony? Harmony;

    private void Awake()
    {
        Log = Logger;
        BindConfig();

        Harmony = new Harmony(PluginGuid);
        Harmony.PatchAll(typeof(Plugin).Assembly);
        PatchOptionalRunBoundaries();
        PatchOptionalRngScopes();

        Log.LogInfo($"{PluginName} {PluginVersion} loaded. Active race code: {SeedRules.PublicCode}");
    }

    private void Update()
    {
        if (!Enabled.Value || !TryKey(CopyCodeKey.Value, out var key) || !Input.GetKeyDown(key)) return;
        GUIUtility.systemCopyBuffer = SeedRules.PublicCode;
        Log.LogInfo($"Copied Seed Duel race code: {SeedRules.PublicCode}");
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }

    private void BindConfig()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Enables Seed Duel seed and loot synchronization.");
        RaceCode = Config.Bind("Race", "RaceCode", "", "Shared race code. Both racers should use the exact same value.");
        SeedSalt = Config.Bind("Race", "SeedSalt", "default", "Optional ruleset salt. Change only when both racers agree.");
        UseDailyCodeWhenBlank = Config.Bind("Race", "UseDailyCodeWhenBlank", true, "Uses a UTC daily code when RaceCode is blank.");
        ForceMapSeed = Config.Bind("Map", "ForceMapSeed", true, "Forces PEAK's map seed ID from the active race code.");
        LockLevelSelection = Config.Bind("Map", "LockLevelSelection", true, "Chooses the map level index deterministically from the active race code.");
        DeterministicMapRng = Config.Bind("Map", "DeterministicMapRng", true, "Wraps known map generation methods in deterministic Unity RNG scopes.");
        DeterministicLoot = Config.Bind("Loot", "DeterministicLoot", true, "Wraps known loot and item roll methods in deterministic Unity RNG scopes.");
        CopyCodeKey = Config.Bind("Controls", "CopyCodeKey", "F8", "Keyboard key that copies the active race code to the clipboard.");
    }

    private void PatchOptionalRunBoundaries()
    {
        PatchPrefix("RunManager", nameof(RunBoundaryPrefix), "StartRun", "RPC_MiniRunPlayerSpawn", "RPC_NewPlayerSpawn");
        PatchPrefix("MapHandler", nameof(RunBoundaryPrefix), "InitializeMap");
    }

    private void PatchOptionalRngScopes()
    {
        PatchScope("map", nameof(MapRngPrefix), "MapGenerator", "GenerateMap", "GenerateNodeMap", "SpawnObjectsFromNodeMap", "CreateCurveMap", "CreateHeighMap", "PopulateMapAndPlayerStates");
        PatchScope("map", nameof(MapRngPrefix), "MapHandler", "InitializeMap");
        PatchScope("loot", nameof(LootRngPrefix), "LootData", "PopulateLootData", "GetRandomItem", "GetRandomItems");
        PatchScope("loot", nameof(LootRngPrefix), "ItemDatabase", "GetRandomItem", "GetRandomItems");
        PatchScope("loot", nameof(LootRngPrefix), "Luggage", "OpenLuggageRPC");
        PatchScope("loot", nameof(LootRngPrefix), "RespawnChest", "OnRespawnChestOpened", "TriggerRespawnChestOpened");
        PatchScope("loot", nameof(LootRngPrefix), "SpawnPool", "GetSingleSpawn", "GetSpawns");
    }

    private void PatchPrefix(string typeName, string prefixName, params string[] methodNames)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null) return;

        var prefix = new HarmonyMethod(typeof(Plugin), prefixName);
        foreach (var method in MatchingMethods(type, methodNames))
        {
            try { Harmony!.Patch(method, prefix); }
            catch (Exception e) { Log.LogWarning($"Could not patch {typeName}.{method.Name}: {e.Message}"); }
        }
    }

    private void PatchScope(string label, string prefixName, string typeName, params string[] methodNames)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null) return;

        var count = 0;
        var prefix = new HarmonyMethod(typeof(Plugin), prefixName);
        var postfix = new HarmonyMethod(typeof(Plugin), nameof(RngPostfix));
        foreach (var method in MatchingMethods(type, methodNames))
        {
            try
            {
                Harmony!.Patch(method, prefix, postfix);
                count++;
            }
            catch (Exception e)
            {
                Log.LogWarning($"Could not patch {typeName}.{method.Name}: {e.Message}");
            }
        }

        if (count > 0) Log.LogInfo($"Seed Duel patched {count} {label} RNG method(s) on {typeName}.");
    }

    private static IEnumerable<MethodInfo> MatchingMethods(Type type, IReadOnlyCollection<string> names)
    {
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            if (!method.IsGenericMethodDefinition && names.Contains(method.Name))
                yield return method;
    }

    private static void RunBoundaryPrefix(MethodBase __originalMethod) => SeedRules.ResetRunCounters(__originalMethod.Name);
    private static void MapRngPrefix(MethodBase __originalMethod, object? __instance, object[] __args, ref RngPatchState __state) => SeedRules.BeginRngScope("map", __originalMethod, __instance, __args, ref __state);
    private static void LootRngPrefix(MethodBase __originalMethod, object? __instance, object[] __args, ref RngPatchState __state) => SeedRules.BeginRngScope("loot", __originalMethod, __instance, __args, ref __state);
    private static void RngPostfix(RngPatchState __state) => SeedRules.EndRngScope(__state);

    private static bool TryKey(string value, out KeyCode key) => Enum.TryParse(value.Trim(), true, out key);
}

internal static class SeedRules
{
    private static string LastToken = "";
    private static int MapCalls;
    private static int LootCalls;

    internal static string PublicCode
    {
        get
        {
            var code = Plugin.RaceCode.Value.Trim();
            if (!string.IsNullOrEmpty(code)) return code;
            return Plugin.UseDailyCodeWhenBlank.Value ? $"daily-{DateTime.UtcNow:yyyyMMdd}" : "default";
        }
    }

    private static string Token => $"SeedDuel|{PublicCode}|{Plugin.SeedSalt.Value.Trim()}";

    internal static bool TryForceBiomeId(ref string __result)
    {
        if (!Plugin.Enabled.Value || !Plugin.ForceMapSeed.Value) return true;
        ResetIfTokenChanged();
        __result = BiomeId();
        return false;
    }

    internal static void ForceLevel(MapBaker baker, ref int levelIndex)
    {
        if (!Plugin.Enabled.Value || !Plugin.LockLevelSelection.Value || baker?.selectedBiomes == null || baker.selectedBiomes.Count == 0) return;
        ResetIfTokenChanged();
        levelIndex = PositiveHash("level", Token) % baker.selectedBiomes.Count;
    }

    internal static void BeginRngScope(string scope, MethodBase method, object? instance, object[] args, ref RngPatchState state)
    {
        if (!Plugin.Enabled.Value || scope == "map" && !Plugin.DeterministicMapRng.Value || scope == "loot" && !Plugin.DeterministicLoot.Value) return;
        ResetIfTokenChanged();
        state = new RngPatchState(true, UnityRandom.state);
        var call = scope == "loot" ? ++LootCalls : ++MapCalls;
        UnityRandom.InitState(PositiveHash(scope, Token, method.DeclaringType?.FullName ?? "", method.Name, call.ToString(CultureInfo.InvariantCulture), Describe(instance), Describe(args)));
    }

    internal static void EndRngScope(RngPatchState state)
    {
        if (state.Active) UnityRandom.state = state.State;
    }

    internal static void ResetRunCounters(string reason)
    {
        if (!Plugin.Enabled.Value) return;
        ResetIfTokenChanged();
        MapCalls = 0;
        LootCalls = 0;
        Plugin.Log.LogInfo($"Seed Duel reset deterministic counters at {reason}. Code: {PublicCode}");
    }

    private static void ResetIfTokenChanged()
    {
        if (LastToken == Token) return;
        LastToken = Token;
        MapCalls = 0;
        LootCalls = 0;
    }

    private static string BiomeId()
    {
        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var value = (uint)PositiveHash("biome", Token);
        var sb = new StringBuilder(7);
        for (var i = 0; i < 7; i++)
        {
            sb.Append(chars[(int)(value % chars.Length)]);
            value /= (uint)chars.Length;
            if (value == 0) value = (uint)PositiveHash("biome", Token, i.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static int PositiveHash(params string[] parts)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var part in parts)
            {
                for (var i = 0; i < part.Length; i++)
                {
                    hash ^= part[i];
                    hash *= 16777619u;
                }
                hash ^= 31u;
                hash *= 16777619u;
            }
            return (int)(hash & 0x7fffffff);
        }
    }

    private static string Describe(object[] values)
    {
        if (values.Length == 0) return "";
        var sb = new StringBuilder();
        for (var i = 0; i < values.Length && i < 8; i++)
        {
            if (i > 0) sb.Append('|');
            sb.Append(Describe(values[i]));
        }
        return sb.ToString();
    }

    private static string Describe(object? value)
    {
        switch (value)
        {
            case null:
                return "";
            case string text:
                return text;
            case int or uint or long or ulong or short or ushort or byte or sbyte or bool:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            case float f:
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            case Component component:
                return $"{component.GetType().Name}:{component.name}:{Position(component.transform)}";
            case GameObject gameObject:
                return $"{gameObject.GetType().Name}:{gameObject.name}:{Position(gameObject.transform)}";
            case UnityEngine.Object unityObject:
                return $"{unityObject.GetType().Name}:{unityObject.name}";
            default:
                return value.GetType().FullName ?? value.GetType().Name;
        }
    }

    private static string Position(Transform transform)
    {
        var p = transform.position;
        return $"{p.x:0.##},{p.y:0.##},{p.z:0.##}";
    }
}

internal readonly struct RngPatchState
{
    internal readonly bool Active;
    internal readonly UnityRandom.State State;

    internal RngPatchState(bool active, UnityRandom.State state)
    {
        Active = active;
        State = state;
    }
}

[HarmonyPatch(typeof(MapBaker), nameof(MapBaker.GetBiomeID))]
internal static class MapBakerGetBiomeIdPatch
{
    private static bool Prefix(ref string __result) => SeedRules.TryForceBiomeId(ref __result);
}

[HarmonyPatch(typeof(MapBaker), nameof(MapBaker.GetLevel))]
internal static class MapBakerGetLevelPatch
{
    private static void Prefix(MapBaker __instance, ref int levelIndex) => SeedRules.ForceLevel(__instance, ref levelIndex);
}
