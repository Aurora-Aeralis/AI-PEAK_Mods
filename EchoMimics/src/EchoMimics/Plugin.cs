using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityRandom = UnityEngine.Random;

namespace EchoMimics;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "aurora.aeralis.peak.echomimics";
    internal const string PluginName = "EchoMimics";
    internal const string PluginVersion = "0.1.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static ConfigEntry<bool> ModEnabled { get; private set; } = null!;
    internal static ConfigEntry<bool> RecordingEnabled { get; private set; } = null!;
    internal static ConfigEntry<string> MicrophoneDevice { get; private set; } = null!;
    internal static ConfigEntry<float> VoiceThreshold { get; private set; } = null!;
    internal static ConfigEntry<float> UrgentPeakThreshold { get; private set; } = null!;
    internal static ConfigEntry<float> MinClipSeconds { get; private set; } = null!;
    internal static ConfigEntry<float> MaxClipSeconds { get; private set; } = null!;
    internal static ConfigEntry<float> SilenceSeconds { get; private set; } = null!;
    internal static ConfigEntry<int> MaxStoredClips { get; private set; } = null!;
    internal static ConfigEntry<float> MemorySeconds { get; private set; } = null!;
    internal static ConfigEntry<float> ReplayMinDelay { get; private set; } = null!;
    internal static ConfigEntry<float> ReplayMaxDelay { get; private set; } = null!;
    internal static ConfigEntry<float> ReplayVolume { get; private set; } = null!;
    internal static ConfigEntry<float> PitchMin { get; private set; } = null!;
    internal static ConfigEntry<float> PitchMax { get; private set; } = null!;
    internal static ConfigEntry<bool> UseHostileFilter { get; private set; } = null!;
    internal static ConfigEntry<float> FilterCutoff { get; private set; } = null!;
    internal static ConfigEntry<float> EchoDelay { get; private set; } = null!;
    internal static ConfigEntry<float> EchoWetMix { get; private set; } = null!;

    static Plugin? Instance;
    Harmony Harmony = null!;

    void Awake()
    {
        Instance = this;
        Log = Logger;
        BindConfig();

        gameObject.AddComponent<EchoRecorder>();
        Harmony = new Harmony(PluginGuid);
        PatchZombieGrunts();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    void BindConfig()
    {
        ModEnabled = Config.Bind("General", "Enabled", true, "Enables zombie voice mimicry.");
        RecordingEnabled = Config.Bind("Recording", "Enabled", true, "Enables local microphone capture.");
        MicrophoneDevice = Config.Bind("Recording", "MicrophoneDevice", "", "Microphone device name. Blank uses the first available device.");
        VoiceThreshold = Config.Bind("Recording", "VoiceThreshold", 0.035f, "RMS level required to start recording a voice snippet.");
        UrgentPeakThreshold = Config.Bind("Recording", "UrgentPeakThreshold", 0.14f, "Peak level that marks a captured snippet as urgent.");
        MinClipSeconds = Config.Bind("Recording", "MinClipSeconds", 0.35f, "Minimum captured snippet length.");
        MaxClipSeconds = Config.Bind("Recording", "MaxClipSeconds", 2.75f, "Maximum captured snippet length.");
        SilenceSeconds = Config.Bind("Recording", "SilenceSeconds", 0.22f, "Silence duration that ends the current snippet.");
        MaxStoredClips = Config.Bind("Recording", "MaxStoredClips", 32, "Maximum voice snippets kept in memory.");
        MemorySeconds = Config.Bind("Recording", "MemorySeconds", 75f, "Seconds before old snippets are discarded.");
        ReplayMinDelay = Config.Bind("Replay", "MinDelay", 3.5f, "Minimum seconds between zombie mimic attempts.");
        ReplayMaxDelay = Config.Bind("Replay", "MaxDelay", 8f, "Maximum seconds between zombie mimic attempts.");
        ReplayVolume = Config.Bind("Replay", "Volume", 1f, "Voice replay volume multiplier.");
        PitchMin = Config.Bind("Replay", "PitchMin", 0.82f, "Minimum pitch used for zombie mimic playback.");
        PitchMax = Config.Bind("Replay", "PitchMax", 1.08f, "Maximum pitch used for zombie mimic playback.");
        UseHostileFilter = Config.Bind("Replay", "UseHostileFilter", true, "Applies low-pass and echo effects to zombie mimic playback.");
        FilterCutoff = Config.Bind("Replay", "FilterCutoff", 3300f, "Low-pass cutoff frequency for zombie mimic playback.");
        EchoDelay = Config.Bind("Replay", "EchoDelay", 95f, "Echo delay in milliseconds for zombie mimic playback.");
        EchoWetMix = Config.Bind("Replay", "EchoWetMix", 0.28f, "Echo wet mix for zombie mimic playback.");
    }

    void PatchZombieGrunts()
    {
        var Type = AccessTools.TypeByName("MushroomZombie");
        var Method = Type == null ? null : AccessTools.Method(Type, "ZombieGrunts");
        if (Method == null)
        {
            Log.LogWarning("MushroomZombie.ZombieGrunts was not found; zombie mimic playback is inactive.");
            return;
        }

        Harmony.Patch(Method, prefix: new HarmonyMethod(typeof(Plugin), nameof(ZombieGruntsPrefix)));
        Log.LogInfo("Patched MushroomZombie.ZombieGrunts.");
    }

    static bool ZombieGruntsPrefix(object __instance, ref IEnumerator __result)
    {
        if (Instance == null) return true;
        __result = Instance.ZombieVoiceLoop(__instance);
        return false;
    }

    IEnumerator ZombieVoiceLoop(object Zombie)
    {
        if (Zombie is not Component ZombieComponent) yield break;

        while (ZombieComponent != null)
        {
            yield return new WaitForSeconds(UnityRandom.Range(Mathf.Max(0.25f, ReplayMinDelay.Value), Mathf.Max(0.25f, ReplayMaxDelay.Value)));
            if (!ModEnabled.Value) continue;

            var Clip = EchoRecorder.Instance?.PickClip();
            var Source = Clip == null ? null : GetZombieSource(Zombie, ZombieComponent);
            if (Clip == null || Source == null) continue;

            ApplyHostileFilter(Source);
            Source.Stop();
            Source.pitch = UnityRandom.Range(PitchMin.Value, PitchMax.Value);
            Source.spatialBlend = Mathf.Max(Source.spatialBlend, 0.85f);
            Source.PlayOneShot(Clip, ReplayVolume.Value);
        }
    }

    static AudioSource? GetZombieSource(object Zombie, Component ZombieComponent)
    {
        if (AccessTools.Field(Zombie.GetType(), "gruntSource")?.GetValue(Zombie) is AudioSource Source) return Source;
        return ZombieComponent.GetComponentInChildren<AudioSource>(true);
    }

    static void ApplyHostileFilter(AudioSource Source)
    {
        if (!UseHostileFilter.Value) return;

        var LowPass = Source.GetComponent<AudioLowPassFilter>() ?? Source.gameObject.AddComponent<AudioLowPassFilter>();
        LowPass.cutoffFrequency = Mathf.Clamp(FilterCutoff.Value, 800f, 12000f);

        var Echo = Source.GetComponent<AudioEchoFilter>() ?? Source.gameObject.AddComponent<AudioEchoFilter>();
        Echo.delay = Mathf.Clamp(EchoDelay.Value, 10f, 500f);
        Echo.decayRatio = 0.35f;
        Echo.wetMix = Mathf.Clamp01(EchoWetMix.Value);
        Echo.dryMix = 1f;
    }

    void OnDestroy()
    {
        Harmony?.UnpatchSelf();
        if (Instance == this) Instance = null;
    }

    sealed class EchoRecorder : MonoBehaviour
    {
        internal static EchoRecorder? Instance { get; private set; }

        const int SampleRate = 24000;
        readonly List<EchoClip> Clips = new();
        readonly List<float> Segment = new(SampleRate * 3);
        AudioClip? MicClip;
        string Device = "";
        int LastPosition;
        float LastVoiceAt;
        float SegmentPeak;
        float SegmentEnergy;
        int SegmentSamples;

        void Awake() => Instance = this;

        void Update()
        {
            if (!RecordingEnabled.Value)
            {
                StopMic();
                return;
            }

            EnsureMic();
            PumpMic();
            TrimClips();
        }

        void EnsureMic()
        {
            var Devices = Microphone.devices;
            if (Devices.Length == 0)
            {
                StopMic();
                return;
            }

            var Desired = string.IsNullOrWhiteSpace(MicrophoneDevice.Value) ? Devices[0] : MicrophoneDevice.Value;
            if (!Devices.Contains(Desired)) Desired = Devices[0];
            if (MicClip != null && Device == Desired && Microphone.IsRecording(Device)) return;

            StopMic();
            Device = Desired;
            MicClip = Microphone.Start(Device, true, 60, SampleRate);
            LastPosition = 0;
        }

        void StopMic()
        {
            if (!string.IsNullOrEmpty(Device) && Microphone.IsRecording(Device)) Microphone.End(Device);
            MicClip = null;
            LastPosition = 0;
            Segment.Clear();
        }

        void PumpMic()
        {
            if (MicClip == null || string.IsNullOrEmpty(Device)) return;

            var Position = Microphone.GetPosition(Device);
            if (Position < 0 || Position == LastPosition) return;

            if (Position > LastPosition) ReadMic(LastPosition, Position - LastPosition);
            else
            {
                ReadMic(LastPosition, MicClip.samples - LastPosition);
                if (Position > 0) ReadMic(0, Position);
            }

            LastPosition = Position;
        }

        void ReadMic(int Offset, int Length)
        {
            if (MicClip == null || Length <= 0) return;

            var Channels = Mathf.Max(1, MicClip.channels);
            var Raw = new float[Length * Channels];
            if (!MicClip.GetData(Raw, Offset)) return;

            var Mono = new float[Length];
            var Sum = 0f;
            var Peak = 0f;

            for (var I = 0; I < Length; I++)
            {
                var Sample = 0f;
                for (var C = 0; C < Channels; C++) Sample += Raw[I * Channels + C];
                Sample /= Channels;
                Mono[I] = Sample;

                var Abs = Mathf.Abs(Sample);
                Peak = Mathf.Max(Peak, Abs);
                Sum += Sample * Sample;
            }

            PushSamples(Mono, Mathf.Sqrt(Sum / Mathf.Max(1, Length)), Peak);
        }

        void PushSamples(float[] Mono, float Rms, float Peak)
        {
            var Now = Time.unscaledTime;
            var HasVoice = Rms >= VoiceThreshold.Value || Peak >= VoiceThreshold.Value * 1.8f;
            if (!HasVoice && Segment.Count == 0) return;

            if (Segment.Count == 0)
            {
                SegmentPeak = 0f;
                SegmentEnergy = 0f;
                SegmentSamples = 0;
            }

            Segment.AddRange(Mono);
            SegmentPeak = Mathf.Max(SegmentPeak, Peak);
            SegmentEnergy += Rms * Rms * Mono.Length;
            SegmentSamples += Mono.Length;
            if (HasVoice) LastVoiceAt = Now;

            var TooLong = Segment.Count >= SampleRate * Mathf.Max(0.5f, MaxClipSeconds.Value);
            var Silent = !HasVoice && Now - LastVoiceAt >= Mathf.Max(0.05f, SilenceSeconds.Value);
            if (TooLong || Silent) FinishSegment();
        }

        void FinishSegment()
        {
            var Seconds = Segment.Count / (float)SampleRate;
            if (Seconds >= Mathf.Max(0.1f, MinClipSeconds.Value))
            {
                var Data = Segment.ToArray();
                var Clip = AudioClip.Create("EchoMimicVoice", Data.Length, 1, SampleRate, false);
                Clip.SetData(Data, 0);

                var Rms = Mathf.Sqrt(SegmentEnergy / Mathf.Max(1, SegmentSamples));
                var Urgent = SegmentPeak >= UrgentPeakThreshold.Value || Seconds <= 0.9f && SegmentPeak >= UrgentPeakThreshold.Value * 0.72f;
                var Score = Mathf.Clamp01(SegmentPeak / Mathf.Max(0.01f, UrgentPeakThreshold.Value)) + Mathf.Clamp01(Rms / Mathf.Max(0.01f, VoiceThreshold.Value)) * 0.5f;
                Clips.Add(new EchoClip(Clip, Time.unscaledTime, Score, Urgent));
                TrimClips();
            }

            Segment.Clear();
            SegmentPeak = 0f;
            SegmentEnergy = 0f;
            SegmentSamples = 0;
        }

        internal AudioClip? PickClip()
        {
            TrimClips();
            if (Clips.Count == 0) return null;

            var Total = 0f;
            foreach (var Clip in Clips) Total += Weight(Clip);

            var Roll = UnityRandom.value * Total;
            foreach (var Clip in Clips)
            {
                Roll -= Weight(Clip);
                if (Roll <= 0f) return Clip.Clip;
            }

            return Clips[Clips.Count - 1].Clip;
        }

        float Weight(EchoClip Clip)
        {
            var Age = Time.unscaledTime - Clip.CreatedAt;
            return 1f + Clip.Score * 2.5f + (Clip.Urgent ? 8f : 0f) + Mathf.Clamp01(1f - Age / Mathf.Max(1f, MemorySeconds.Value));
        }

        void TrimClips()
        {
            var Cutoff = Time.unscaledTime - Mathf.Max(5f, MemorySeconds.Value);
            for (var I = Clips.Count - 1; I >= 0; I--)
            {
                if (Clips[I].CreatedAt >= Cutoff) continue;
                Destroy(Clips[I].Clip);
                Clips.RemoveAt(I);
            }

            while (Clips.Count > Mathf.Max(1, MaxStoredClips.Value))
            {
                Destroy(Clips[0].Clip);
                Clips.RemoveAt(0);
            }
        }

        void OnDestroy()
        {
            StopMic();
            foreach (var Clip in Clips) Destroy(Clip.Clip);
            Clips.Clear();
            if (Instance == this) Instance = null;
        }
    }

    sealed class EchoClip
    {
        internal readonly AudioClip Clip;
        internal readonly float CreatedAt;
        internal readonly float Score;
        internal readonly bool Urgent;

        internal EchoClip(AudioClip Clip, float CreatedAt, float Score, bool Urgent)
        {
            this.Clip = Clip;
            this.CreatedAt = CreatedAt;
            this.Score = Score;
            this.Urgent = Urgent;
        }
    }
}
