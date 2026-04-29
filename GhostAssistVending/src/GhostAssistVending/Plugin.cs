using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AeralisFoundation.GhostAssistVending;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    private const byte RequestEvent = 188;
    private const byte ResultEvent = 189;
    private const byte TickEvent = 190;
    private const int Schema = 1;
    private const int WindowId = 737001;
    private const float RemoteTimeout = 8f;

    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<KeyboardShortcut> _toggleKey = null!;
    private ConfigEntry<float> _passiveIncome = null!;
    private ConfigEntry<int> _miniGameReward = null!;
    private readonly StageLimitState _localLimits = new();
    private readonly Dictionary<int, StageLimitState> _remoteLimits = new();
    private readonly Dictionary<int, PendingPurchase> _pending = new();
    private readonly List<int> _timedOut = new();
    private Purchase[] _purchases = Array.Empty<Purchase>();
    private Rect _window = new(24, 80, 380, 540);
    private float _soulPoints;
    private bool _showWindow;
    private int _selectedTarget;
    private int _requestId;
    private string _notice = "";
    private float _noticeUntil;
    private bool _miniActive;
    private float _miniRunnerY;
    private float _miniVelocity;
    private float _miniObstacleX = 1f;
    private int _miniScore;

    private void Awake()
    {
        Log = Logger;
        _toggleKey = Config.Bind("Input", "ToggleVendingKey", new KeyboardShortcut(KeyCode.F7));
        _passiveIncome = Config.Bind("Economy", "PassiveSoulPointsPerSecond", 1f);
        _miniGameReward = Config.Bind("Economy", "MiniGameSoulPointsPerObstacle", 4);
        _purchases =
        [
            new(Offer.AppleBox, "Apple Blind Box", "75% Green Apple", LimitKind.Box, Config.Bind("Costs", "AppleBlindBox", 60)),
            new(Offer.MushroomBox, "Mushroom Blind Box", "Random forest mushroom", LimitKind.Box, Config.Bind("Costs", "MushroomBlindBox", 70)),
            new(Offer.Tick, "Tick", "Minor stamina tug with weight", LimitKind.Tick, Config.Bind("Costs", "Tick", 90)),
            new(Offer.SpecialtyFood, "Specialty Food", "Cactus, Chili, Coconut, Orange, rare Scout Cookie", LimitKind.Food, Config.Bind("Costs", "SpecialtyFood", 120)),
            new(Offer.UtilityProp, "Utility Prop", "Rope, Pitons, Spring Mushroom, Balloons", LimitKind.Prop, Config.Bind("Costs", "UtilityProp", 130)),
            new(Offer.Respawn, "Respawn", "Return at the current bonfire", LimitKind.None, Config.Bind("Costs", "Respawn", 600))
        ];

        PhotonNetwork.NetworkingClient.EventReceived += OnPhotonEvent;
        Log.LogInfo($"Plugin {Name} {Version} is loaded.");
    }

    private void OnDestroy()
    {
        if (PhotonNetwork.NetworkingClient != null) PhotonNetwork.NetworkingClient.EventReceived -= OnPhotonEvent;
    }

    private void Update()
    {
        if (_toggleKey.Value.IsDown()) _showWindow = !_showWindow;

        var ghost = IsLocalGhost();
        if (!ghost)
        {
            _miniActive = false;
            return;
        }

        _soulPoints = Mathf.Min(9999f, _soulPoints + _passiveIncome.Value * Time.unscaledDeltaTime);
        var stage = GetCurrentStage();
        var previousStage = _localLimits.Stage;
        _localLimits.EnsureStage(stage);
        if (previousStage != int.MinValue && previousStage != stage) Notice("Bonfire reached. Vending limits reset.");

        if (_miniActive) UpdateMiniGame();
        RefundTimedOutPurchases();
    }

    private void OnGUI()
    {
        if (!IsLocalGhost()) return;

        GUILayout.BeginArea(new Rect(24, 24, 360, 48), GUI.skin.box);
        GUILayout.Label($"Soul Points: {Mathf.FloorToInt(_soulPoints)}");
        GUILayout.Label(_showWindow ? "F7 closes vending" : "F7 opens vending");
        GUILayout.EndArea();

        if (_showWindow) _window = GUILayout.Window(WindowId, _window, DrawWindow, "Ghost Assist Vending");
    }

    private void DrawWindow(int id)
    {
        var targets = GetTargets();
        if (_selectedTarget >= targets.Count) _selectedTarget = 0;

        GUILayout.Label($"Stage uses: Boxes {_localLimits.Boxes}/2  Ticks {_localLimits.Ticks}/2  Food {_localLimits.Food}/1  Props {_localLimits.Props}/1");
        if (Time.unscaledTime < _noticeUntil) GUILayout.Label(_notice);

        GUILayout.Space(6);
        GUILayout.Label("Target");
        if (targets.Count == 0) GUILayout.Label("No player target found.");
        else
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(36))) _selectedTarget = (_selectedTarget + targets.Count - 1) % targets.Count;
            GUILayout.Label(TargetLabel(targets[_selectedTarget]), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(36))) _selectedTarget = (_selectedTarget + 1) % targets.Count;
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        foreach (var purchase in _purchases) DrawPurchaseButton(purchase, targets);

        GUILayout.Space(10);
        DrawMiniGame();
        GUI.DragWindow();
    }

    private void DrawPurchaseButton(Purchase purchase, List<Character> targets)
    {
        var allowed = targets.Count > 0 && CanSpend(purchase, _localLimits) && _soulPoints >= purchase.Cost.Value;
        var old = GUI.enabled;
        GUI.enabled = allowed;
        if (GUILayout.Button($"{purchase.Name} - {purchase.Cost.Value} SP"))
            TryBuy(purchase, targets[_selectedTarget]);
        GUI.enabled = old;
        GUILayout.Label($"{purchase.Detail}  {LimitText(purchase)}");
    }

    private void DrawMiniGame()
    {
        GUILayout.Label($"Rift Hop: {_miniScore} cleared");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_miniActive ? "Stop" : "Start")) _miniActive = !_miniActive;
        if (GUILayout.Button("Jump")) JumpMiniGame();
        GUILayout.EndHorizontal();

        var rect = GUILayoutUtility.GetRect(320, 82);
        GUI.Box(rect, "");
        var ground = rect.yMax - 18;
        GUI.Box(new Rect(rect.x + 45, ground - 20 - _miniRunnerY * 50f, 18, 20), "");
        GUI.Box(new Rect(rect.x + 30 + _miniObstacleX * (rect.width - 60), ground - 24, 14, 24), "");
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, 22), "Space / button to hop");
    }

    private void TryBuy(Purchase purchase, Character target)
    {
        if (!CanSpend(purchase, _localLimits))
        {
            Notice("Stage limit reached.");
            return;
        }

        if (_soulPoints < purchase.Cost.Value)
        {
            Notice("Not enough Soul Points.");
            return;
        }

        _soulPoints -= purchase.Cost.Value;
        _localLimits.Add(purchase.Limit, 1);

        var stage = GetCurrentStage();
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            var requestId = ++_requestId;
            _pending[requestId] = new PendingPurchase(purchase, stage, Time.unscaledTime);
            PhotonNetwork.RaiseEvent(RequestEvent, new object[] { Schema, requestId, (int)purchase.Offer, target.view.ViewID, stage }, new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
            Notice("Purchase sent to host.");
            return;
        }

        if (ApplyPurchase(purchase.Offer, target.view.ViewID, PhotonNetwork.LocalPlayer?.ActorNumber ?? -1, out var message))
        {
            Notice(message);
            return;
        }

        _soulPoints += purchase.Cost.Value;
        _localLimits.Add(purchase.Limit, -1);
        Notice(message);
    }

    private void OnPhotonEvent(EventData data)
    {
        try
        {
            if (data.Code == RequestEvent && PhotonNetwork.IsMasterClient) HandlePurchaseRequest(data);
            else if (data.Code == ResultEvent) HandlePurchaseResult(data);
            else if (data.Code == TickEvent && data.CustomData is object[] tick && tick.Length >= 2 && (int)tick[0] == Schema) ApplyTick((int)tick[1]);
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Ghost vending event failed: {ex}");
        }
    }

    private void HandlePurchaseRequest(EventData data)
    {
        if (data.CustomData is not object[] request || request.Length < 5 || (int)request[0] != Schema) return;

        var requestId = (int)request[1];
        var offer = (Offer)(int)request[2];
        var targetViewId = (int)request[3];
        var sender = data.Sender;
        var purchase = _purchases.FirstOrDefault(p => p.Offer == offer);
        var ok = false;
        var message = "Unknown purchase.";

        if (purchase != null)
        {
            var limits = GetRemoteLimits(sender);
            limits.EnsureStage(GetCurrentStage());
            if (CanSpend(purchase, limits))
            {
                ok = ApplyPurchase(offer, targetViewId, sender, out message);
                if (ok) limits.Add(purchase.Limit, 1);
            }
            else message = "Stage limit reached.";
        }

        PhotonNetwork.RaiseEvent(ResultEvent, new object[] { Schema, requestId, ok ? 1 : 0, message }, new RaiseEventOptions { TargetActors = new[] { sender } }, SendOptions.SendReliable);
    }

    private void HandlePurchaseResult(EventData data)
    {
        if (data.CustomData is not object[] result || result.Length < 4 || (int)result[0] != Schema) return;

        var requestId = (int)result[1];
        if (!_pending.TryGetValue(requestId, out var pending)) return;

        _pending.Remove(requestId);
        var ok = (int)result[2] == 1;
        var message = result[3] as string ?? (ok ? "Purchase complete." : "Purchase failed.");
        if (!ok)
        {
            _soulPoints += pending.Purchase.Cost.Value;
            _localLimits.Add(pending.Purchase.Limit, -1);
        }

        Notice(message);
    }

    private bool ApplyPurchase(Offer offer, int targetViewId, int buyerActor, out string message)
    {
        if (!Character.GetCharacterWithPhotonID(targetViewId, out var target) || target == null)
        {
            message = "Target was not found.";
            return false;
        }

        switch (offer)
        {
            case Offer.AppleBox:
                return GrantItem(target, ResolveAppleBox, out message);
            case Offer.MushroomBox:
                return GrantItem(target, ResolveMushroomBox, out message);
            case Offer.Tick:
                return GrantTick(target, out message);
            case Offer.SpecialtyFood:
                return GrantItem(target, ResolveSpecialtyFood, out message);
            case Offer.UtilityProp:
                return GrantItem(target, ResolveUtilityProp, out message);
            case Offer.Respawn:
                return Respawn(target, out message);
            default:
                message = "Unknown purchase.";
                return false;
        }
    }

    private bool GrantItem(Character target, Func<ResolvedItem?> resolve, out string message)
    {
        if (target.data.dead || target.data.fullyPassedOut)
        {
            message = "Items can only be sent to a living scout.";
            return false;
        }

        var item = resolve();
        if (!item.HasValue)
        {
            message = "No matching PEAK item was found.";
            return false;
        }

        var resolved = item.Value;
        if (PhotonNetwork.IsMasterClient && target.player != null)
        {
            var slot = default(ItemSlot);
            if (target.player.AddItem(resolved.Id, new ItemInstanceData(Guid.NewGuid()), out slot))
            {
                message = $"Granted {resolved.Name} to {TargetLabel(target)}.";
                return true;
            }
        }

        return SpawnItemNear(target, resolved.Item, resolved.Name, out message);
    }

    private bool SpawnItemNear(Character target, Item item, string itemName, out string message)
    {
        var pos = target.Center + Vector3.up * 0.6f + target.transform.forward * 0.7f;
        if (PhotonNetwork.InRoom) PhotonNetwork.Instantiate("0_Items/" + item.gameObject.name, pos, Quaternion.identity, 0, Array.Empty<object>());
        else Instantiate(item.gameObject, pos, Quaternion.identity);

        message = $"Dropped {itemName} near {TargetLabel(target)}.";
        return true;
    }

    private bool GrantTick(Character target, out string message)
    {
        if (target.data.dead || target.data.fullyPassedOut)
        {
            message = "Tick needs a living scout.";
            return false;
        }

        ApplyTick(target.view.ViewID);
        if (PhotonNetwork.InRoom) PhotonNetwork.RaiseEvent(TickEvent, new object[] { Schema, target.view.ViewID }, new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);

        message = $"Attached a tick to {TargetLabel(target)}.";
        return true;
    }

    private static void ApplyTick(int targetViewId)
    {
        if (!Character.GetCharacterWithPhotonID(targetViewId, out var target) || target == null) return;
        var tick = target.GetComponent<GhostTickEffect>() ?? target.gameObject.AddComponent<GhostTickEffect>();
        tick.Init(target);
    }

    private bool Respawn(Character target, out string message)
    {
        if (!target.data.dead && !target.data.fullyPassedOut)
        {
            message = "Target is already alive.";
            return false;
        }

        var position = GetRespawnPosition(target, out var segment);
        if (PhotonNetwork.InRoom && target.view != null) target.view.RPC("RPCA_ReviveAtPosition", RpcTarget.All, position, true, segment);
        else target.RPCA_ReviveAtPosition(position, true, segment);

        message = $"Respawned {TargetLabel(target)}.";
        return true;
    }

    private ResolvedItem? ResolveAppleBox()
    {
        if (UnityEngine.Random.value < 0.75f && TryFindItem(n => Has(n, "green") && Has(n, "apple"), out var green)) return green;
        return TryFindItem(n => Has(n, "apple") && !Has(n, "green"), out var apple) ? apple : TryFindItem(n => Has(n, "apple"), out apple) ? apple : null;
    }

    private ResolvedItem? ResolveMushroomBox()
    {
        var mushrooms = FindItems(n => Has(n, "mushroom") && !Has(n, "spring"));
        if (mushrooms.Count == 0) mushrooms = FindItems(n => Has(n, "mushroom"));
        return mushrooms.Count == 0 ? null : mushrooms[UnityEngine.Random.Range(0, mushrooms.Count)];
    }

    private ResolvedItem? ResolveSpecialtyFood()
    {
        return PickWeighted([
            new(n => Has(n, "scout") && Has(n, "cookie"), 2),
            new(n => Has(n, "cactus"), 24),
            new(n => Has(n, "chili") || Has(n, "chilli"), 24),
            new(n => Has(n, "coconut"), 25),
            new(n => Has(n, "orange"), 25)
        ]);
    }

    private ResolvedItem? ResolveUtilityProp()
    {
        return PickWeighted([
            new(n => Has(n, "bundle") && Has(n, "balloon"), 5),
            new(n => Has(n, "rope"), 50),
            new(n => Has(n, "piton"), 20),
            new(n => Has(n, "spring") && Has(n, "mushroom"), 15),
            new(n => Has(n, "balloon"), 10)
        ]);
    }

    private ResolvedItem? PickWeighted(WeightedQuery[] queries)
    {
        var total = queries.Sum(q => q.Weight);
        var roll = UnityEngine.Random.Range(0, total);
        foreach (var query in queries)
        {
            roll -= query.Weight;
            if (roll < 0 && TryFindItem(query.Match, out var item)) return item;
        }

        foreach (var query in queries)
            if (TryFindItem(query.Match, out var item)) return item;

        return null;
    }

    private static bool TryFindItem(Predicate<string> match, out ResolvedItem item)
    {
        foreach (var candidate in FindItems(match))
        {
            item = candidate;
            return true;
        }

        item = default;
        return false;
    }

    private static List<ResolvedItem> FindItems(Predicate<string> match)
    {
        var result = new List<ResolvedItem>();
        var database = ItemDatabase.Instance;
        if (database?.itemLookup == null) return result;

        foreach (var pair in database.itemLookup)
        {
            if (pair.Value == null) continue;
            var name = pair.Value.gameObject.name;
            if (match(Normalize(name))) result.Add(new ResolvedItem(pair.Key, pair.Value, name));
        }

        return result;
    }

    private void UpdateMiniGame()
    {
        var dt = Time.unscaledDeltaTime;
        if (Input.GetKeyDown(KeyCode.Space)) JumpMiniGame();

        _miniVelocity -= 3.8f * dt;
        _miniRunnerY = Mathf.Max(0f, _miniRunnerY + _miniVelocity * dt);
        if (_miniRunnerY <= 0f) _miniVelocity = 0f;

        _miniObstacleX -= (0.52f + _miniScore * 0.015f) * dt;
        if (_miniObstacleX < -0.08f)
        {
            _miniObstacleX = UnityEngine.Random.Range(0.95f, 1.25f);
            _miniScore++;
            _soulPoints = Mathf.Min(9999f, _soulPoints + _miniGameReward.Value);
            Notice($"+{_miniGameReward.Value} Soul Points");
        }

        if (_miniObstacleX is > 0.12f and < 0.23f && _miniRunnerY < 0.25f)
        {
            _miniObstacleX = 1f;
            _miniRunnerY = 0f;
            _miniVelocity = 0f;
            _miniScore = 0;
            Notice("Rift Hop reset.");
        }
    }

    private void JumpMiniGame()
    {
        if (_miniRunnerY <= 0.02f) _miniVelocity = 1.55f;
    }

    private void RefundTimedOutPurchases()
    {
        _timedOut.Clear();
        foreach (var pair in _pending)
        {
            if (Time.unscaledTime - pair.Value.StartedAt < RemoteTimeout) continue;
            _soulPoints += pair.Value.Purchase.Cost.Value;
            _localLimits.Add(pair.Value.Purchase.Limit, -1);
            _timedOut.Add(pair.Key);
        }

        foreach (var id in _timedOut) _pending.Remove(id);
        if (_timedOut.Count > 0) Notice("Host did not answer. Purchase refunded.");
    }

    private List<Character> GetTargets()
    {
        var targets = new List<Character>();
        if (Character.AllCharacters == null) return targets;

        foreach (var character in Character.AllCharacters)
        {
            if (character == null || character.view == null || character.player == null) continue;
            if (character.isBot || character.isScoutmaster) continue;
            targets.Add(character);
        }

        var local = Character.localCharacter;
        if (local != null && targets.Remove(local)) targets.Insert(0, local);
        return targets;
    }

    private static bool IsLocalGhost()
    {
        var character = Character.localCharacter;
        return character != null && character.data != null && (character.IsGhost || character.data.dead || character.data.fullyPassedOut);
    }

    private static int GetCurrentStage()
    {
        try
        {
            return (int)MapHandler.CurrentSegmentNumber;
        }
        catch
        {
            return 0;
        }
    }

    private static Vector3 GetRespawnPosition(Character target, out int segment)
    {
        segment = GetCurrentStage();
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

    private StageLimitState GetRemoteLimits(int actor)
    {
        if (_remoteLimits.TryGetValue(actor, out var limits)) return limits;
        limits = new StageLimitState();
        _remoteLimits[actor] = limits;
        return limits;
    }

    private static bool CanSpend(Purchase purchase, StageLimitState state)
    {
        return purchase.Limit switch
        {
            LimitKind.Box => state.Boxes < 2,
            LimitKind.Tick => state.Ticks < 2,
            LimitKind.Food => state.Food < 1,
            LimitKind.Prop => state.Props < 1,
            _ => true
        };
    }

    private static string LimitText(Purchase purchase)
    {
        return purchase.Limit switch
        {
            LimitKind.Box => "Box limit: 2/stage",
            LimitKind.Tick => "Tick limit: 2/stage",
            LimitKind.Food => "Food limit: 1/stage",
            LimitKind.Prop => "Prop limit: 1/stage",
            _ => "No stage use limit"
        };
    }

    private void Notice(string text)
    {
        _notice = text;
        _noticeUntil = Time.unscaledTime + 4f;
    }

    private static string TargetLabel(Character character)
    {
        if (character == null) return "Unknown";
        var state = character.data != null && (character.data.dead || character.data.fullyPassedOut) ? "dead" : "alive";
        return $"{character.characterName} ({state})";
    }

    private static string Normalize(string value) => value.Replace("_", " ").Replace("-", " ").ToLowerInvariant();
    private static bool Has(string name, string token) => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private enum Offer { AppleBox, MushroomBox, Tick, SpecialtyFood, UtilityProp, Respawn }
    private enum LimitKind { None, Box, Tick, Food, Prop }

    private sealed class Purchase
    {
        public Purchase(Offer offer, string name, string detail, LimitKind limit, ConfigEntry<int> cost)
        {
            Offer = offer;
            Name = name;
            Detail = detail;
            Limit = limit;
            Cost = cost;
        }

        public Offer Offer { get; }
        public string Name { get; }
        public string Detail { get; }
        public LimitKind Limit { get; }
        public ConfigEntry<int> Cost { get; }
    }

    private sealed class StageLimitState
    {
        public int Stage = int.MinValue;
        public int Boxes;
        public int Ticks;
        public int Food;
        public int Props;

        public void EnsureStage(int stage)
        {
            if (Stage == stage) return;
            Stage = stage;
            Boxes = 0;
            Ticks = 0;
            Food = 0;
            Props = 0;
        }

        public void Add(LimitKind limit, int amount)
        {
            switch (limit)
            {
                case LimitKind.Box:
                    Boxes = Mathf.Max(0, Boxes + amount);
                    break;
                case LimitKind.Tick:
                    Ticks = Mathf.Max(0, Ticks + amount);
                    break;
                case LimitKind.Food:
                    Food = Mathf.Max(0, Food + amount);
                    break;
                case LimitKind.Prop:
                    Props = Mathf.Max(0, Props + amount);
                    break;
            }
        }
    }

    private sealed class PendingPurchase
    {
        public PendingPurchase(Purchase purchase, int stage, float startedAt)
        {
            Purchase = purchase;
            Stage = stage;
            StartedAt = startedAt;
        }

        public Purchase Purchase { get; }
        public int Stage { get; }
        public float StartedAt { get; }
    }

    private readonly struct ResolvedItem
    {
        public ResolvedItem(ushort id, Item item, string name)
        {
            Id = id;
            Item = item;
            Name = name;
        }

        public ushort Id { get; }
        public Item Item { get; }
        public string Name { get; }
    }

    private readonly struct WeightedQuery
    {
        public WeightedQuery(Predicate<string> match, int weight)
        {
            Match = match;
            Weight = weight;
        }

        public Predicate<string> Match { get; }
        public int Weight { get; }
    }
}

internal sealed class GhostTickEffect : MonoBehaviour
{
    private Character _target = null!;
    private float _remaining;
    private float _pulse;

    public void Init(Character target)
    {
        _target = target;
        _remaining = 45f;
        _pulse = 0f;
    }

    private void Update()
    {
        if (_target == null || _target.data == null || _target.data.dead)
        {
            Destroy(this);
            return;
        }

        _remaining -= Time.deltaTime;
        _pulse -= Time.deltaTime;
        if (_pulse <= 0f)
        {
            _target.AddStamina(0.08f);
            _target.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Weight, 0.015f, true, false, true);
            _pulse = 5f;
        }

        if (_remaining <= 0f) Destroy(this);
    }
}
