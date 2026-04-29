using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace AeralisFoundation.ChromaticSkies;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "aeralisfoundation.peak.chromaticskies";
    public const string PluginName = "Chromatic Skies";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<bool> Enabled = null!;
    private ConfigEntry<SkyPreset> Preset = null!;
    private ConfigEntry<string> CustomTop = null!;
    private ConfigEntry<string> CustomHorizon = null!;
    private ConfigEntry<string> CustomGround = null!;
    private ConfigEntry<float> Brightness = null!;
    private ConfigEntry<float> Vibrance = null!;
    private ConfigEntry<float> Exposure = null!;
    private ConfigEntry<int> GradientResolution = null!;
    private ConfigEntry<bool> ApplyAmbientLighting = null!;
    private ConfigEntry<bool> ApplyFogColor = null!;
    private ConfigEntry<float> ReapplyInterval = null!;
    private ConfigEntry<bool> RestoreOnUnload = null!;

    private Material? OriginalSkybox;
    private AmbientMode OriginalAmbientMode;
    private Color OriginalAmbientSky;
    private Color OriginalAmbientEquator;
    private Color OriginalAmbientGround;
    private bool OriginalFog;
    private Color OriginalFogColor;
    private Material? SkyboxMaterial;
    private Cubemap? SkyboxTexture;
    private string LastSignature = "";
    private float Timer;
    private bool Applied;
    private bool MissingShaderWarned;

    private void Awake()
    {
        Log = Logger;
        BindConfig();
        CaptureOriginalRenderSettings();
        ApplySky(true);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        Timer += Time.unscaledDeltaTime;
        if (Timer < ReapplyInterval.Value) return;
        Timer = 0f;
        if (Enabled.Value) ApplySky(false);
        else if (Applied) RestoreSky();
    }

    private void OnDestroy()
    {
        if (RestoreOnUnload.Value) RestoreSky();
        if (SkyboxMaterial != null) Destroy(SkyboxMaterial);
        if (SkyboxTexture != null) Destroy(SkyboxTexture);
    }

    private void BindConfig()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Enables Chromatic Skies.");
        Preset = Config.Bind("Sky", "Preset", SkyPreset.ConcertGlow, "Sky color preset. Custom uses the values from Custom Colors.");
        Brightness = Config.Bind("Sky", "Brightness", 1.2f, new ConfigDescription("Color brightness multiplier.", new AcceptableValueRange<float>(0.1f, 3f)));
        Vibrance = Config.Bind("Sky", "Vibrance", 1.25f, new ConfigDescription("Color saturation multiplier.", new AcceptableValueRange<float>(0f, 3f)));
        Exposure = Config.Bind("Sky", "Exposure", 1.15f, new ConfigDescription("Skybox material exposure.", new AcceptableValueRange<float>(0.1f, 3f)));
        GradientResolution = Config.Bind("Sky", "GradientResolution", 64, new ConfigDescription("Generated cubemap face size.", new AcceptableValueRange<int>(16, 256)));
        ApplyAmbientLighting = Config.Bind("Lighting", "ApplyAmbientLighting", true, "Applies matching trilight ambient colors.");
        ApplyFogColor = Config.Bind("Lighting", "ApplyFogColor", true, "Applies a matching fog color without changing fog density.");
        ReapplyInterval = Config.Bind("Advanced", "ReapplyInterval", 0.5f, new ConfigDescription("Seconds between sky reapply attempts.", new AcceptableValueRange<float>(0.05f, 5f)));
        RestoreOnUnload = Config.Bind("Advanced", "RestoreOnUnload", true, "Restores the previous sky, ambient lighting, and fog color when unloaded.");
        CustomTop = Config.Bind("Custom Colors", "TopColor", "#4FB8FF", "Top sky color as a hex color.");
        CustomHorizon = Config.Bind("Custom Colors", "HorizonColor", "#FFC46B", "Horizon sky color as a hex color.");
        CustomGround = Config.Bind("Custom Colors", "GroundColor", "#2A1842", "Ground/lower sky color as a hex color.");
    }

    private void CaptureOriginalRenderSettings()
    {
        OriginalSkybox = RenderSettings.skybox;
        OriginalAmbientMode = RenderSettings.ambientMode;
        OriginalAmbientSky = RenderSettings.ambientSkyColor;
        OriginalAmbientEquator = RenderSettings.ambientEquatorColor;
        OriginalAmbientGround = RenderSettings.ambientGroundColor;
        OriginalFog = RenderSettings.fog;
        OriginalFogColor = RenderSettings.fogColor;
    }

    private void ApplySky(bool force)
    {
        if (!Enabled.Value) return;

        var (top, horizon, ground) = GetColors();
        var signature = $"{Preset.Value}|{CustomTop.Value}|{CustomHorizon.Value}|{CustomGround.Value}|{Brightness.Value:F3}|{Vibrance.Value:F3}|{Exposure.Value:F3}|{GradientResolution.Value}";
        var changed = force || signature != LastSignature;
        if (changed) BuildSkybox(top, horizon, ground);
        LastSignature = signature;

        if (SkyboxMaterial != null && RenderSettings.skybox != SkyboxMaterial)
        {
            RenderSettings.skybox = SkyboxMaterial;
            changed = true;
        }
        if (ApplyAmbientLighting.Value)
        {
            if (RenderSettings.ambientMode != AmbientMode.Trilight || RenderSettings.ambientSkyColor != top || RenderSettings.ambientEquatorColor != horizon || RenderSettings.ambientGroundColor != ground) changed = true;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = top;
            RenderSettings.ambientEquatorColor = horizon;
            RenderSettings.ambientGroundColor = ground;
        }
        if (ApplyFogColor.Value)
        {
            var fogColor = Color.Lerp(horizon, top, 0.25f);
            if (RenderSettings.fogColor != fogColor) changed = true;
            RenderSettings.fogColor = fogColor;
        }
        if (changed) DynamicGI.UpdateEnvironment();
        Applied = true;
    }

    private void BuildSkybox(Color top, Color horizon, Color ground)
    {
        var shader = Shader.Find("Skybox/Cubemap") ?? Shader.Find("Skybox/Procedural");
        if (shader == null)
        {
            if (!MissingShaderWarned) Log.LogWarning("No supported Unity skybox shader was found. Ambient and fog colors will still be applied.");
            MissingShaderWarned = true;
            return;
        }

        if (SkyboxMaterial == null || SkyboxMaterial.shader != shader)
        {
            if (SkyboxMaterial != null) Destroy(SkyboxMaterial);
            SkyboxMaterial = new Material(shader) { name = "Chromatic Skies Skybox", hideFlags = HideFlags.DontSave };
        }

        if (shader.name == "Skybox/Cubemap")
        {
            BuildCubemap(top, horizon, ground);
            SkyboxMaterial.SetTexture("_Tex", SkyboxTexture);
            if (SkyboxMaterial.HasProperty("_Tint")) SkyboxMaterial.SetColor("_Tint", Color.white);
            if (SkyboxMaterial.HasProperty("_Exposure")) SkyboxMaterial.SetFloat("_Exposure", Exposure.Value);
            return;
        }

        if (SkyboxMaterial.HasProperty("_SkyTint")) SkyboxMaterial.SetColor("_SkyTint", top);
        if (SkyboxMaterial.HasProperty("_GroundColor")) SkyboxMaterial.SetColor("_GroundColor", ground);
        if (SkyboxMaterial.HasProperty("_AtmosphereThickness")) SkyboxMaterial.SetFloat("_AtmosphereThickness", 1f);
        if (SkyboxMaterial.HasProperty("_Exposure")) SkyboxMaterial.SetFloat("_Exposure", Exposure.Value);
    }

    private void BuildCubemap(Color top, Color horizon, Color ground)
    {
        var size = GradientResolution.Value;
        if (SkyboxTexture == null || SkyboxTexture.width != size)
        {
            if (SkyboxTexture != null) Destroy(SkyboxTexture);
            SkyboxTexture = new Cubemap(size, TextureFormat.RGBA32, false) { name = "Chromatic Skies Gradient", hideFlags = HideFlags.DontSave };
        }

        var pixels = new Color[size * size];
        for (var faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            var face = (CubemapFace)faceIndex;
            for (var y = 0; y < size; y++)
            {
                var v = 2f * (y + 0.5f) / size - 1f;
                for (var x = 0; x < size; x++)
                {
                    var u = 2f * (x + 0.5f) / size - 1f;
                    pixels[y * size + x] = GetGradientColor(GetDirection(face, u, v), top, horizon, ground);
                }
            }
            SkyboxTexture.SetPixels(pixels, face);
        }
        SkyboxTexture.Apply(false);
    }

    private (Color Top, Color Horizon, Color Ground) GetColors()
    {
        var colors = Preset.Value == SkyPreset.Custom
            ? (ParseColor(CustomTop.Value, new Color(0.31f, 0.72f, 1f)), ParseColor(CustomHorizon.Value, new Color(1f, 0.77f, 0.42f)), ParseColor(CustomGround.Value, new Color(0.16f, 0.09f, 0.26f)))
            : GetPresetColors(Preset.Value);
        return (Adjust(colors.Item1), Adjust(colors.Item2), Adjust(colors.Item3));
    }

    private static (Color Top, Color Horizon, Color Ground) GetPresetColors(SkyPreset preset)
    {
        return preset switch
        {
            SkyPreset.VanillaBoost => (new Color(0.43f, 0.68f, 1f), new Color(0.95f, 0.83f, 0.62f), new Color(0.35f, 0.42f, 0.52f)),
            SkyPreset.AlienGreen => (new Color(0.14f, 1f, 0.43f), new Color(0.73f, 1f, 0.28f), new Color(0.02f, 0.18f, 0.12f)),
            SkyPreset.SunsetBloom => (new Color(0.45f, 0.16f, 0.82f), new Color(1f, 0.48f, 0.23f), new Color(0.16f, 0.05f, 0.16f)),
            SkyPreset.DeepViolet => (new Color(0.23f, 0.09f, 0.72f), new Color(0.9f, 0.3f, 0.88f), new Color(0.03f, 0.02f, 0.12f)),
            SkyPreset.IceBlue => (new Color(0.55f, 0.91f, 1f), new Color(0.82f, 1f, 0.95f), new Color(0.18f, 0.32f, 0.5f)),
            _ => (new Color(0.28f, 0.54f, 1f), new Color(1f, 0.78f, 0.22f), new Color(0.12f, 0.04f, 0.2f)),
        };
    }

    private Color Adjust(Color color)
    {
        var gray = (color.r + color.g + color.b) / 3f;
        var vibrance = Vibrance.Value;
        var brightness = Brightness.Value;
        return new Color(
            Mathf.Clamp01((gray + (color.r - gray) * vibrance) * brightness),
            Mathf.Clamp01((gray + (color.g - gray) * vibrance) * brightness),
            Mathf.Clamp01((gray + (color.b - gray) * vibrance) * brightness),
            1f
        );
    }

    private static Color ParseColor(string value, Color fallback)
    {
        return ColorUtility.TryParseHtmlString(value.Trim(), out var color) ? color : fallback;
    }

    private static Color GetGradientColor(Vector3 direction, Color top, Color horizon, Color ground)
    {
        var y = Mathf.Clamp(direction.y, -1f, 1f);
        return y >= 0f
            ? Color.Lerp(horizon, top, Mathf.Pow(y, 0.65f))
            : Color.Lerp(horizon, ground, Mathf.Pow(-y, 0.75f));
    }

    private static Vector3 GetDirection(CubemapFace face, float u, float v)
    {
        return face switch
        {
            CubemapFace.PositiveX => new Vector3(1f, -v, -u).normalized,
            CubemapFace.NegativeX => new Vector3(-1f, -v, u).normalized,
            CubemapFace.PositiveY => new Vector3(u, 1f, v).normalized,
            CubemapFace.NegativeY => new Vector3(u, -1f, -v).normalized,
            CubemapFace.PositiveZ => new Vector3(u, -v, 1f).normalized,
            _ => new Vector3(-u, -v, -1f).normalized,
        };
    }

    private void RestoreSky()
    {
        RenderSettings.skybox = OriginalSkybox;
        RenderSettings.ambientMode = OriginalAmbientMode;
        RenderSettings.ambientSkyColor = OriginalAmbientSky;
        RenderSettings.ambientEquatorColor = OriginalAmbientEquator;
        RenderSettings.ambientGroundColor = OriginalAmbientGround;
        RenderSettings.fog = OriginalFog;
        RenderSettings.fogColor = OriginalFogColor;
        DynamicGI.UpdateEnvironment();
        Applied = false;
    }
}
