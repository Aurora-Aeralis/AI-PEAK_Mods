using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Photon.Pun;
using UnityEngine;

namespace AeralisFoundation.Peak.ZombieForm;

[BepInAutoPlugin(id: "aeralisfoundation.peak.zombieform")]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<KeyboardShortcut> transformKey = null!;
    private ConfigEntry<bool> allowPassedOut = null!;
    private float lastTransformAttempt;

    private void Awake()
    {
        Log = Logger;
        transformKey = Config.Bind("Controls", "TransformKey", new KeyboardShortcut(KeyCode.F8), "Transforms the local character into PEAK's synced mushroom zombie form.");
        allowPassedOut = Config.Bind("Safety", "AllowPassedOutTransform", false, "Allow the hotkey while the local character is passed out but not dead.");
        Log.LogInfo($"Plugin {Name} is loaded. Press {transformKey.Value} to transform.");
    }

    private void Update()
    {
        if (transformKey.Value.IsDown()) TransformLocalCharacter();
    }

    private void TransformLocalCharacter()
    {
        if (Time.unscaledTime < lastTransformAttempt + 0.75f) return;
        lastTransformAttempt = Time.unscaledTime;

        var character = Character.localCharacter;
        if (character == null)
        {
            Log.LogWarning("Cannot transform: no local character is active.");
            return;
        }

        if (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            Log.LogWarning("Cannot transform: no active Photon room.");
            return;
        }

        if (character.data.zombified || character.isZombie)
        {
            Log.LogWarning("Cannot transform: the local character is already zombified.");
            return;
        }

        if (character.data.dead)
        {
            Log.LogWarning("Cannot transform: the local character is dead.");
            return;
        }

        if (character.data.passedOut && !allowPassedOut.Value)
        {
            Log.LogWarning("Cannot transform: the local character is passed out. Enable AllowPassedOutTransform to override this.");
            return;
        }

        character.view.RPC("RPCA_Zombify", RpcTarget.All, character.Center);
        Log.LogInfo("Requested network-visible zombie transformation.");
    }
}
