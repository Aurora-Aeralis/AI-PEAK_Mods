using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PeakGrandTour;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.aeralis.peak.grandtour";
    public const string PluginName = "Peak Grand Tour";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    private Harmony? _harmony;

    private void Awake()
    {
        Log = Logger;
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. Route: {GrandTour.RouteText}");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}

internal static class GrandTour
{
    internal const string RouteText = "Shore -> Tropics -> Roots -> Alpine -> Mesa -> Caldera -> Kiln";
    private const string BiomeId = "STRAMVK";
    private static readonly Biome.BiomeType[] Route =
    [
        Biome.BiomeType.Shore,
        Biome.BiomeType.Tropics,
        Biome.BiomeType.Roots,
        Biome.BiomeType.Alpine,
        Biome.BiomeType.Mesa,
        Biome.BiomeType.Volcano,
        Biome.BiomeType.Peak
    ];
    private static int? _levelIndex;
    private static bool _appliedToMap;

    internal static void ForceLevel(MapBaker baker, ref int levelIndex)
    {
        if (!baker) return;
        try
        {
            _levelIndex ??= FindBaseLevel(baker);
            if (_levelIndex is int forced) levelIndex = forced;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Grand Tour level selection fell back to vanilla: {e.Message}");
        }
    }

    internal static bool ForceBiomeId(ref string __result)
    {
        __result = BiomeId;
        return false;
    }

    internal static void Apply(MapHandler handler)
    {
        if (!handler) return;
        try
        {
            var chosen = BuildRoute(handler);
            if (chosen.Count == 0) return;

            handler.segments = chosen.ToArray();
            handler.biomes.Clear();
            handler.biomes.AddRange(chosen.Select(segment => segment._biome));

            if (!_appliedToMap)
            {
                _appliedToMap = true;
                Plugin.Log.LogInfo($"Applied Grand Tour route: {Describe(handler.biomes)}");
                if (chosen.Count != Route.Length) Plugin.Log.LogWarning($"Scene only exposed {chosen.Count}/{Route.Length} requested route biomes.");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Failed to apply Grand Tour route: {e}");
        }
    }

    private static List<MapHandler.MapSegment> BuildRoute(MapHandler handler)
    {
        var source = AllSegments(handler).GroupBy(segment => segment._biome).ToDictionary(group => group.Key, group => group.First());
        var chosen = new List<MapHandler.MapSegment>();

        foreach (var biome in Route)
        {
            if (!source.TryGetValue(biome, out var segment))
            {
                Plugin.Log.LogWarning($"No map segment found for {Name(biome)}.");
                continue;
            }

            segment.hasVariant = false;
            chosen.Add(segment);
        }

        return chosen;
    }

    private static IEnumerable<MapHandler.MapSegment> AllSegments(MapHandler handler)
    {
        if (handler.segments != null)
        {
            foreach (var segment in handler.segments)
                if (segment != null) yield return segment;
        }

        if (handler.variantSegments != null)
        {
            foreach (var segment in handler.variantSegments)
                if (segment != null) yield return segment;
        }
    }

    private static int FindBaseLevel(MapBaker baker)
    {
        if (baker.selectedBiomes == null || baker.selectedBiomes.Count == 0) return 0;

        var best = 0;
        var bestScore = int.MinValue;

        for (var i = 0; i < baker.selectedBiomes.Count; i++)
        {
            var biomes = baker.selectedBiomes[i].biomeTypes;
            var score = Score(biomes);
            if (score <= bestScore) continue;
            best = i;
            bestScore = score;
        }

        Plugin.Log.LogInfo($"Using generated level {best} as Grand Tour base.");
        return best;
    }

    private static int Score(IReadOnlyList<Biome.BiomeType> biomes)
    {
        var score = 0;
        var cursor = 0;

        foreach (var biome in biomes)
        {
            for (var i = cursor; i < Route.Length; i++)
            {
                if (Route[i] != biome) continue;
                score += 10;
                cursor = i + 1;
                break;
            }

            if (Route.Contains(biome)) score++;
        }

        return score;
    }

    private static string Describe(IEnumerable<Biome.BiomeType> biomes) => string.Join(" -> ", biomes.Select(Name));

    private static string Name(Biome.BiomeType biome) => biome switch
    {
        Biome.BiomeType.Volcano => "Caldera",
        Biome.BiomeType.Peak => "Kiln",
        _ => biome.ToString()
    };
}

[HarmonyPatch(typeof(MapBaker), nameof(MapBaker.GetLevel))]
internal static class MapBakerGetLevelPatch
{
    private static void Prefix(MapBaker __instance, ref int levelIndex) => GrandTour.ForceLevel(__instance, ref levelIndex);
}

[HarmonyPatch(typeof(MapBaker), nameof(MapBaker.GetBiomeID))]
internal static class MapBakerGetBiomeIdPatch
{
    private static bool Prefix(ref string __result) => GrandTour.ForceBiomeId(ref __result);
}

[HarmonyPatch(typeof(MapHandler), nameof(MapHandler.Awake))]
internal static class MapHandlerAwakePatch
{
    private static void Postfix(MapHandler __instance) => GrandTour.Apply(__instance);
}

[HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
internal static class MapHandlerInitializeMapPatch
{
    private static void Prefix(MapHandler __instance) => GrandTour.Apply(__instance);
}

[HarmonyPatch(typeof(MountainProgressHandler), nameof(MountainProgressHandler.InitProgressPoints))]
internal static class MountainProgressHandlerInitProgressPointsPatch
{
    private static void Prefix()
    {
        if (MapHandler.Instance) GrandTour.Apply(MapHandler.Instance);
    }
}
