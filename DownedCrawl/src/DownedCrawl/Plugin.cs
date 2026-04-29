using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AeralisFoundation.DownedCrawl;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "aeralisfoundation.peak.downedcrawl";
    public const string PluginName = "Downed Crawl";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private static ConfigEntry<bool> Enabled = null!;
    private static ConfigEntry<float> CrawlSpeed = null!;
    private static ConfigEntry<float> SprintCrawlSpeed = null!;
    private static ConfigEntry<float> SprintUnlockDeathBar = null!;
    private static ConfigEntry<float> CrawlDrag = null!;

    private Harmony? Harmony;

    private void Awake()
    {
        Log = Logger;
        BindConfig();
        Harmony = new Harmony(PluginGuid);
        Harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }

    private void BindConfig()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Enables downed crawling.");
        CrawlSpeed = Config.Bind("Movement", "CrawlSpeed", 0.22f, new ConfigDescription("Downed crawl speed as a fraction of normal walking speed.", new AcceptableValueRange<float>(0f, 1f)));
        SprintCrawlSpeed = Config.Bind("Movement", "SprintCrawlSpeed", 0.55f, new ConfigDescription("Downed crawl sprint speed as a fraction of normal walking speed.", new AcceptableValueRange<float>(0f, 1f)));
        SprintUnlockDeathBar = Config.Bind("Movement", "SprintUnlockDeathBar", 0.5f, new ConfigDescription("Death bar fraction where holding sprint enables the faster crawl.", new AcceptableValueRange<float>(0f, 1f)));
        CrawlDrag = Config.Bind("Movement", "CrawlDrag", 0.92f, new ConfigDescription("Extra drag applied while crawling to reduce sliding.", new AcceptableValueRange<float>(0.5f, 1f)));
    }

    private static bool IsDowned(Character character)
    {
        if (!Enabled.Value || character == null || !character.IsLocal) return false;
        var data = character.data;
        return data != null && !data.dead && (data.passedOut || data.fullyPassedOut) && !data.carrier;
    }

    private static bool WantsSprint(CharacterMovement movement)
    {
        var character = movement.character;
        return IsDowned(character)
            && character.data.deathTimer >= SprintUnlockDeathBar.Value
            && character.input.sprintIsPressed
            && character.input.movementInput.sqrMagnitude > 0.01f;
    }

    private static float ActiveSpeed(CharacterMovement movement)
    {
        return Mathf.Clamp(WantsSprint(movement) ? SprintCrawlSpeed.Value : CrawlSpeed.Value, 0f, 1f);
    }

    [HarmonyPatch(typeof(CharacterMovement), "FixedUpdate")]
    private static class CharacterMovementFixedUpdatePatch
    {
        private static void Postfix(CharacterMovement __instance)
        {
            if (!IsDowned(__instance.character)) return;
            var speed = ActiveSpeed(__instance);
            if (speed <= 0f || __instance.character.data.worldMovementInput_Lerp.sqrMagnitude <= 0.0001f) return;

            var force = __instance.movementForce * Mathf.Max(0f, __instance.movementModifier) * speed;
            if (force <= 0f) return;

            var parts = __instance.character.refs.ragdoll.partList;
            for (var i = 0; i < parts.Count; i++)
            {
                parts[i].AddMovementForce(force);
                parts[i].Drag(CrawlDrag.Value, true);
                parts[i].ApplyForces();
            }
        }
    }
}
