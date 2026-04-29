using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AeralisFoundation.ScoutmasterGroupPressure;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "com.aeralisfoundation.peak.scoutmastergrouppressure";
    internal const string PluginName = "Scoutmaster Group Pressure";
    internal const string PluginVersion = "0.1.0";

    internal static ConfigEntry<int> MinimumLivingScouts = null!;
    internal static ConfigEntry<int> GroupReferenceRank = null!;
    internal static ConfigEntry<float> GroupThresholdMultiplier = null!;
    internal static ManualLogSource Log = null!;

    private Harmony? harmony;

    private void Awake()
    {
        Log = Logger;
        MinimumLivingScouts = Config.Bind("Trigger", "MinimumLivingScouts", 4, new ConfigDescription("Minimum living scouts required before the group runaway rule is active.", new AcceptableValueRange<int>(4, 16)));
        GroupReferenceRank = Config.Bind("Trigger", "GroupReferenceRank", 2, new ConfigDescription("Leading scout rank compared against the third-highest living scout. Use 1 or 2.", new AcceptableValueRange<int>(1, 2)));
        GroupThresholdMultiplier = Config.Bind("Trigger", "GroupThresholdMultiplier", 1.5f, new ConfigDescription("Multiplier applied to the vanilla scoutmaster height delta for the group runaway rule.", new AcceptableValueRange<float>(1f, 3f)));
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();
}

internal static class ScoutmasterRules
{
    private static readonly FieldInfo? SinceLookForTarget = AccessTools.Field(typeof(Scoutmaster), "sinceLookForTarget");
    private static readonly FieldInfo? AttackHeightDelta = AccessTools.Field(typeof(Scoutmaster), "attackHeightDelta");
    private static readonly FieldInfo? MaxAggroHeight = AccessTools.Field(typeof(Scoutmaster), "maxAggroHeight");
    private static readonly MethodInfo? SetCurrentTarget = AccessTools.Method(typeof(Scoutmaster), "SetCurrentTarget", new[] { typeof(Character), typeof(float) });
    private static readonly MethodInfo? PreventSpawningGetter = AccessTools.PropertyGetter(typeof(Scoutmaster), "preventSpawning");
    private static bool Ready => SinceLookForTarget != null && AttackHeightDelta != null && MaxAggroHeight != null && SetCurrentTarget != null;

    internal static bool TryAcquire(Scoutmaster scoutmaster)
    {
        if (!Ready) return false;
        var scouts = LivingScouts();
        if (scouts.Count < Plugin.MinimumLivingScouts.Value) return false;
        if (GetFloat(SinceLookForTarget, scoutmaster) < 30f) return true;
        SetFloat(SinceLookForTarget, scoutmaster, 0f);
        if (UnityEngine.Random.value > 0.1f) return true;
        if (TryGetTarget(scoutmaster, scouts, 0f, false, out var target)) SetTarget(scoutmaster, target);
        return true;
    }

    internal static bool Verify(Scoutmaster scoutmaster)
    {
        if (!Ready) return false;
        var scouts = LivingScouts();
        if (scouts.Count < Plugin.MinimumLivingScouts.Value) return false;
        if (PreventSpawning(scoutmaster) || !TryGetTarget(scoutmaster, scouts, -20f, true, out var target) || scoutmaster.currentTarget != target) SetTarget(scoutmaster, null);
        return true;
    }

    private static bool TryGetTarget(Scoutmaster scoutmaster, List<Character> scouts, float hysteresis, bool verify, out Character target)
    {
        scouts.Sort((a, b) => b.Center.y.CompareTo(a.Center.y));
        target = scouts[0];
        if (target.Center.y >= GetFloat(MaxAggroHeight, scoutmaster, float.MaxValue)) return false;
        var soloActive = IsAbove(target, scouts[1], GetFloat(AttackHeightDelta, scoutmaster) + hysteresis);
        if (soloActive && (!verify || ClosestOther(target, scouts) is not { } closest || Vector3.Distance(closest.Center, target.Center) >= 15f)) return true;
        if (scouts.Count < 3) return false;
        var reference = scouts[Mathf.Clamp(Plugin.GroupReferenceRank.Value, 1, 2) - 1];
        return IsAbove(reference, scouts[2], GetFloat(AttackHeightDelta, scoutmaster) * Plugin.GroupThresholdMultiplier.Value + hysteresis);
    }

    private static List<Character> LivingScouts()
    {
        var scouts = new List<Character>();
        foreach (var character in Character.AllCharacters)
        {
            if (!character || character.isBot || character.isScoutmaster || character.data.dead || character.data.fullyPassedOut) continue;
            scouts.Add(character);
        }
        return scouts;
    }

    private static Character? ClosestOther(Character target, List<Character> scouts)
    {
        Character? closest = null;
        var distance = float.MaxValue;
        foreach (var scout in scouts)
        {
            if (scout == target) continue;
            var next = Vector3.Distance(scout.Center, target.Center);
            if (next >= distance) continue;
            closest = scout;
            distance = next;
        }
        return closest;
    }

    private static bool IsAbove(Character leader, Character follower, float threshold) => leader.Center.y > follower.Center.y + Mathf.Max(0f, threshold);
    private static float GetFloat(FieldInfo? field, Scoutmaster scoutmaster, float fallback = 0f) => field?.GetValue(scoutmaster) is float value ? value : fallback;
    private static void SetFloat(FieldInfo? field, Scoutmaster scoutmaster, float value) => field?.SetValue(scoutmaster, value);
    private static bool PreventSpawning(Scoutmaster scoutmaster) => PreventSpawningGetter?.Invoke(scoutmaster, null) is true;
    private static void SetTarget(Scoutmaster scoutmaster, Character? target) => SetCurrentTarget?.Invoke(scoutmaster, new object?[] { target, 0f });
}

[HarmonyPatch(typeof(Scoutmaster), "LookForTarget")]
internal static class ScoutmasterLookForTargetPatch
{
    private static bool Prefix(Scoutmaster __instance) => !ScoutmasterRules.TryAcquire(__instance);
}

[HarmonyPatch(typeof(Scoutmaster), "VerifyTarget")]
internal static class ScoutmasterVerifyTargetPatch
{
    private static bool Prefix(Scoutmaster __instance) => !ScoutmasterRules.Verify(__instance);
}
