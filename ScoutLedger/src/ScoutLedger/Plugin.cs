using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AeralisFoundation.ScoutLedger;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "com.aeralisfoundation.peak.scoutledger";
    internal const string PluginName = "Scout Ledger";
    internal const string PluginVersion = "0.1.0";

    private static readonly (string Key, string Label, string Description)[] Stats =
    {
        (StatKeys.PeaksReached, "Peaks reached", "Times the local player stat for reaching the peak was incremented."),
        (StatKeys.ScoutsRevived, "Scouts revived", "Direct scout revive completions performed by the local player."),
        (StatKeys.ReviveChestActivations, "Revive chest activations", "Respawn chest revive activations completed by the local player."),
        (StatKeys.ScoutsCannibalized, "Scouts cannibalized", "Scouts cannibalized by the local player."),
        (StatKeys.PoisonHazardsTriggered, "Poison hazards stepped on", "Poison trigger hazards touched by the local player."),
        (StatKeys.PoisonTaken, "Poison status gains", "Positive poison status applications on the local player."),
        (StatKeys.ThornsTaken, "Thorn hits", "Positive thorn status applications on the local player."),
        (StatKeys.SporesTaken, "Spore hits", "Positive spore status applications on the local player."),
        (StatKeys.InjuriesTaken, "Injury hits", "Positive injury status applications on the local player."),
        (StatKeys.PassedOut, "Times passed out", "Local player pass-out events."),
        (StatKeys.Deaths, "Deaths", "Local player death events."),
        (StatKeys.ItemsConsumed, "Items consumed", "Items consumed by the local player."),
        (StatKeys.LuggageOpened, "Luggage opened", "Luggage searches completed by the local player.")
    };

    internal static Plugin? Instance { get; private set; }
    internal static ManualLogSource Log { get; private set; } = null!;

    private readonly Dictionary<string, ConfigEntry<int>> counters = new();
    private ConfigEntry<bool> showOverlay = null!;
    private ConfigEntry<KeyboardShortcut> toggleOverlay = null!;
    private ConfigEntry<KeyboardShortcut> resetCounters = null!;
    private Harmony? harmony;
    private Rect windowRect = new(20f, 80f, 360f, 408f);
    private string notice = "";
    private float noticeUntil;
    private float saveAfter;
    private bool dirty;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Config.SaveOnConfigSet = false;
        showOverlay = Config.Bind("Display", "ShowOverlay", true, "Show the Scout Ledger overlay.");
        toggleOverlay = Config.Bind("Controls", "ToggleOverlay", new KeyboardShortcut(KeyCode.F8), "Toggle the Scout Ledger overlay.");
        resetCounters = Config.Bind("Controls", "ResetCounters", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl), "Reset all Scout Ledger counters.");
        foreach (var stat in Stats) counters[stat.Key] = Config.Bind("Counters", stat.Key, 0, stat.Description);
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (toggleOverlay.Value.IsDown())
        {
            showOverlay.Value = !showOverlay.Value;
            Config.Save();
        }
        if (resetCounters.Value.IsDown()) ResetAll();
        if (dirty && Time.realtimeSinceStartup >= saveAfter) SaveCounters();
    }

    private void OnGUI()
    {
        if (showOverlay.Value) windowRect = GUILayout.Window(824190, windowRect, DrawWindow, PluginName);
        if (noticeUntil <= Time.realtimeSinceStartup) return;
        GUI.Box(new Rect(20f, 40f, 360f, 28f), notice);
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        if (dirty) Config.Save();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    internal static void Count(string key, int amount = 1) => Instance?.Increment(key, amount);

    internal static bool IsLocal(Character? character)
    {
        if (character == null) return false;
        try
        {
            return character.IsLocal || ReferenceEquals(Character.localCharacter, character);
        }
        catch
        {
            return ReferenceEquals(Character.localCharacter, character);
        }
    }

    internal static bool IsRecoverable(Character? character)
    {
        if (character?.data == null) return false;
        return character.data.passedOut || character.data.fullyPassedOut || character.data.dead;
    }

    internal static Character? CharacterFrom(Collider? collider) => collider == null ? null : collider.GetComponentInParent<Character>();

    private void Increment(string key, int amount)
    {
        if (amount <= 0 || !counters.TryGetValue(key, out var counter)) return;
        counter.Value = Math.Max(0, counter.Value + amount);
        dirty = true;
        saveAfter = Time.realtimeSinceStartup + 1.5f;
        notice = $"{LabelFor(key)}: {counter.Value}";
        noticeUntil = Time.realtimeSinceStartup + 2.5f;
    }

    private void ResetAll()
    {
        foreach (var counter in counters.Values) counter.Value = 0;
        notice = "Scout Ledger counters reset";
        noticeUntil = Time.realtimeSinceStartup + 2.5f;
        SaveCounters();
    }

    private void SaveCounters()
    {
        Config.Save();
        dirty = false;
    }

    private void DrawWindow(int id)
    {
        foreach (var stat in Stats)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(stat.Label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(counters[stat.Key].Value.ToString(), GUILayout.Width(52f));
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(6f);
        GUILayout.Label($"Toggle: {toggleOverlay.Value}");
        GUILayout.Label($"Reset: {resetCounters.Value}");
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    private static string LabelFor(string key)
    {
        foreach (var stat in Stats)
            if (stat.Key == key)
                return stat.Label;
        return key;
    }
}

internal static class StatKeys
{
    internal const string PeaksReached = "PeaksReached";
    internal const string ScoutsRevived = "ScoutsRevived";
    internal const string ReviveChestActivations = "ReviveChestActivations";
    internal const string ScoutsCannibalized = "ScoutsCannibalized";
    internal const string PoisonHazardsTriggered = "PoisonHazardsTriggered";
    internal const string PoisonTaken = "PoisonTaken";
    internal const string ThornsTaken = "ThornsTaken";
    internal const string SporesTaken = "SporesTaken";
    internal const string InjuriesTaken = "InjuriesTaken";
    internal const string PassedOut = "PassedOut";
    internal const string Deaths = "Deaths";
    internal const string ItemsConsumed = "ItemsConsumed";
    internal const string LuggageOpened = "LuggageOpened";
}

[HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.IncrementSteamStat))]
internal static class AchievementManagerIncrementSteamStatPatch
{
    private static void Prefix(STEAMSTATTYPE steamStatType, int value)
    {
        if (steamStatType == STEAMSTATTYPE.TimesPeaked) Plugin.Count(StatKeys.PeaksReached, value);
    }
}

[HarmonyPatch(typeof(CharacterInteractible), nameof(CharacterInteractible.Interact_CastFinished))]
internal static class CharacterInteractibleCastFinishedPatch
{
    private static void Prefix(CharacterInteractible __instance, Character interactor, ref bool __state)
    {
        __state = Plugin.IsLocal(interactor) && !__instance.IsCannibal() && !ReferenceEquals(__instance.character, interactor) && Plugin.IsRecoverable(__instance.character);
    }

    private static void Postfix(bool __state)
    {
        if (__state) Plugin.Count(StatKeys.ScoutsRevived);
    }
}

[HarmonyPatch(typeof(Skelleton), nameof(Skelleton.Interact_CastFinished))]
internal static class SkeletonCastFinishedPatch
{
    private static void Prefix(Skelleton __instance, Character interactor, ref bool __state)
    {
        __state = Plugin.IsLocal(interactor) && __instance.spawnedFromCharacter != null && !ReferenceEquals(__instance.spawnedFromCharacter, interactor);
    }

    private static void Postfix(bool __state)
    {
        if (__state) Plugin.Count(StatKeys.ScoutsRevived);
    }
}

[HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.Interact_CastFinished))]
internal static class RespawnChestCastFinishedPatch
{
    private static void Prefix(Character interactor, ref bool __state)
    {
        __state = Plugin.IsLocal(interactor);
    }

    private static void Postfix(bool __state)
    {
        if (__state) Plugin.Count(StatKeys.ReviveChestActivations);
    }
}

[HarmonyPatch(typeof(CharacterInteractible), nameof(CharacterInteractible.GetEaten))]
internal static class CharacterInteractibleGetEatenPatch
{
    private static void Prefix(CharacterInteractible __instance, Character eater, ref bool __state)
    {
        __state = Plugin.IsLocal(eater) && __instance.character != null && !ReferenceEquals(__instance.character, eater);
    }

    private static void Postfix(bool __state)
    {
        if (__state) Plugin.Count(StatKeys.ScoutsCannibalized);
    }
}

[HarmonyPatch(typeof(StatusTrigger), nameof(StatusTrigger.OnTriggerEnter))]
internal static class StatusTriggerEnterPatch
{
    private static void Prefix(StatusTrigger __instance, Collider other)
    {
        if (__instance.counter > 0f || !Plugin.IsLocal(Plugin.CharacterFrom(other))) return;
        if (__instance.poisonOverTime || (__instance.addStatus && __instance.statusType == CharacterAfflictions.STATUSTYPE.Poison))
            Plugin.Count(StatKeys.PoisonHazardsTriggered);
    }
}

[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
internal static class CharacterAfflictionsAddStatusPatch
{
    private static void Postfix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, float amount, bool __result)
    {
        if (!__result || amount <= 0f || !Plugin.IsLocal(__instance.character)) return;
        switch (statusType)
        {
            case CharacterAfflictions.STATUSTYPE.Poison:
                Plugin.Count(StatKeys.PoisonTaken);
                break;
            case CharacterAfflictions.STATUSTYPE.Thorns:
                Plugin.Count(StatKeys.ThornsTaken);
                break;
            case CharacterAfflictions.STATUSTYPE.Spores:
                Plugin.Count(StatKeys.SporesTaken);
                break;
            case CharacterAfflictions.STATUSTYPE.Injury:
                Plugin.Count(StatKeys.InjuriesTaken);
                break;
        }
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.RPCA_PassOut))]
internal static class CharacterPassOutPatch
{
    private static void Prefix(Character __instance)
    {
        if (Plugin.IsLocal(__instance) && __instance.data != null && !__instance.data.passedOut && !__instance.data.dead)
            Plugin.Count(StatKeys.PassedOut);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.RPCA_Die))]
internal static class CharacterDiePatch
{
    private static void Prefix(Character __instance)
    {
        if (Plugin.IsLocal(__instance) && __instance.data != null && !__instance.data.dead)
            Plugin.Count(StatKeys.Deaths);
    }
}

[HarmonyPatch(typeof(GlobalEvents), nameof(GlobalEvents.TriggerItemConsumed))]
internal static class GlobalEventsItemConsumedPatch
{
    private static void Postfix(Character character)
    {
        if (Plugin.IsLocal(character)) Plugin.Count(StatKeys.ItemsConsumed);
    }
}

[HarmonyPatch(typeof(Luggage), nameof(Luggage.Interact_CastFinished))]
internal static class LuggageCastFinishedPatch
{
    private static void Prefix(Character interactor, ref bool __state)
    {
        __state = Plugin.IsLocal(interactor);
    }

    private static void Postfix(bool __state)
    {
        if (__state) Plugin.Count(StatKeys.LuggageOpened);
    }
}
