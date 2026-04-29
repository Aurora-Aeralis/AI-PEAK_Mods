using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SpectralComms;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "aurora.aeralis.peak.spectralcomms";
    public const string PluginName = "SpectralComms";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    Harmony? Harmony;

    void Awake()
    {
        Log = Logger;
        Harmony = new Harmony(PluginGuid);
        VoiceRouting.Patch(Harmony);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    void OnDestroy()
    {
        VoiceRouting.RestoreMutedSources();
        Harmony?.UnpatchSelf();
    }
}

static class VoiceRouting
{
    static readonly System.Type? CharacterType = AccessTools.TypeByName("Character");
    static readonly System.Type? TriggerType = AccessTools.TypeByName("ProximityVoiceTrigger");
    static readonly System.Type? VoiceHandlerType = AccessTools.TypeByName("CharacterVoiceHandler");
    static readonly FieldInfo? LocalCharacter = AccessTools.Field(CharacterType, "localCharacter");
    static readonly MethodInfo? IsGhostGetter = AccessTools.PropertyGetter(CharacterType, "IsGhost");
    static readonly MethodInfo? TargetInterestGroupGetter = AccessTools.PropertyGetter(TriggerType, "TargetInterestGroup");
    static readonly FieldInfo? GroupsToAdd = AccessTools.Field(TriggerType, "groupsToAdd");
    static readonly FieldInfo? GroupsToRemove = AccessTools.Field(TriggerType, "groupsToRemove");
    static readonly FieldInfo? HandlerCharacter = AccessTools.Field(VoiceHandlerType, "m_character");
    static readonly FieldInfo? HandlerSource = AccessTools.Field(VoiceHandlerType, "m_source");
    static readonly Dictionary<AudioSource, bool> MutedSources = new();

    internal static void Patch(Harmony Harmony)
    {
        PatchPrefix(Harmony, AccessTools.Method(TriggerType, "OnTriggerEnter", new[] { typeof(Collider) }), "ProximityVoiceTrigger.OnTriggerEnter");
        PatchPrefix(Harmony, AccessTools.Method(TriggerType, "OnTriggerStay", new[] { typeof(Collider) }), "ProximityVoiceTrigger.OnTriggerStay");

        var Update = AccessTools.Method(VoiceHandlerType, "Update");
        if (Update == null) Plugin.Log.LogWarning("CharacterVoiceHandler.Update was not found; active ghost streams cannot be locally muted.");
        else
        {
            Harmony.Patch(Update, postfix: new HarmonyMethod(typeof(VoiceRouting), nameof(MuteGhostVoicePostfix)));
            Plugin.Log.LogInfo("Patched CharacterVoiceHandler.Update.");
        }
    }

    static void PatchPrefix(Harmony Harmony, MethodInfo? Method, string Name)
    {
        if (Method == null)
        {
            Plugin.Log.LogWarning($"{Name} was not found; ghost voice subscription suppression may be incomplete.");
            return;
        }

        Harmony.Patch(Method, prefix: new HarmonyMethod(typeof(VoiceRouting), nameof(BlockGhostSubscriptionPrefix)));
        Plugin.Log.LogInfo($"Patched {Name}.");
    }

    static bool BlockGhostSubscriptionPrefix(object __instance, Collider other)
    {
        if (!ShouldBlockGhostVoice(other, out var Group)) return true;
        QueueRemoval(__instance, Group);
        return false;
    }

    static void MuteGhostVoicePostfix(object __instance)
    {
        if (HandlerCharacter?.GetValue(__instance) is not { } Character || HandlerSource?.GetValue(__instance) is not AudioSource Source) return;
        if (LocalIsLiving() && IsGhost(Character))
        {
            if (!MutedSources.ContainsKey(Source)) MutedSources[Source] = Source.mute;
            Source.mute = true;
            if (MutedSources.Count > 64) TrimMutedSources();
            return;
        }

        RestoreSource(Source);
    }

    static bool ShouldBlockGhostVoice(Collider Other, out byte Group)
    {
        Group = 0;
        if (!LocalIsLiving() || TriggerType == null || CharacterType == null) return false;
        var Trigger = Other.GetComponent(TriggerType);
        var Character = Trigger == null ? null : Trigger.GetComponentInParent(CharacterType);
        if (Character == null || !IsGhost(Character)) return false;
        Group = TargetInterestGroupGetter?.Invoke(Trigger, null) is byte Value ? Value : (byte)0;
        return Group != 0;
    }

    static void QueueRemoval(object Trigger, byte Group)
    {
        if (GroupsToAdd?.GetValue(Trigger) is List<byte> Add) Add.Remove(Group);
        if (GroupsToRemove?.GetValue(Trigger) is List<byte> Remove && !Remove.Contains(Group)) Remove.Add(Group);
    }

    static bool LocalIsLiving()
    {
        var Character = LocalCharacter?.GetValue(null);
        return Character != null && !IsGhost(Character);
    }

    static bool IsGhost(object Character) => IsGhostGetter?.Invoke(Character, null) is true;

    internal static void RestoreMutedSources()
    {
        foreach (var Source in MutedSources.Keys.ToArray()) RestoreSource(Source);
    }

    static void RestoreSource(AudioSource Source)
    {
        if (!MutedSources.TryGetValue(Source, out var WasMuted)) return;
        if (Source != null) Source.mute = WasMuted;
        MutedSources.Remove(Source!);
    }

    static void TrimMutedSources()
    {
        foreach (var Source in MutedSources.Keys.Where(Source => Source == null).ToArray()) MutedSources.Remove(Source!);
    }
}
