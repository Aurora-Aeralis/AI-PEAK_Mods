using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace AeralisFoundation.SacrificialRevival;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "com.aeralisfoundation.peak.sacrificialrevival";
    internal const string PluginName = "Sacrificial Revival";
    internal const string PluginVersion = "1.0.0";

    private const byte PostReviveStatusEvent = 176;
    private const int Schema = 1;
    private const int NoticeWindowId = 932176;
    private const float MaxTotalStatus = 2f;

    internal static ManualLogSource Log { get; private set; } = null!;
    private static Plugin? Instance;

    private static ConfigEntry<bool> EnableDirectRevive = null!;
    private static ConfigEntry<bool> EnableStatueDrop = null!;
    private static ConfigEntry<bool> ShowNotices = null!;
    private static ConfigEntry<KeyboardShortcut> ReviveKey = null!;
    private static ConfigEntry<KeyboardShortcut> StatueKey = null!;
    private static ConfigEntry<int> SelfReviveInjury = null!;
    private static ConfigEntry<int> SelfReviveCurse = null!;
    private static ConfigEntry<int> RevivedHunger = null!;
    private static ConfigEntry<int> RevivedCurse = null!;
    private static ConfigEntry<int> StatueInjury = null!;
    private static ConfigEntry<int> StatueHunger = null!;
    private static ConfigEntry<int> StatueCurse = null!;

    private string _notice = "";
    private float _noticeUntil;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        BindConfig();
        PhotonNetwork.NetworkingClient.EventReceived += OnPhotonEvent;
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (EnableDirectRevive.Value && ReviveKey.Value.IsDown()) TryReviveNearest();
        if (EnableStatueDrop.Value && StatueKey.Value.IsDown()) TryDropScoutStatue();
    }

    private void OnGUI()
    {
        if (!ShowNotices.Value || Time.realtimeSinceStartup >= _noticeUntil) return;
        GUILayout.Window(NoticeWindowId, new Rect(20f, 48f, 430f, 44f), _ => GUILayout.Label(_notice), PluginName);
    }

    private void OnDestroy()
    {
        if (PhotonNetwork.NetworkingClient != null) PhotonNetwork.NetworkingClient.EventReceived -= OnPhotonEvent;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    private void BindConfig()
    {
        EnableDirectRevive = Config.Bind("General", "EnableDirectRevive", true, "Enable the direct teammate revive action.");
        EnableStatueDrop = Config.Bind("General", "EnableScoutStatueDrop", true, "Enable the Scout Statue item drop action.");
        ShowNotices = Config.Bind("Display", "ShowNotices", true, "Show short on-screen result notices.");
        ReviveKey = Config.Bind("Controls", "ReviveKey", new KeyboardShortcut(KeyCode.F6), "Revive the nearest dead or downed teammate.");
        StatueKey = Config.Bind("Controls", "DropScoutStatueKey", new KeyboardShortcut(KeyCode.F7), "Drop a Scout Statue-like item near yourself.");
        SelfReviveInjury = Config.Bind("Direct Revive Cost", "SelfInjuryPercent", 30, new ConfigDescription("Injury applied to the payer before direct revive.", new AcceptableValueRange<int>(0, 200)));
        SelfReviveCurse = Config.Bind("Direct Revive Cost", "SelfCursePercent", 10, new ConfigDescription("Curse applied to the payer before direct revive.", new AcceptableValueRange<int>(0, 200)));
        RevivedHunger = Config.Bind("Direct Revive Result", "RevivedHungerPercent", 20, new ConfigDescription("Hunger applied to the revived teammate after the statue-style revive.", new AcceptableValueRange<int>(0, 200)));
        RevivedCurse = Config.Bind("Direct Revive Result", "RevivedCursePercent", 10, new ConfigDescription("Curse applied to the revived teammate after the statue-style revive.", new AcceptableValueRange<int>(0, 200)));
        StatueInjury = Config.Bind("Scout Statue Cost", "SelfInjuryPercent", 30, new ConfigDescription("Injury applied to the payer before dropping a Scout Statue.", new AcceptableValueRange<int>(0, 200)));
        StatueHunger = Config.Bind("Scout Statue Cost", "SelfHungerPercent", 20, new ConfigDescription("Hunger applied to the payer before dropping a Scout Statue.", new AcceptableValueRange<int>(0, 200)));
        StatueCurse = Config.Bind("Scout Statue Cost", "SelfCursePercent", 10, new ConfigDescription("Curse applied to the payer before dropping a Scout Statue.", new AcceptableValueRange<int>(0, 200)));
    }

    private void TryReviveNearest()
    {
        if (!TryGetLivingLocal(out var local)) return;
        var target = FindNearestRecoverableTeammate(local);
        if (target == null)
        {
            Notice("No dead or downed teammate found.");
            return;
        }

        var costs = new[]
        {
            new StatusCost(CharacterAfflictions.STATUSTYPE.Injury, Percent(SelfReviveInjury.Value)),
            new StatusCost(CharacterAfflictions.STATUSTYPE.Curse, Percent(SelfReviveCurse.Value))
        };
        if (!TryPay(local, costs, out var reason))
        {
            Notice(reason);
            return;
        }

        var position = GetRevivePosition(target, out var segment);
        if (PhotonNetwork.InRoom && target.view != null) target.view.RPC("RPCA_ReviveAtPosition", RpcTarget.All, position, true, segment);
        else target.RPCA_ReviveAtPosition(position, true, segment);

        QueuePostReviveStatuses(target.view == null ? -1 : target.view.ViewID, Percent(RevivedHunger.Value), Percent(RevivedCurse.Value), true);
        Notice($"Revived {TargetLabel(target)}.");
    }

    private void TryDropScoutStatue()
    {
        if (!TryGetLivingLocal(out var local)) return;
        if (!TryFindScoutStatue(out var item, out var itemName))
        {
            Notice("No Scout Statue item was found in ItemDatabase.");
            return;
        }

        var costs = new[]
        {
            new StatusCost(CharacterAfflictions.STATUSTYPE.Injury, Percent(StatueInjury.Value)),
            new StatusCost(CharacterAfflictions.STATUSTYPE.Hunger, Percent(StatueHunger.Value)),
            new StatusCost(CharacterAfflictions.STATUSTYPE.Curse, Percent(StatueCurse.Value))
        };
        if (!TryPay(local, costs, out var reason))
        {
            Notice(reason);
            return;
        }

        var pos = local.Center + Vector3.up * 0.6f + local.transform.forward * 0.8f;
        if (PhotonNetwork.InRoom) PhotonNetwork.Instantiate("0_Items/" + item.gameObject.name, pos, Quaternion.identity, 0, Array.Empty<object>());
        else Instantiate(item.gameObject, pos, Quaternion.identity);
        Notice($"Dropped {itemName}.");
    }

    private void OnPhotonEvent(EventData data)
    {
        if (data.Code != PostReviveStatusEvent || data.CustomData is not object[] payload || payload.Length < 4 || Convert.ToInt32(payload[0]) != Schema) return;
        QueuePostReviveStatuses(Convert.ToInt32(payload[1]), Convert.ToSingle(payload[2]), Convert.ToSingle(payload[3]), false);
    }

    private void QueuePostReviveStatuses(int targetViewId, float hunger, float curse, bool broadcast)
    {
        if (targetViewId < 0) return;
        StartCoroutine(ApplyPostReviveStatuses(targetViewId, hunger, curse));
        if (broadcast && PhotonNetwork.InRoom)
            PhotonNetwork.RaiseEvent(PostReviveStatusEvent, new object[] { Schema, targetViewId, hunger, curse }, new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
    }

    private IEnumerator ApplyPostReviveStatuses(int targetViewId, float hunger, float curse)
    {
        yield return new WaitForSeconds(0.75f);
        if (!Character.GetCharacterWithPhotonID(targetViewId, out var target) || target?.refs?.afflictions == null) yield break;
        if (hunger > 0f) target.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hunger, hunger, true, true, true);
        if (curse > 0f) target.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, curse, true, true, true);
    }

    private static bool TryGetLivingLocal(out Character local)
    {
        local = Character.localCharacter;
        if (local != null && local.data != null && !local.IsGhost && !local.data.dead && !local.data.passedOut && !local.data.fullyPassedOut) return true;
        Instance?.Notice("Only a living scout can use Sacrificial Revival.");
        return false;
    }

    private static Character? FindNearestRecoverableTeammate(Character local)
    {
        if (Character.AllCharacters == null) return null;
        Character? best = null;
        var bestDist = float.MaxValue;
        var origin = local.transform.position;
        foreach (var candidate in Character.AllCharacters)
        {
            if (candidate == null || ReferenceEquals(candidate, local) || candidate.data == null || candidate.isBot || candidate.isScoutmaster || !IsRecoverable(candidate)) continue;
            var dist = (candidate.transform.position - origin).sqrMagnitude;
            if (dist >= bestDist) continue;
            best = candidate;
            bestDist = dist;
        }

        return best;
    }

    private static bool IsRecoverable(Character character)
    {
        return character.data.dead || character.data.passedOut || character.data.fullyPassedOut || character.IsGhost;
    }

    private static Vector3 GetRevivePosition(Character target, out int segment)
    {
        segment = 0;
        try
        {
            segment = (int)MapHandler.CurrentSegmentNumber;
            var statue = MapHandler.CurrentScoutStatue ?? MapHandler.BaseCampScoutStatue;
            if (statue != null) return statue.RandomRevivePoint;
            var spawn = MapHandler.CurrentBaseCampSpawnPoint;
            if (spawn != null) return spawn.position;
        }
        catch
        {
        }

        return target.LastLivingPosition == Vector3.zero ? target.transform.position + Vector3.up : target.LastLivingPosition;
    }

    private static bool TryPay(Character local, IReadOnlyList<StatusCost> costs, out string reason)
    {
        var afflictions = local.refs?.afflictions;
        if (afflictions == null)
        {
            reason = "Local afflictions were not ready.";
            return false;
        }

        if (!HasStatusRoom(afflictions, costs, out reason)) return false;
        var applied = new List<StatusCost>();
        foreach (var cost in costs)
        {
            if (cost.Amount <= 0f) continue;
            if (afflictions.AddStatus(cost.Type, cost.Amount, false, true, true))
            {
                applied.Add(cost);
                continue;
            }

            for (var i = applied.Count - 1; i >= 0; i--) afflictions.SubtractStatus(applied[i].Type, applied[i].Amount, false, false);
            reason = "The sacrifice cost could not be applied.";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool HasStatusRoom(CharacterAfflictions afflictions, IReadOnlyList<StatusCost> costs, out string reason)
    {
        var total = 0f;
        for (var i = 0; i < costs.Count; i++)
        {
            var cost = costs[i];
            if (cost.Amount <= 0f) continue;
            total += cost.Amount;
            var perType = cost.Amount;
            for (var j = i + 1; j < costs.Count; j++)
                if (costs[j].Type == cost.Type)
                    perType += costs[j].Amount;
            if (afflictions.GetCurrentStatus(cost.Type) + perType > afflictions.GetStatusCap(cost.Type) + 0.0001f)
            {
                reason = $"Not enough {cost.Type} capacity for the sacrifice.";
                return false;
            }
        }

        if (afflictions.statusSum + total <= MaxTotalStatus + 0.0001f)
        {
            reason = "";
            return true;
        }

        reason = "Not enough remaining status capacity for the sacrifice.";
        return false;
    }

    private static bool TryFindScoutStatue(out Item item, out string itemName)
    {
        return TryFindItem(n => Has(n, "scout") && (Has(n, "statue") || Has(n, "effigy")), out item, out itemName)
            || TryFindItem(n => Has(n, "revive") && (Has(n, "statue") || Has(n, "effigy")), out item, out itemName)
            || TryFindItem(n => Has(n, "respawn") && (Has(n, "statue") || Has(n, "effigy")), out item, out itemName)
            || TryFindItem(n => Has(n, "statue") || Has(n, "effigy"), out item, out itemName);
    }

    private static bool TryFindItem(Predicate<string> match, out Item item, out string itemName)
    {
        var database = ItemDatabase.Instance;
        if (database?.itemLookup != null)
        {
            foreach (var pair in database.itemLookup)
            {
                if (pair.Value == null) continue;
                var name = pair.Value.gameObject.name;
                if (!match(Normalize(name))) continue;
                item = pair.Value;
                itemName = name;
                return true;
            }
        }

        item = null!;
        itemName = "";
        return false;
    }

    private void Notice(string text)
    {
        _notice = text;
        _noticeUntil = Time.realtimeSinceStartup + 3.5f;
        Log.LogInfo(text);
    }

    private static float Percent(int value) => Mathf.Max(0, value) / 100f;
    private static string TargetLabel(Character character) => string.IsNullOrWhiteSpace(character.characterName) ? "teammate" : character.characterName;
    private static string Normalize(string value) => value.Replace("_", " ").Replace("-", " ").ToLowerInvariant();
    private static bool Has(string name, string token) => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private readonly struct StatusCost
    {
        public StatusCost(CharacterAfflictions.STATUSTYPE type, float amount)
        {
            Type = type;
            Amount = amount;
        }

        public CharacterAfflictions.STATUSTYPE Type { get; }
        public float Amount { get; }
    }
}
