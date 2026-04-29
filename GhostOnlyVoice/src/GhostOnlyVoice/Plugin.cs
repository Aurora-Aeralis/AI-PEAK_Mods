using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GhostOnlyVoice;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "aurora.aeralis.peak.ghostonlyvoice";
    public const string PluginName = "Ghost Only Voice";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private Harmony? harmony;

    private void Awake()
    {
        Log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();
}

internal static class VoiceRules
{
    internal static readonly Type? CharacterType = AccessTools.TypeByName("Character");
    internal static readonly Type? TriggerType = AccessTools.TypeByName("ProximityVoiceTrigger");
    internal static readonly Type? VoiceHandlerType = AccessTools.TypeByName("CharacterVoiceHandler");
    private static readonly FieldInfo? LocalCharacter = AccessTools.Field(CharacterType, "localCharacter");
    private static readonly MethodInfo? IsGhostGetter = AccessTools.PropertyGetter(CharacterType, "IsGhost");
    private static readonly MethodInfo? TargetInterestGroupGetter = AccessTools.PropertyGetter(TriggerType, "TargetInterestGroup");
    private static readonly FieldInfo? GroupsToAdd = AccessTools.Field(TriggerType, "groupsToAdd");
    private static readonly FieldInfo? GroupsToRemove = AccessTools.Field(TriggerType, "groupsToRemove");
    private static readonly FieldInfo? HandlerCharacter = AccessTools.Field(VoiceHandlerType, "m_character");
    private static readonly FieldInfo? HandlerSource = AccessTools.Field(VoiceHandlerType, "m_source");

    internal static bool LocalIsGhost()
    {
        var local = LocalCharacter?.GetValue(null);
        return local != null && IsGhost(local);
    }

    internal static bool IsGhost(object character) => IsGhostGetter?.Invoke(character, null) is true;

    internal static bool TryGetTrigger(Collider collider, out Component trigger)
    {
        trigger = null!;
        if (TriggerType == null) return false;
        trigger = collider.GetComponent(TriggerType);
        return trigger != null;
    }

    internal static bool TryGetCharacter(Component component, out Component character)
    {
        character = null!;
        if (CharacterType == null) return false;
        character = component.GetComponentInParent(CharacterType);
        return character != null;
    }

    internal static byte TargetGroup(object trigger) => TargetInterestGroupGetter?.Invoke(trigger, null) is byte group ? group : (byte)0;

    internal static void QueueRemoval(object trigger, byte group)
    {
        if (group == 0) return;
        if (GroupsToAdd?.GetValue(trigger) is List<byte> add) add.Remove(group);
        if (GroupsToRemove?.GetValue(trigger) is List<byte> remove && !remove.Contains(group)) remove.Add(group);
    }

    internal static bool ShouldBlockGhostVoice(Collider other, out byte group)
    {
        group = 0;
        if (LocalIsGhost() || !TryGetTrigger(other, out var trigger) || !TryGetCharacter(trigger, out var character) || !IsGhost(character)) return false;
        group = TargetGroup(trigger);
        return group != 0;
    }

    internal static void MuteGhostForLiving(object handler)
    {
        var character = HandlerCharacter?.GetValue(handler);
        if (character == null || !IsGhost(character) || LocalIsGhost()) return;
        if (HandlerSource?.GetValue(handler) is AudioSource source) source.volume = 0f;
    }
}

[HarmonyPatch]
internal static class ProximityVoiceTriggerOnTriggerEnterPatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(VoiceRules.TriggerType, "OnTriggerEnter");

    private static bool Prefix(object __instance, Collider other)
    {
        if (!VoiceRules.ShouldBlockGhostVoice(other, out var group)) return true;
        VoiceRules.QueueRemoval(__instance, group);
        return false;
    }
}

[HarmonyPatch]
internal static class CharacterVoiceHandlerUpdatePatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(VoiceRules.VoiceHandlerType, "Update");

    private static void Postfix(object __instance) => VoiceRules.MuteGhostForLiving(__instance);
}
