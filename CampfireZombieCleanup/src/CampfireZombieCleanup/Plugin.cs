using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace CampfireZombieCleanup;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "aurora.aeralis.peak.campfirezombiecleanup";
    internal const string PluginName = "CampfireZombieCleanup";
    internal const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
    internal static ConfigEntry<float> CheckIntervalSeconds { get; private set; } = null!;
    internal static ConfigEntry<float> RadiusPadding { get; private set; } = null!;
    internal static ConfigEntry<float> MinimumRadius { get; private set; } = null!;
    internal static ConfigEntry<bool> KillBeforeDespawn { get; private set; } = null!;
    internal static ConfigEntry<float> DespawnDelaySeconds { get; private set; } = null!;

    void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Removes zombies that enter an active campfire area.");
        CheckIntervalSeconds = Config.Bind("General", "CheckIntervalSeconds", 0.35f, "Seconds between campfire zombie checks.");
        RadiusPadding = Config.Bind("Campfire", "RadiusPadding", 1.5f, "Extra meters added to the campfire protection radius.");
        MinimumRadius = Config.Bind("Campfire", "MinimumRadius", 8f, "Minimum radius used if the campfire radius cannot be read.");
        KillBeforeDespawn = Config.Bind("Behavior", "KillBeforeDespawn", false, "Sets the zombie to Dead before despawning it.");
        DespawnDelaySeconds = Config.Bind("Behavior", "DespawnDelaySeconds", 0.15f, "Delay after killing a zombie before despawning it.");

        gameObject.AddComponent<CampfireZombieSweeper>();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    sealed class CampfireZombieSweeper : MonoBehaviour
    {
        readonly Dictionary<int, float> _pendingDespawn = new();
        readonly HashSet<int> _seenZombies = new();
        readonly List<int> _stalePending = new();
        Type? _zombieType;
        Type? _campfireType;
        FieldInfo? _campfireRadiusField;
        FieldInfo? _zombieCharacterField;
        MethodInfo? _dieMethod;
        MethodInfo? _destroyZombieMethod;
        MethodInfo? _characterCenterGetter;
        MethodInfo? _photonViewGetter;
        MethodInfo? _isMineGetter;
        float _nextCheck;
        bool _warnedMissingTypes;
        bool _warnedMissingDestroy;

        void Update()
        {
            if (!Enabled.Value || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + Mathf.Max(0.05f, CheckIntervalSeconds.Value);
            if (!ResolveTypes()) return;

            var campfires = UnityObject.FindObjectsByType(_campfireType, FindObjectsSortMode.None);
            if (campfires.Length == 0) return;

            var zombies = UnityObject.FindObjectsByType(_zombieType, FindObjectsSortMode.None);
            _seenZombies.Clear();
            foreach (var zombieObject in zombies)
            {
                if (zombieObject is not Component zombie || zombie == null) continue;
                var id = zombie.GetInstanceID();
                _seenZombies.Add(id);
                if (!OwnsZombie(zombieObject)) continue;

                if (_pendingDespawn.TryGetValue(id, out var despawnAt))
                {
                    if (Time.unscaledTime >= despawnAt) DespawnZombie(zombieObject, zombie, id);
                    continue;
                }

                if (!InCampfireArea(GetZombiePosition(zombieObject, zombie), campfires)) continue;
                if (KillBeforeDespawn.Value && _dieMethod != null)
                {
                    TryInvoke(_dieMethod, zombieObject);
                    _pendingDespawn[id] = Time.unscaledTime + Mathf.Max(0f, DespawnDelaySeconds.Value);
                    continue;
                }

                DespawnZombie(zombieObject, zombie, id);
            }

            if (_pendingDespawn.Count == 0) return;
            _stalePending.Clear();
            foreach (var pair in _pendingDespawn)
                if (!_seenZombies.Contains(pair.Key)) _stalePending.Add(pair.Key);
            foreach (var id in _stalePending) _pendingDespawn.Remove(id);
        }

        bool ResolveTypes()
        {
            _zombieType ??= AccessTools.TypeByName("MushroomZombie");
            _campfireType ??= AccessTools.TypeByName("Campfire");
            if (_zombieType == null || _campfireType == null)
            {
                if (!_warnedMissingTypes)
                {
                    Log.LogWarning("MushroomZombie or Campfire type was not found; campfire cleanup is inactive.");
                    _warnedMissingTypes = true;
                }

                return false;
            }

            _campfireRadiusField ??= AccessTools.Field(_campfireType, "moraleBoostRadius");
            _zombieCharacterField ??= AccessTools.Field(_zombieType, "character");
            _dieMethod ??= AccessTools.Method(_zombieType, "Die");
            _destroyZombieMethod ??= AccessTools.Method(_zombieType, "DestroyZombie");
            _photonViewGetter ??= AccessTools.PropertyGetter(_zombieType, "photonView");

            if (_destroyZombieMethod == null && !_warnedMissingDestroy)
            {
                Log.LogWarning("MushroomZombie.DestroyZombie was not found; falling back to local GameObject destruction.");
                _warnedMissingDestroy = true;
            }

            return true;
        }

        bool InCampfireArea(Vector3 position, UnityObject[] campfires)
        {
            foreach (var campfireObject in campfires)
            {
                if (campfireObject is not Component campfire || campfire == null || !campfire.gameObject.activeInHierarchy) continue;
                var radius = GetCampfireRadius(campfireObject);
                if ((position - campfire.transform.position).sqrMagnitude <= radius * radius) return true;
            }

            return false;
        }

        float GetCampfireRadius(object campfire)
        {
            var radius = _campfireRadiusField?.GetValue(campfire) is float value ? value : MinimumRadius.Value;
            return Mathf.Max(MinimumRadius.Value, radius) + Mathf.Max(0f, RadiusPadding.Value);
        }

        Vector3 GetZombiePosition(UnityObject zombieObject, Component zombie)
        {
            var character = _zombieCharacterField?.GetValue(zombieObject);
            if (character != null)
            {
                _characterCenterGetter ??= AccessTools.PropertyGetter(character.GetType(), "Center");
                if (_characterCenterGetter != null)
                {
                    try
                    {
                        if (_characterCenterGetter.Invoke(character, null) is Vector3 center) return center;
                    }
                    catch
                    {
                    }
                }

                if (character is Component characterComponent) return characterComponent.transform.position;
            }

            return zombie.transform.position;
        }

        bool OwnsZombie(UnityObject zombieObject)
        {
            if (_photonViewGetter == null) return true;

            try
            {
                var view = _photonViewGetter.Invoke(zombieObject, null);
                if (view == null) return true;
                _isMineGetter ??= AccessTools.PropertyGetter(view.GetType(), "IsMine");
                return _isMineGetter?.Invoke(view, null) is not bool isMine || isMine;
            }
            catch
            {
                return true;
            }
        }

        void DespawnZombie(UnityObject zombieObject, Component zombie, int id)
        {
            _pendingDespawn.Remove(id);
            if (_destroyZombieMethod != null && TryInvoke(_destroyZombieMethod, zombieObject)) return;
            UnityObject.Destroy(zombie.gameObject);
        }

        static bool TryInvoke(MethodInfo method, object target)
        {
            try
            {
                method.Invoke(target, null);
                return true;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"{method.DeclaringType?.Name}.{method.Name} failed: {ex.GetBaseException().Message}");
                return false;
            }
        }
    }
}
