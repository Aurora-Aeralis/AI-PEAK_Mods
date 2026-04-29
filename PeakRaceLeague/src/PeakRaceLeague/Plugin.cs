using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRaceLeague;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.aeralis.peak.raceleague";
    public const string PluginName = "PeakRaceLeague";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<KeyCode> _toggleHudKey = null!;
    private ConfigEntry<KeyCode> _queueKey = null!;
    private ConfigEntry<KeyCode> _startKey = null!;
    private ConfigEntry<KeyCode> _finishKey = null!;
    private ConfigEntry<KeyCode> _cancelKey = null!;
    private ConfigEntry<KeyCode> _copyCodeKey = null!;
    private ConfigEntry<string> _runnerName = null!;
    private ConfigEntry<string> _routeName = null!;
    private ConfigEntry<string> _matchSeed = null!;
    private ConfigEntry<string> _finishSceneTokens = null!;
    private ConfigEntry<float> _parSeconds = null!;
    private ConfigEntry<int> _queueWindowMinutes = null!;
    private ConfigEntry<int> _queuedCountdownSeconds = null!;
    private ConfigEntry<bool> _autoStartQueuedRace = null!;
    private ConfigEntry<bool> _autoCopyQueueCode = null!;
    private ConfigEntry<bool> _autoFinishOnSceneChange = null!;

    private readonly List<string> _messages = new();
    private LeagueState _state = new();
    private Rect _window = new(20f, 20f, 430f, 390f);
    private string _statePath = "";
    private string _queueCode = "";
    private string _activeQueueCode = "";
    private bool _showHud = true;
    private bool _queued;
    private bool _running;
    private bool _startedFromQueue;
    private float _startRealtime;
    private float _queuedStartRealtime;
    private DateTimeOffset _startedAtUtc;

    private void Awake()
    {
        Log = Logger;
        BindConfig();
        LoadState();
        SceneManager.activeSceneChanged += OnSceneChanged;
        PushMessage($"{PluginName} loaded. {_toggleHudKey.Value} toggles HUD.");
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        SaveState();
    }

    private void Update()
    {
        if (Input.GetKeyDown(_toggleHudKey.Value)) _showHud = !_showHud;
        if (Input.GetKeyDown(_queueKey.Value)) ToggleQueue();
        if (Input.GetKeyDown(_startKey.Value)) StartRun(_queued, _queueCode);
        if (Input.GetKeyDown(_finishKey.Value)) FinishRun("manual");
        if (Input.GetKeyDown(_cancelKey.Value)) CancelRun();
        if (Input.GetKeyDown(_copyCodeKey.Value)) CopyQueueCode();
        if (_queued && !_running && _autoStartQueuedRace.Value && Time.realtimeSinceStartup >= _queuedStartRealtime) StartRun(true, _queueCode);
    }

    private void OnGUI()
    {
        if (!_showHud) return;

        _window.width = Mathf.Min(430f, Screen.width - 20f);
        _window.height = Mathf.Min(390f, Screen.height - 20f);
        _window = GUI.Window(827403, _window, DrawWindow, PluginName);
    }

    private void DrawWindow(int id)
    {
        var rank = RankFor(_state.Rating);
        GUILayout.Label($"{_runnerName.Value} | {rank.Name} {rank.Icon} | {_state.Rating} RP");
        GUILayout.Label($"Route: {RouteLabel()} | Par: {FormatSeconds(Mathf.Max(1f, _parSeconds.Value))}");

        if (_running)
        {
            GUILayout.Space(6f);
            GUILayout.Label($"Race live: {FormatSeconds(Time.realtimeSinceStartup - _startRealtime)}");
            GUILayout.Label(_startedFromQueue && !string.IsNullOrWhiteSpace(_activeQueueCode) ? $"Match: {_activeQueueCode}" : "Match: solo/manual");
        }
        else if (_queued)
        {
            GUILayout.Space(6f);
            GUILayout.Label($"Queued: {_queueCode}");
            GUILayout.Label($"Band: {rank.Name} | Window: {Mathf.Max(1, _queueWindowMinutes.Value)} min");
            if (_autoStartQueuedRace.Value) GUILayout.Label($"Starts in: {FormatSeconds(Mathf.Max(0f, _queuedStartRealtime - Time.realtimeSinceStartup))}");
        }
        else
        {
            GUILayout.Space(6f);
            GUILayout.Label($"Ready. {_queueKey.Value} queues, {_startKey.Value} starts.");
        }

        GUILayout.Space(8f);
        GUILayout.Label($"PB: {BestForRoute(RouteLabel())}");
        GUILayout.Label($"Runs: {_state.TotalRuns} | Last delta: {_state.LastRatingDelta:+#;-#;0} RP");

        GUILayout.Space(8f);
        GUILayout.Label("Recent runs");
        foreach (var run in RecentRuns(6)) GUILayout.Label($"{run.Route}  {FormatSeconds(run.Seconds)}  {run.Rank}  {run.RatingAfter} RP");

        GUILayout.FlexibleSpace();
        foreach (var message in _messages.Skip(Math.Max(0, _messages.Count - 3))) GUILayout.Label(message);
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    private void BindConfig()
    {
        _runnerName = Config.Bind("Runner", "Name", Environment.UserName, "Runner name shown on the HUD and matchmaking card.");
        _routeName = Config.Bind("Race", "RouteName", "AnyPercent", "Route or category name used for PBs and matchmaking codes.");
        _parSeconds = Config.Bind("Race", "ParSeconds", 900f, "Expected par time for this route in seconds. Rating changes are based around this value.");
        _matchSeed = Config.Bind("Matchmaking", "Seed", "", "Optional shared seed. Runners using the same seed, route, rank, and queue window get the same code.");
        _queueWindowMinutes = Config.Bind("Matchmaking", "QueueWindowMinutes", 15, "Length of each deterministic matchmaking window.");
        _queuedCountdownSeconds = Config.Bind("Matchmaking", "QueuedCountdownSeconds", 20, "Countdown used when AutoStartQueuedRace is enabled.");
        _autoStartQueuedRace = Config.Bind("Matchmaking", "AutoStartQueuedRace", false, "Automatically start a queued race after the countdown.");
        _autoCopyQueueCode = Config.Bind("Matchmaking", "AutoCopyQueueCode", true, "Copy the queue code to clipboard when queueing.");
        _finishSceneTokens = Config.Bind("Race", "FinishSceneTokens", "End,Credits,Airport,Win", "Comma-separated scene name fragments that finish an active run.");
        _autoFinishOnSceneChange = Config.Bind("Race", "AutoFinishOnSceneChange", true, "Finish the active race if the new scene name matches FinishSceneTokens.");
        _toggleHudKey = Config.Bind("Controls", "ToggleHud", KeyCode.F8, "Toggle the race HUD.");
        _queueKey = Config.Bind("Controls", "Queue", KeyCode.F7, "Queue or unqueue for a race.");
        _startKey = Config.Bind("Controls", "StartRace", KeyCode.F6, "Start a race manually.");
        _finishKey = Config.Bind("Controls", "FinishRace", KeyCode.F5, "Finish the active race manually.");
        _cancelKey = Config.Bind("Controls", "CancelRace", KeyCode.F9, "Cancel the active race or queue.");
        _copyCodeKey = Config.Bind("Controls", "CopyQueueCode", KeyCode.F10, "Copy the active queue code to clipboard.");
    }

    private void ToggleQueue()
    {
        if (_running)
        {
            PushMessage("Finish or cancel the active race before queueing.");
            return;
        }

        _queued = !_queued;
        if (!_queued)
        {
            PushMessage("Unqueued.");
            return;
        }

        _queueCode = BuildQueueCode();
        _queuedStartRealtime = Time.realtimeSinceStartup + Mathf.Max(3, _queuedCountdownSeconds.Value);
        if (_autoCopyQueueCode.Value) CopyQueueCode();
        PushMessage($"Queued for {RouteLabel()}: {_queueCode}");
    }

    private void StartRun(bool fromQueue, string queueCode)
    {
        if (_running) return;

        _running = true;
        _queued = false;
        _startedFromQueue = fromQueue;
        _activeQueueCode = fromQueue ? queueCode : "";
        _startRealtime = Time.realtimeSinceStartup;
        _startedAtUtc = DateTimeOffset.UtcNow;
        PushMessage(fromQueue ? $"Race started from {_activeQueueCode}." : "Race started.");
    }

    private void FinishRun(string source)
    {
        if (!_running) return;

        var seconds = Mathf.Max(0.01f, Time.realtimeSinceStartup - _startRealtime);
        var route = RouteLabel();
        var before = _state.Rating;
        var delta = RatingDeltaFor(seconds, route, _startedFromQueue);
        var after = Clamp(before + delta, 0, 2400);
        var rank = RankFor(after);

        _state.Rating = after;
        _state.LastRatingDelta = delta;
        _state.TotalRuns++;
        _state.Records.Add(new RunRecord
        {
            Route = route,
            Seconds = seconds,
            StartedUtc = _startedAtUtc.ToString("O"),
            FinishedUtc = DateTimeOffset.UtcNow.ToString("O"),
            Queued = _startedFromQueue,
            QueueCode = _activeQueueCode,
            RatingBefore = before,
            RatingAfter = after,
            RatingDelta = delta,
            Rank = rank.Name
        });

        TrimRecords();
        SaveState();
        PushMessage($"Finished by {source}: {FormatSeconds(seconds)}, {delta:+#;-#;0} RP.");
        _running = false;
        _startedFromQueue = false;
        _activeQueueCode = "";
    }

    private void CancelRun()
    {
        if (_running)
        {
            _running = false;
            _startedFromQueue = false;
            _activeQueueCode = "";
            PushMessage("Race cancelled.");
            return;
        }

        if (!_queued) return;

        _queued = false;
        PushMessage("Queue cancelled.");
    }

    private int RatingDeltaFor(float seconds, string route, bool queued)
    {
        var par = Mathf.Max(1f, _parSeconds.Value);
        var target = Clamp(Mathf.RoundToInt(1000f + ((par - seconds) / par * 850f)), 100, 2300);
        var delta = Mathf.RoundToInt((target - _state.Rating) * 0.18f);
        var currentBest = BestSeconds(route);
        if (currentBest <= 0f || seconds < currentBest) delta += 20;
        if (queued) delta += 10;
        if (seconds <= par * 0.75f) delta += 10;
        return Clamp(delta, -85, 125);
    }

    private string BuildQueueCode()
    {
        var route = CodePart(RouteLabel(), 8);
        var rank = RankFor(_state.Rating);
        var window = Math.Max(1, _queueWindowMinutes.Value) * 60;
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / window;
        var seed = string.IsNullOrWhiteSpace(_matchSeed.Value) ? DateTimeOffset.UtcNow.ToString("yyyyMMdd") : _matchSeed.Value.Trim();
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{PluginGuid}|{route}|{rank.Name}|{bucket}|{seed}"));
        return $"PRL-{route}-{rank.Code}-{BitConverter.ToString(bytes).Replace("-", "").Substring(0, 6)}";
    }

    private void CopyQueueCode()
    {
        if (string.IsNullOrWhiteSpace(_queueCode))
        {
            PushMessage("No active queue code.");
            return;
        }

        GUIUtility.systemCopyBuffer = $"{_runnerName.Value} | {RouteLabel()} | {RankFor(_state.Rating).Name} {_state.Rating} RP | {_queueCode}";
        PushMessage("Queue card copied.");
    }

    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        if (!_running || !_autoFinishOnSceneChange.Value) return;

        foreach (var token in _finishSceneTokens.Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            if (newScene.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
            FinishRun($"scene {newScene.name}");
            return;
        }
    }

    private void LoadState()
    {
        _statePath = Path.Combine(Paths.ConfigPath, PluginName, "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        if (!File.Exists(_statePath)) return;

        try
        {
            _state = JsonUtility.FromJson<LeagueState>(File.ReadAllText(_statePath)) ?? new LeagueState();
            _state.Records ??= new List<RunRecord>();
        }
        catch (Exception ex)
        {
            _state = new LeagueState();
            Log.LogWarning($"Could not load state: {ex.Message}");
        }
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonUtility.ToJson(_state, true));
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Could not save state: {ex.Message}");
        }
    }

    private void TrimRecords()
    {
        const int limit = 80;
        if (_state.Records.Count <= limit) return;
        _state.Records.RemoveRange(0, _state.Records.Count - limit);
    }

    private IEnumerable<RunRecord> RecentRuns(int count) => _state.Records.OrderByDescending(x => x.FinishedUtc).Take(count);

    private string BestForRoute(string route)
    {
        var best = BestSeconds(route);
        return best <= 0f ? "none yet" : FormatSeconds(best);
    }

    private float BestSeconds(string route)
    {
        var best = _state.Records.Where(x => string.Equals(x.Route, route, StringComparison.OrdinalIgnoreCase)).Select(x => x.Seconds).DefaultIfEmpty(0f).Min();
        return best > 0f ? best : 0f;
    }

    private string RouteLabel() => string.IsNullOrWhiteSpace(_routeName.Value) ? "AnyPercent" : _routeName.Value.Trim();

    private static Rank RankFor(int rating)
    {
        if (rating >= 2000) return new Rank("Master", "MAS", "S");
        if (rating >= 1800) return new Rank("Diamond", "DIA", "D");
        if (rating >= 1600) return new Rank("Platinum", "PLA", "P");
        if (rating >= 1400) return new Rank("Gold", "GLD", "G");
        if (rating >= 1200) return new Rank("Silver", "SLV", "S");
        if (rating >= 900) return new Rank("Bronze", "BRZ", "B");
        return new Rank("Rookie", "ROK", "R");
    }

    private static string CodePart(string value, int max)
    {
        var text = new string(value.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(max).ToArray());
        return text.Length == 0 ? "ANY" : text;
    }

    private static string FormatSeconds(float seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.Hours > 0 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}" : $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private void PushMessage(string message)
    {
        Log.LogInfo(message);
        _messages.Add(message);
        if (_messages.Count > 10) _messages.RemoveAt(0);
    }

    [Serializable]
    private sealed class LeagueState
    {
        public int Rating = 1000;
        public int TotalRuns;
        public int LastRatingDelta;
        public List<RunRecord> Records = new();
    }

    [Serializable]
    private sealed class RunRecord
    {
        public string Route = "";
        public float Seconds;
        public string StartedUtc = "";
        public string FinishedUtc = "";
        public bool Queued;
        public string QueueCode = "";
        public int RatingBefore;
        public int RatingAfter;
        public int RatingDelta;
        public string Rank = "";
    }

    private readonly struct Rank
    {
        public Rank(string name, string code, string icon)
        {
            Name = name;
            Code = code;
            Icon = icon;
        }

        public string Name { get; }
        public string Code { get; }
        public string Icon { get; }
    }
}
