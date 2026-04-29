using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace AeralisFoundation.RopeCannonReloaded;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "aeralisfoundation.peak.ropecannonreloaded";
    public const string PluginName = "Rope Cannon Reloaded";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private static ConfigEntry<bool> Enabled = null!;
    private static ConfigEntry<bool> PlayEmptySoundWhenNoRope = null!;

    private Harmony Harmony = null!;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Allows empty rope cannons to reload from carried rope items.");
        PlayEmptySoundWhenNoRope = Config.Bind("General", "PlayEmptySoundWhenNoRope", true, "Keeps the vanilla empty-shot feedback when no carried rope can be consumed.");
        Harmony = new Harmony(PluginGuid);
        Harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        Harmony.UnpatchSelf();
    }

    private static bool TryConsumeCarriedRope(out byte slotId)
    {
        slotId = 0;
        var player = Player.localPlayer;
        if (player?.itemSlots == null) return false;
        foreach (var slot in player.itemSlots)
        {
            if (slot?.prefab == null || slot.prefab.GetComponent<RopeSpool>() == null) continue;
            slotId = slot.itemSlotID;
            player.EmptySlot(Optionable<byte>.Some(slotId));
            return true;
        }
        return false;
    }

    private static void Reload(RopeShooter shooter)
    {
        shooter.Ammo = 1;
        shooter.ForceSync();
        if (shooter.hideOnFire != null) shooter.hideOnFire.SetActive(true);
        if (shooter.photonView != null) shooter.photonView.RPC("Sync_Rpc", RpcTarget.Others, true);
    }

    [HarmonyPatch(typeof(RopeShooter), nameof(RopeShooter.OnPrimaryFinishedCast))]
    private static class RopeShooterPatch
    {
        private static bool Prefix(RopeShooter __instance)
        {
            if (!Enabled.Value || __instance.HasAmmo || __instance.startAmmo < 1) return true;
            if (!TryConsumeCarriedRope(out var slotId)) return PlayEmptySoundWhenNoRope.Value;
            Reload(__instance);
            Log.LogInfo($"Reloaded rope cannon from rope slot {slotId}.");
            return false;
        }
    }
}
