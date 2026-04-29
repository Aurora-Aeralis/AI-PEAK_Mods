using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AeralisFoundation.SplitVitals;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "aeralisfoundation.peak.splitvitals";
    internal const string PluginName = "SplitVitals";
    internal const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private static ConfigEntry<bool> Enabled = null!;
    private static ConfigEntry<bool> ShowHealthBar = null!;
    private static ConfigEntry<bool> HideVanillaStatusSegments = null!;
    private static ConfigEntry<float> BarX = null!;
    private static ConfigEntry<float> BarBottom = null!;
    private static ConfigEntry<float> BarWidth = null!;
    private static ConfigEntry<float> BarHeight = null!;

    private static readonly CharacterAfflictions.STATUSTYPE[] StatusOrder =
    {
        CharacterAfflictions.STATUSTYPE.Injury,
        CharacterAfflictions.STATUSTYPE.Hunger,
        CharacterAfflictions.STATUSTYPE.Cold,
        CharacterAfflictions.STATUSTYPE.Poison,
        CharacterAfflictions.STATUSTYPE.Crab,
        CharacterAfflictions.STATUSTYPE.Curse,
        CharacterAfflictions.STATUSTYPE.Drowsy,
        CharacterAfflictions.STATUSTYPE.Weight,
        CharacterAfflictions.STATUSTYPE.Hot,
        CharacterAfflictions.STATUSTYPE.Thorns,
        CharacterAfflictions.STATUSTYPE.Spores,
        CharacterAfflictions.STATUSTYPE.Web
    };

    private static readonly Color Border = new(0.02f, 0.02f, 0.025f, 0.92f);
    private static readonly Color Backing = new(0.02f, 0.025f, 0.03f, 0.72f);
    private static Texture2D? Pixel;
    private static GUIStyle? LabelStyle;

    private Harmony? Harmony;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Separates status pressure from stamina.");
        ShowHealthBar = Config.Bind("Display", "ShowHealthBar", true, "Shows the separate health/status bar.");
        HideVanillaStatusSegments = Config.Bind("Display", "HideVanillaStatusSegments", true, "Hides status chunks from the vanilla stamina bar.");
        BarX = Config.Bind("Display", "HealthBarX", 24f, new ConfigDescription("Health bar left position in screen pixels.", new AcceptableValueRange<float>(0f, 3840f)));
        BarBottom = Config.Bind("Display", "HealthBarBottomOffset", 116f, new ConfigDescription("Health bar distance from the bottom of the screen.", new AcceptableValueRange<float>(0f, 2160f)));
        BarWidth = Config.Bind("Display", "HealthBarWidth", 260f, new ConfigDescription("Health bar width in screen pixels.", new AcceptableValueRange<float>(80f, 800f)));
        BarHeight = Config.Bind("Display", "HealthBarHeight", 18f, new ConfigDescription("Health bar height in screen pixels.", new AcceptableValueRange<float>(8f, 64f)));

        Harmony = new Harmony(PluginGuid);
        Harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnGUI()
    {
        if (!Active || !ShowHealthBar.Value || Event.current.type != EventType.Repaint) return;
        var character = Character.observedCharacter ?? Character.localCharacter;
        if (character?.refs?.afflictions == null || character.data == null) return;

        var statuses = character.refs.afflictions;
        var health = character.data.dead || character.data.fullyPassedOut ? 0f : Mathf.Clamp01(1f - statuses.statusSum);
        var width = Mathf.Clamp(BarWidth.Value, 80f, Screen.width);
        var height = Mathf.Clamp(BarHeight.Value, 8f, Screen.height);
        var rect = new Rect(Mathf.Clamp(BarX.Value, 0f, Screen.width - width), Mathf.Clamp(Screen.height - BarBottom.Value - height, 0f, Screen.height - height), width, height);

        DrawRect(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), Border);
        DrawRect(rect, Backing);
        DrawRect(new Rect(rect.x, rect.y, rect.width * health, rect.height), HealthColor(health));
        DrawStatusSegments(rect, statuses);
        GUI.Label(new Rect(rect.x, rect.y - 18f, rect.width, 18f), $"Health {Mathf.RoundToInt(health * 100f)}%", GetLabelStyle());
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
        if (Pixel != null)
        {
            Destroy(Pixel);
            Pixel = null;
        }
    }

    internal static bool Active => Enabled?.Value == true;
    internal static bool HideStatusSegments => Active && HideVanillaStatusSegments.Value;

    private static void DrawStatusSegments(Rect rect, CharacterAfflictions afflictions)
    {
        var x = rect.xMax;
        foreach (var status in StatusOrder)
        {
            var amount = Mathf.Clamp01(afflictions.GetCurrentStatus(status));
            if (amount <= 0.001f) continue;
            var width = Mathf.Min(x - rect.x, rect.width * amount);
            if (width <= 0f) return;
            x -= width;
            DrawRect(new Rect(x, rect.y, width, rect.height), StatusColor(status));
        }
    }

    private static void DrawRect(Rect rect, Color color)
    {
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, GetPixel());
        GUI.color = old;
    }

    private static Texture2D GetPixel()
    {
        if (Pixel != null) return Pixel;
        Pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        Pixel.SetPixel(0, 0, Color.white);
        Pixel.Apply(false);
        return Pixel;
    }

    private static GUIStyle GetLabelStyle()
    {
        if (LabelStyle != null) return LabelStyle;
        LabelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            normal = { textColor = new Color(0.92f, 0.98f, 0.95f, 0.96f) }
        };
        return LabelStyle;
    }

    private static Color HealthColor(float health)
    {
        if (health > 0.55f) return new Color(0.23f, 0.78f, 0.43f, 0.9f);
        if (health > 0.25f) return new Color(0.95f, 0.78f, 0.25f, 0.92f);
        return new Color(0.92f, 0.2f, 0.16f, 0.94f);
    }

    private static Color StatusColor(CharacterAfflictions.STATUSTYPE status)
    {
        return status switch
        {
            CharacterAfflictions.STATUSTYPE.Injury => new Color(0.88f, 0.08f, 0.06f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Hunger => new Color(0.9f, 0.46f, 0.1f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Cold => new Color(0.38f, 0.78f, 1f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Poison => new Color(0.28f, 0.86f, 0.18f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Crab => new Color(1f, 0.42f, 0.24f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Curse => new Color(0.62f, 0.2f, 0.95f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Drowsy => new Color(0.35f, 0.36f, 0.7f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Weight => new Color(0.55f, 0.52f, 0.48f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Hot => new Color(1f, 0.28f, 0.08f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Thorns => new Color(0.2f, 0.62f, 0.14f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Spores => new Color(0.74f, 0.86f, 0.2f, 0.95f),
            CharacterAfflictions.STATUSTYPE.Web => new Color(0.82f, 0.82f, 0.86f, 0.95f),
            _ => new Color(0.7f, 0.7f, 0.7f, 0.95f)
        };
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.GetMaxStamina))]
internal static class CharacterGetMaxStaminaPatch
{
    private static bool Prefix(ref float __result)
    {
        if (!Plugin.Active) return true;
        __result = 1f;
        return false;
    }
}

[HarmonyPatch(typeof(StaminaBar), "Update")]
internal static class StaminaBarUpdatePatch
{
    private static void Postfix(StaminaBar __instance)
    {
        if (!Plugin.HideStatusSegments || __instance.fullBar == null || __instance.staminaBarOutline == null) return;
        var size = __instance.staminaBarOutline.sizeDelta;
        size.x = __instance.fullBar.sizeDelta.x + 14f;
        __instance.staminaBarOutline.sizeDelta = size;
        if (__instance.staminaBarOutlineOverflowBar != null) __instance.staminaBarOutlineOverflowBar.gameObject.SetActive(false);
    }
}

[HarmonyPatch(typeof(BarAffliction), nameof(BarAffliction.ChangeAffliction))]
internal static class BarAfflictionChangePatch
{
    private static bool Prefix(BarAffliction __instance)
    {
        if (!Plugin.HideStatusSegments) return true;
        __instance.size = 0f;
        __instance.width = 0f;
        __instance.gameObject.SetActive(false);
        return false;
    }
}

[HarmonyPatch(typeof(BarAffliction), nameof(BarAffliction.UpdateAffliction))]
internal static class BarAfflictionUpdatePatch
{
    private static bool Prefix(BarAffliction __instance)
    {
        if (!Plugin.HideStatusSegments) return true;
        __instance.width = 0f;
        return false;
    }
}
