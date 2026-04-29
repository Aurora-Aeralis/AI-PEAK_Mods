using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AeralisFoundation.UniversalCampfireFuel;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "aeralisfoundation.peak.universalcampfirefuel";
    public const string PluginName = "Universal Campfire Fuel";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private static ConfigEntry<bool> Enabled = null!;
    private static ConfigEntry<int> BaseRequiredHeat = null!;
    private static ConfigEntry<int> HeatPerExtraScout = null!;
    private static ConfigEntry<int> MaxRequiredHeat = null!;
    private static ConfigEntry<int> MinimumItemHeat = null!;
    private static ConfigEntry<int> GeneratedHeatVariance = null!;
    private static ConfigEntry<string> HeatOverrides = null!;
    private static readonly ConditionalWeakTable<object, CampfireState> States = new();
    private static readonly int[] SixLogOrder = [0, 5, 2, 3, 1, 4];
    private static Plugin? Instance;
    private Harmony? _harmony;
    private bool _syncWarningLogged;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        BindConfig();
        _harmony = new Harmony(PluginGuid);
        PatchCampfire();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (Instance == this) Instance = null;
    }

    private void BindConfig()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Enables universal campfire fuel.");
        BaseRequiredHeat = Config.Bind("Heat", "BaseRequiredHeat", 12, new ConfigDescription("Heat required for one living scout.", new AcceptableValueRange<int>(1, 100)));
        HeatPerExtraScout = Config.Bind("Heat", "HeatPerExtraScout", 5, new ConfigDescription("Extra heat required for each additional living scout.", new AcceptableValueRange<int>(0, 50)));
        MaxRequiredHeat = Config.Bind("Heat", "MaxRequiredHeat", 30, new ConfigDescription("Maximum heat required no matter how many scouts are present.", new AcceptableValueRange<int>(1, 200)));
        MinimumItemHeat = Config.Bind("Items", "MinimumItemHeat", 2, new ConfigDescription("Minimum generated heat for items without an override.", new AcceptableValueRange<int>(1, 50)));
        GeneratedHeatVariance = Config.Bind("Items", "GeneratedHeatVariance", 5, new ConfigDescription("Generated heat range above the minimum for items without an override.", new AcceptableValueRange<int>(1, 50)));
        HeatOverrides = Config.Bind("Items", "HeatOverrides", "28=8,firewood=8,wood=8,log=8,stick=6,branch=6,rope=4,food=3,berry=3,mushroom=3", "Comma-separated item heat overrides. Keys can be item IDs or normalized name fragments.");
    }

    private void PatchCampfire()
    {
        var campfire = AccessTools.TypeByName("Campfire");
        if (campfire == null)
        {
            Log.LogWarning("Campfire type was not found. The mod will stay idle until PEAK exposes the expected type.");
            return;
        }

        Patch(campfire, "Awake", postfix: nameof(CampfireAwakePostfix));
        Patch(campfire, "SetFireWoodCount", prefix: nameof(CampfireSetFireWoodCountPrefix));
        Patch(campfire, "GetInteractionText", prefix: nameof(CampfireGetInteractionTextPrefix));
        Patch(campfire, "Interact", prefix: nameof(CampfireInteractPrefix));
        Patch(campfire, "Interact_CastFinished", prefix: nameof(CampfireInteractCastFinishedPrefix));
    }

    private void Patch(Type type, string methodName, string? prefix = null, string? postfix = null)
    {
        var original = AccessTools.Method(type, methodName);
        if (original == null)
        {
            Log.LogWarning($"Campfire.{methodName} was not found.");
            return;
        }

        _harmony!.Patch(
            original,
            prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix)
        );
    }

    private static void CampfireAwakePostfix(object __instance)
    {
        if (Enabled.Value) SetHeat(__instance, 0);
    }

    private static bool CampfireSetFireWoodCountPrefix(object __instance, int count)
    {
        if (!Enabled.Value) return true;
        SetHeat(__instance, count);
        return false;
    }

    private static bool CampfireGetInteractionTextPrefix(object __instance, ref string __result)
    {
        if (!Enabled.Value) return true;
        __result = GetInteractionText(__instance);
        return false;
    }

    private static bool CampfireInteractPrefix(object __instance, object interactor)
    {
        return !Enabled.Value || Interact(__instance, interactor);
    }

    private static bool CampfireInteractCastFinishedPrefix(object __instance, object interactor)
    {
        return !Enabled.Value || IsLit(__instance) || GetHeat(__instance) >= GetRequiredHeat();
    }

    private static string GetInteractionText(object campfire)
    {
        if (IsLit(campfire)) return "COOK";

        var heat = GetHeat(campfire);
        var required = GetRequiredHeat();
        if (heat >= required)
        {
            var gate = EveryoneInRange(campfire);
            return gate.Allowed ? "LIGHT" : gate.Text;
        }

        var itemHeat = GetItemHeat(GetCurrentItem(GetLocalCharacter()));
        var progress = Math.Min(heat, required);
        return itemHeat > 0 ? $"ADD FUEL +{itemHeat} ({progress}/{required})" : $"ADD FUEL ({progress}/{required})";
    }

    private static bool Interact(object campfire, object interactor)
    {
        if (IsLit(campfire)) return true;
        if (GetHeat(campfire) >= GetRequiredHeat()) return true;

        var item = GetCurrentItem(interactor);
        var itemHeat = GetItemHeat(item);
        if (item == null || itemHeat <= 0) return false;

        var heat = GetHeat(campfire) + itemHeat;
        SetHeat(campfire, heat);
        SyncHeat(campfire, heat);
        ConsumeItem(item, interactor);
        return false;
    }

    private static int GetHeat(object campfire)
    {
        return States.TryGetValue(campfire, out var state) ? state.Heat : 0;
    }

    private static void SetHeat(object campfire, int heat)
    {
        States.GetOrCreateValue(campfire).Heat = Math.Max(0, heat);
        UpdateLogs(campfire);
    }

    private static int GetRequiredHeat()
    {
        var scouts = Math.Max(1, GetLivingScoutCount());
        var required = BaseRequiredHeat.Value + (scouts - 1) * HeatPerExtraScout.Value;
        return Math.Max(1, Math.Min(required, Math.Max(1, MaxRequiredHeat.Value)));
    }

    private static int GetLivingScoutCount()
    {
        var characters = GetStaticMember(AccessTools.TypeByName("Character"), "AllCharacters") as IEnumerable;
        if (characters == null) return 1;

        var count = 0;
        foreach (var character in characters)
        {
            if (character != null && IsLivingCharacter(character)) count++;
        }

        return count;
    }

    private static bool IsLivingCharacter(object character)
    {
        var data = GetMember(character, "data");
        return data == null || !GetBoolMember(data, "dead");
    }

    private static bool IsLit(object campfire)
    {
        return GetBoolMember(campfire, "Lit");
    }

    private static object? GetLocalCharacter()
    {
        return GetStaticMember(AccessTools.TypeByName("Character"), "localCharacter");
    }

    private static object? GetCurrentItem(object? character)
    {
        return GetMember(GetMember(character, "data"), "currentItem");
    }

    private static int GetItemHeat(object? item)
    {
        if (item == null) return 0;

        var id = GetIntMember(item, "itemID", -1);
        var name = Normalize(ObjectName(item));
        if (TryGetOverride(id, name, out var heat)) return heat;

        heat = MinimumItemHeat.Value + StableHash($"{id}:{name}") % Math.Max(1, GeneratedHeatVariance.Value);
        if (ContainsAny(name, "firewood", "wood", "log")) heat = Math.Max(heat, 8);
        else if (ContainsAny(name, "stick", "branch")) heat = Math.Max(heat, 6);
        else if (CanBeCooked(item) || ContainsAny(name, "food", "berry", "mushroom", "apple", "coconut", "orange")) heat = Math.Max(heat, 3);
        return Math.Max(1, heat);
    }

    private static bool TryGetOverride(int id, string name, out int heat)
    {
        foreach (var entry in HeatOverrides.Value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split(['=', ':'], 2);
            if (pair.Length != 2 || !int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out heat)) continue;

            var key = Normalize(pair[0]);
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyId))
            {
                if (id == keyId) return true;
            }
            else if (key.Length > 0 && name.Contains(key)) return true;
        }

        heat = 0;
        return false;
    }

    private static bool CanBeCooked(object item)
    {
        return GetBoolMember(GetMember(item, "cooking"), "canBeCooked");
    }

    private static void ConsumeItem(object item, object interactor)
    {
        try
        {
            var consume = FindMethod(item.GetType(), "Consume", 1);
            if (consume != null)
            {
                consume.Invoke(item, [GetPhotonViewId(interactor)]);
                return;
            }

            var gameObject = GetGameObject(item);
            if (gameObject != null) Destroy(gameObject);
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to consume campfire fuel item: {ex.Message}");
        }
    }

    private static int GetPhotonViewId(object obj)
    {
        return GetIntMember(GetMember(obj, "photonView"), "ViewID", -1);
    }

    private static void SyncHeat(object campfire, int heat)
    {
        try
        {
            var photonViewType = AccessTools.TypeByName("Photon.Pun.PhotonView");
            var rpcTargetType = AccessTools.TypeByName("Photon.Pun.RpcTarget");
            var gameObject = GetGameObject(campfire);
            if (photonViewType == null || rpcTargetType == null || gameObject == null) return;

            var photonView = gameObject.GetComponent(photonViewType);
            if (photonView == null) return;

            var target = Enum.Parse(rpcTargetType, "All");
            var rpc = FindRpcMethod(photonView.GetType(), rpcTargetType);
            rpc?.Invoke(photonView, ["SetFireWoodCount", target, new object[] { heat }]);
        }
        catch (Exception ex)
        {
            var plugin = Instance;
            if (plugin == null || plugin._syncWarningLogged) return;
            plugin._syncWarningLogged = true;
            Log.LogWarning($"Campfire heat sync failed once and will be local-only until the next successful RPC path: {ex.Message}");
        }
    }

    private static MethodInfo? FindRpcMethod(Type type, Type rpcTargetType)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameters = method.GetParameters();
            if (method.Name == "RPC" && parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == rpcTargetType && parameters[2].ParameterType == typeof(object[])) return method;
        }

        return null;
    }

    private static (bool Allowed, string Text) EveryoneInRange(object campfire)
    {
        var method = FindMethod(campfire.GetType(), "EveryoneInRange", 1);
        if (method == null) return (true, "");

        try
        {
            object?[] args = [""];
            var allowed = method.Invoke(campfire, args) as bool? ?? true;
            return (allowed, args[0] as string ?? "");
        }
        catch
        {
            return (true, "");
        }
    }

    private static void UpdateLogs(object campfire)
    {
        var root = FindLogRoot(campfire);
        if (root == null) return;

        var childCount = root.childCount;
        for (var i = 0; i < childCount; i++) root.GetChild(i).gameObject.SetActive(false);

        var active = Mathf.CeilToInt(Mathf.Clamp01((float)GetHeat(campfire) / GetRequiredHeat()) * childCount);
        for (var i = 0; i < active; i++)
        {
            var index = childCount == 6 ? SixLogOrder[i] : i;
            if (index < childCount) root.GetChild(index).gameObject.SetActive(true);
        }
    }

    private static Transform? FindLogRoot(object campfire)
    {
        if (GetMember(campfire, "logRoot") is Transform logRoot && logRoot != null) return logRoot;
        return campfire is Component component ? FindLogsRecursive(component.transform, 0) : null;
    }

    private static Transform? FindLogsRecursive(Transform parent, int depth)
    {
        if (depth > 4) return null;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name.IndexOf("Logs", StringComparison.OrdinalIgnoreCase) >= 0) return child;
            var found = FindLogsRecursive(child, depth + 1);
            if (found != null) return found;
        }

        return null;
    }

    private static GameObject? GetGameObject(object obj)
    {
        return obj is Component component ? component.gameObject : GetMember(obj, "gameObject") as GameObject;
    }

    private static object? GetMember(object? obj, string name)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        return AccessTools.Field(type, name)?.GetValue(obj) ?? AccessTools.Property(type, name)?.GetValue(obj);
    }

    private static object? GetStaticMember(Type? type, string name)
    {
        return type == null ? null : AccessTools.Field(type, name)?.GetValue(null) ?? AccessTools.Property(type, name)?.GetValue(null);
    }

    private static bool GetBoolMember(object? obj, string name, bool fallback = false)
    {
        var value = GetMember(obj, name);
        return value is bool boolean ? boolean : fallback;
    }

    private static int GetIntMember(object? obj, string name, int fallback = 0)
    {
        var value = GetMember(obj, name);
        return value is int integer ? integer : fallback;
    }

    private static MethodInfo? FindMethod(Type type, string name, int parameterCount)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.Name == name && method.GetParameters().Length == parameterCount) return method;
        }

        return null;
    }

    private static string ObjectName(object obj)
    {
        return obj is UnityEngine.Object unityObject ? unityObject.name : GetMember(obj, "name") as string ?? obj.GetType().Name;
    }

    private static string Normalize(string value)
    {
        var chars = new char[value.Length];
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = char.ToLowerInvariant(value[i]);
            if (char.IsLetterOrDigit(c)) chars[count++] = c;
        }

        return new string(chars, 0, count);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        for (var i = 0; i < needles.Length; i++)
            if (value.Contains(needles[i])) return true;
        return false;
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private sealed class CampfireState
    {
        public int Heat;
    }
}
