using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PeakAllBiomeRoute;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.aeralis.peak.allbiomeroute";
    public const string PluginName = "Peak All Biome Route";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    private Harmony? _harmony;

    private void Awake()
    {
        Log = Logger;
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. Route: {AllBiomeRoute.RouteText}");
    }

    private void OnDestroy() => _harmony?.UnpatchSelf();
}

internal static class AllBiomeRoute
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
    private static int? _forcedLevelIndex;
    private static bool _loggedApplied;

    internal static void ForceLevel(MapBaker baker, ref int levelIndex)
    {
        if (!baker) return;

        try
        {
            _forcedLevelIndex ??= FindBaseLevel(baker);
            levelIndex = _forcedLevelIndex.Value;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"All-biome level selection fell back to vanilla: {e.Message}");
        }
    }

    internal static bool ForceBiomeId(ref string __result)
    {
        __result = BiomeId;
        return false;
    }

    internal static void ForceTodayIcons(ref string biomeString) => biomeString = BiomeId;

    internal static void Apply(MapHandler handler)
    {
        if (!handler) return;

        try
        {
            var chosen = BuildRoute(handler);
            if (chosen.Count == 0) return;

            handler.segments = chosen.ToArray();
            ApplyBiomeList(handler, chosen);

            if (_loggedApplied) return;

            _loggedApplied = true;
            Plugin.Log.LogInfo($"Applied all-biome route: {Describe(handler.biomes)}");
            if (chosen.Count != Route.Length) Plugin.Log.LogWarning($"Scene exposed {chosen.Count}/{Route.Length} requested route biomes.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Failed to apply all-biome route: {e}");
        }
    }

    internal static void ApplyBiomeList(MapHandler handler)
    {
        if (!handler) return;

        try
        {
            ApplyBiomeList(handler, handler.segments?.Where(segment => segment != null).ToList() ?? []);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Failed to refresh all-biome list: {e.Message}");
        }
    }

    private static void ApplyBiomeList(MapHandler handler, IReadOnlyList<MapHandler.MapSegment> route)
    {
        if (handler.biomes == null || route.Count == 0) return;

        handler.biomes.Clear();
        foreach (var segment in route)
        {
            var biome = GetBiome(segment);
            if (!handler.biomes.Contains(biome)) handler.biomes.Add(biome);
        }
    }

    private static List<MapHandler.MapSegment> BuildRoute(MapHandler handler)
    {
        var candidates = AllSegments(handler).ToList();
        var chosen = new List<MapHandler.MapSegment>();
        var used = new HashSet<MapHandler.MapSegment>();

        foreach (var biome in Route)
        {
            var segment = candidates.FirstOrDefault(candidate => !used.Contains(candidate) && GetBiome(candidate) == biome);
            if (segment == null)
            {
                Plugin.Log.LogWarning($"No map segment found for {Name(biome)}.");
                continue;
            }

            segment.hasVariant = false;
            chosen.Add(segment);
            used.Add(segment);
        }

        if (chosen.Count == 0) return chosen;

        foreach (var segment in handler.segments ?? [])
            if (segment != null && !used.Contains(segment) && GetBiome(segment) == Biome.BiomeType.Peak) chosen.Add(segment);

        return chosen;
    }

    private static IEnumerable<MapHandler.MapSegment> AllSegments(MapHandler handler)
    {
        foreach (var segment in handler.segments ?? [])
            if (segment != null) yield return segment;

        foreach (var segment in handler.variantSegments ?? [])
            if (segment != null) yield return segment;
    }

    private static Biome.BiomeType GetBiome(MapHandler.MapSegment segment)
    {
        if (segment.isVariant) return segment.variantBiome;
        return segment._biome;
    }

    private static int FindBaseLevel(MapBaker baker)
    {
        if (baker.BiomeIDs == null || baker.BiomeIDs.Count == 0) return 0;

        for (var i = 0; i < baker.BiomeIDs.Count; i++)
            if (string.Equals(baker.BiomeIDs[i], "STAV", StringComparison.OrdinalIgnoreCase)) return i;

        for (var i = 0; i < baker.BiomeIDs.Count; i++)
        {
            var id = baker.BiomeIDs[i] ?? string.Empty;
            if (id.Contains("T") && id.Contains("A") && id.Contains("V")) return i;
        }

        return 0;
    }

    private static string Describe(IEnumerable<Biome.BiomeType> biomes) => string.Join(" -> ", biomes.Select(Name));

    private static string Name(Biome.BiomeType biome) => biome switch
    {
        Biome.BiomeType.Shore => "Shore",
        Biome.BiomeType.Tropics => "Tropics",
        Biome.BiomeType.Roots => "Roots",
        Biome.BiomeType.Alpine => "Alpine",
        Biome.BiomeType.Mesa => "Mesa",
        Biome.BiomeType.Volcano => "Caldera",
        Biome.BiomeType.Peak => "Kiln",
        _ => biome.ToString()
    };
}

[HarmonyPatch(typeof(MapBaker), nameof(MapBaker.GetLevel))]
internal static class MapBakerGetLevelPatch
{
    private static void Prefix(MapBaker __instance, ref int __0) => AllBiomeRoute.ForceLevel(__instance, ref __0);
}

[HarmonyPatch(typeof(MapBaker), nameof(MapBaker.GetBiomeID))]
internal static class MapBakerGetBiomeIdPatch
{
    private static bool Prefix(ref string __result) => AllBiomeRoute.ForceBiomeId(ref __result);
}

[HarmonyPatch(typeof(TodaysBiomes), nameof(TodaysBiomes.SetBiomes))]
internal static class TodaysBiomesSetBiomesPatch
{
    private static void Prefix(ref string __0) => AllBiomeRoute.ForceTodayIcons(ref __0);
}

[HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
internal static class MapHandlerInitializeMapPatch
{
    private static void Prefix(MapHandler __instance) => AllBiomeRoute.Apply(__instance);
}

[HarmonyPatch(typeof(MapHandler), nameof(MapHandler.DetectBiomes))]
internal static class MapHandlerDetectBiomesPatch
{
    private static void Postfix(MapHandler __instance) => AllBiomeRoute.ApplyBiomeList(__instance);
}
