using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

namespace TutorialBackport.Client;

internal sealed class AmbientFirefight : MonoBehaviour
{
    private static AmbientFirefight _instance;
    internal static event Action<AreaController, Vector3, double, int> Gunfire;

    private readonly List<AreaController> _controllers = new List<AreaController>();
    private float[] _densities = Array.Empty<float>();
    private int _maxTotal;
    private float _interval;
    private int _roundRobin;
    private bool _running;
    private float _envDb;

    private const float GroupIndoorDb = 0f, GroupOutdoorDb = 0f;
    private MixerData _mixer;
    internal AudioMixerGroup Group;
    internal readonly PriorityCalculator Priority = new PriorityCalculator(256, 128, 64);

    internal static float VolumeFactor => _instance == null ? 1f : Mathf.Pow(10f, _instance._envDb / 20f);

    public static void OnTutorialRaidStarted()
    {
        Stop();
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "data", "firefight.json");
            JObject data = JObject.Parse(File.ReadAllText(path));
            var go = new GameObject("TutorialBackport_AmbientFirefight");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AmbientFirefight>();
            _instance.Load(data);
        }
        catch (Exception error)
        {
            Log.Warning("Firefight", "distant firefight not started: " + error.Message);
            Stop();
        }
    }

    public static void Stop()
    {
        if (_instance != null)
        {
            _instance._running = false;
            foreach (AreaController c in _instance._controllers)
            {
                c.Dispose();
            }

            Destroy(_instance.gameObject);
            _instance = null;
        }

        if (_bankBundle != null)
        {
            _bankBundle.Unload(true);
            _bankBundle = null;
        }

        _bankCache = null;
    }

    private static Dictionary<string, SoundBank> _bankCache;
    private static AssetBundle _bankBundle;

    private static void LoadBankBundle()
    {
        if (_bankBundle != null)
        {
            return;
        }

        string path = Path.Combine(Path.GetDirectoryName(typeof(AmbientFirefight).Assembly.Location), "bundles", "tutorialbackport_firefight.bundle");
        if (!File.Exists(path))
        {
            Log.Warning("Firefight", "bank bundle missing: " + path);
            return;
        }

        _bankBundle = AssetBundle.LoadFromFile(path);
        SoundBank[] loaded = _bankBundle != null ? _bankBundle.LoadAllAssets<SoundBank>() : Array.Empty<SoundBank>();
        int repaired = RepairBlendOptions(loaded);
    }

    private static int RepairBlendOptions(SoundBank[] banks)
    {
        DistanceBlendOptions standart = Resources.FindObjectsOfTypeAll<DistanceBlendOptions>().FirstOrDefault(b => b != null && b.name == "Standart");
        if (standart == null)
        {
            standart = ScriptableObject.CreateInstance<DistanceBlendOptions>();
            standart.name = "Standart";
            standart.Values = new[] { 5f, 40f, 60f, 100f };
        }

        int repaired = 0;
        foreach (SoundBank bank in banks)
        {
            if (bank != null && bank.BlendOptions == null)
            {
                bank.BlendOptions = standart;
                repaired++;
            }
        }

        return repaired;
    }

    public static void PrewarmBanks()
    {
        LoadBankBundle();
        _bankCache = new Dictionary<string, SoundBank>();
        foreach (SoundBank bank in Resources.FindObjectsOfTypeAll<SoundBank>())
        {
            if (bank != null && !_bankCache.ContainsKey(bank.name))
            {
                _bankCache[bank.name] = bank;
            }
        }
    }

    private void Load(JObject data)
    {
        if (_bankCache == null || _bankCache.Values.Any(b => b == null))
        {
            PrewarmBanks();
        }

        var banks = _bankCache;

        var missing = new List<string>();
        SoundBank Bank(JToken name)
        {
            string n = (string)name;
            if (n == null)
            {
                return null;
            }

            if (banks.TryGetValue(n, out SoundBank b))
            {
                return b;
            }

            missing.Add(n);
            return null;
        }

        JObject p = (JObject)data["preset"];
        var preset = new Preset
        {
            DensityCurve = Curve((JObject)p["densityCurve"]),
            DensityPeriodSec = (float)p["densityPeriodSec"],
            BaseDensity = (float)p["baseDensity"],
            GrenadeEventsPerShooterPerMinuteAtMaxDensity = (float)p["grenadeEventsPerShooterPerMinuteAtMaxDensity"],
            InitialCooldownRangeSec = V2(p["initialCooldownRangeSec"]),
            BaseCooldownAtZeroDensitySec = (float)p["baseCooldownAtZeroDensitySec"],
            BaseCooldownAtMaxDensitySec = (float)p["baseCooldownAtMaxDensitySec"],
            CooldownJitterRangeSec = V2(p["cooldownJitterRangeSec"]),
            NoProfileRetryCooldownSec = (float)p["noProfileRetryCooldownSec"],
            BackoffAtZeroDensitySec = (float)p["backoffAtZeroDensitySec"],
            BackoffAtMaxDensitySec = (float)p["backoffAtMaxDensitySec"],
            EnableCallAndResponse = (bool)p["enableCallAndResponse"],
            ResponseProbability = (float)p["responseProbability"],
            ResponseDelayRange = V2(p["responseDelayRange"]),
            ResponseShotsRange = V2(p["responseShotsRange"]),
            ResponseCooldownRange = V2(p["responseCooldownRange"]),
        };
        foreach (JObject w in p["weaponProfiles"])
        {
            JObject a = (JObject)w["audio"];
            preset.WeaponProfiles.Add(new WeaponProfile
            {
                BurstSizeRange = V2(w["burstSizeRange"]),
                VolumeJitter = V2(w["volumeJitter"]),
                PitchJitter = V2(w["pitchJitter"]),
                SingleShotStartJitterSec = V2(w["singleShotStartJitterSec"]),
                SingleShotGapRangeSec = V2(w["singleShotGapRangeSec"]),
                BurstSequenceGapRangeSec = V2(w["burstSequenceGapRangeSec"]),
                AutoFireBias = (float)w["autoFireBias"],
                Audio = new FirearmAudio
                {
                    SingleShots = a["singleShots"].Select(Bank).Where(b => b != null).ToArray(),
                    LoopTailSets = a["loopTailSets"].Select(s => new LoopTailSet { Rpm = (float)s["rpm"], Loop = Bank(s["loop"]), Tail = Bank(s["tail"]) })
                        .Where(s => s.Loop != null && s.Tail != null).ToArray(),
                    GrenadeBanks = a["grenadeBanks"].Select(Bank).Where(b => b != null).ToArray()
                }
            });
        }

        JObject d = (JObject)data["director"];
        _maxTotal = (int)d["maxConcurrentSequencesTotal"];
        _interval = Mathf.Max(0f, (float)d["updateIntervalSec"]);
        JObject m = (JObject)data["mixer"];
        _mixer = new MixerData { IndoorDb = (float)m["indoorDb"], OutdoorDb = (float)m["outdoorDb"], BlendSec = (float)m["blendSec"] };
        _envDb = _mixer.IndoorDb + GroupIndoorDb;
        Group = Singleton<BetterAudio>.Instantiated ? (Singleton<BetterAudio>.Instance.OutEnvironment ?? Singleton<BetterAudio>.Instance.MasterMixerGroup) : null;

        foreach (JObject a in data["areas"])
        {
            var area = new Area
            {
                Name = (string)a["name"],
                Position = new Vector3((float)a["position"][0], (float)a["position"][1], (float)a["position"][2]),
                Rotation = new Quaternion((float)a["rotation"][0], (float)a["rotation"][1], (float)a["rotation"][2], (float)a["rotation"][3]),
                Radius = (float)a["radius"],
                LocalDensity = (float)a["localDensity"],
                UseSector = (bool)a["useSector"],
                SectorApertureDeg = (float)a["sectorApertureDeg"],
                ForwardOffsetPercentRange = V2(a["forwardOffsetPercentRange"]),
                AudioPreset = preset
            };
            _controllers.Add(new AreaController(this, area));
        }

        int found = preset.WeaponProfiles.Sum(w => w.Audio.SingleShots.Length + w.Audio.LoopTailSets.Length * 2 + w.Audio.GrenadeBanks.Length);
        if (found == 0)
        {
            Log.Warning("Firefight", "no firefight sound banks are loaded - distant firefight off");
            return;
        }

        _running = true;
        StartCoroutine(Ticker());
    }

    private IEnumerator Ticker()
    {
        var wait = new WaitForSecondsRealtime(_interval);
        while (_running)
        {
            try
            {
                Tick(_interval);
            }
            catch (Exception)
            {
            }

            yield return wait;
        }
    }

    private void Update()
    {
        Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
        bool outdoor = player != null && player.Environment == EnvironmentType.Outdoor;
        float target = outdoor ? _mixer.OutdoorDb + GroupOutdoorDb : _mixer.IndoorDb + GroupIndoorDb;
        float step = _mixer.BlendSec > 0f ? Mathf.Abs((_mixer.OutdoorDb + GroupOutdoorDb) - (_mixer.IndoorDb + GroupIndoorDb)) * Time.unscaledDeltaTime / _mixer.BlendSec : 999f;
        _envDb = Mathf.MoveTowards(_envDb, target, step);
    }

    private void Tick(float dt)
    {
        DistributeConcurrencyByDensity();
        foreach (AreaController c in _controllers)
        {
            c.Tick(dt);
        }
    }

    private void DistributeConcurrencyByDensity()
    {
        int count = _controllers.Count;
        if (count == 0)
        {
            return;
        }

        int total = Mathf.Max(0, _maxTotal);
        if (total == 0)
        {
            _controllers.ForEach(c => c.SetMaxActiveSequences(0));
            return;
        }

        double time = AudioSettings.dspTime;
        if (_densities.Length != count)
        {
            _densities = new float[count];
        }

        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            Area a = _controllers[i].Area;
            _densities[i] = a.AudioPreset.EvaluateDensity(time, a.LocalDensity);
            sum += _densities[i];
        }

        if (Mathf.Approximately(sum, 0f))
        {
            _controllers.ForEach(c => c.SetMaxActiveSequences(0));
            return;
        }

        int start = _roundRobin >= 0 && _roundRobin < count ? _roundRobin : 0;
        int assigned = 0;
        float acc = 0f;
        for (int k = 0; k < count; k++)
        {
            int i = (start + k) % count;
            acc += _densities[i] / sum * total;
            int n = (int)Math.Round(acc) - assigned;
            n = n < 0 ? 0 : Math.Min(n, total - assigned);
            assigned += n;
            _controllers[i].SetMaxActiveSequences(n);
        }

        _roundRobin = (start + 1) % Mathf.Max(1, count);
    }

    internal static void RaiseGunfire(AreaController from, Vector3 position, double time, int shots) => Gunfire?.Invoke(from, position, time, shots);

    private void OnDestroy()
    {
        _running = false;
    }

    private static Vector2 V2(JToken t) => new Vector2((float)t[0], (float)t[1]);

    private static AnimationCurve Curve(JObject c)
    {
        var keys = c["keys"].Select(k => new Keyframe((float)k[0], (float)k[1], (float)k[2], (float)k[3], (float)k[4], (float)k[5])
        {
            weightedMode = (WeightedMode)(int)k[6]
        }).ToArray();
        return new AnimationCurve(keys);
    }

    private struct MixerData
    {
        public float IndoorDb;
        public float OutdoorDb;
        public float BlendSec;
    }

    internal sealed class Area
    {
        public string Name;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Radius;
        public float LocalDensity;
        public bool UseSector;
        public float SectorApertureDeg;
        public Vector2 ForwardOffsetPercentRange;
        public Preset AudioPreset;
    }

    internal sealed class Preset
    {
        public readonly List<WeaponProfile> WeaponProfiles = new List<WeaponProfile>();
        public AnimationCurve DensityCurve;
        public float DensityPeriodSec;
        public float BaseDensity;
        public float GrenadeEventsPerShooterPerMinuteAtMaxDensity;
        public Vector2 InitialCooldownRangeSec;
        public float BaseCooldownAtZeroDensitySec;
        public float BaseCooldownAtMaxDensitySec;
        public Vector2 CooldownJitterRangeSec;
        public float NoProfileRetryCooldownSec;
        public float BackoffAtZeroDensitySec;
        public float BackoffAtMaxDensitySec;
        public bool EnableCallAndResponse;
        public float ResponseProbability;
        public Vector2 ResponseDelayRange;
        public Vector2 ResponseShotsRange;
        public Vector2 ResponseCooldownRange;

        public float EvaluateDensity(double timeSec, float localMultiplier)
        {
            if (0.01f >= DensityPeriodSec)
            {
                return Mathf.Clamp01(BaseDensity * localMultiplier);
            }

            float phase = (float)(timeSec % DensityPeriodSec / DensityPeriodSec);
            float curve = Mathf.Clamp01(DensityCurve.Evaluate(phase));
            return Mathf.Clamp01(curve * BaseDensity * localMultiplier);
        }

        public bool TryGetRandomWeaponProfile(out WeaponProfile profile)
        {
            profile = WeaponProfiles.Count > 0 ? WeaponProfiles[Random.Range(0, WeaponProfiles.Count)] : null;
            return profile != null;
        }

        public float EvaluateBaseCooldownSec(float density) =>
            (BaseCooldownAtMaxDensitySec - BaseCooldownAtZeroDensitySec) * Mathf.Clamp01(density) + BaseCooldownAtZeroDensitySec;

        public float EvaluateBackoffCooldownSec(float density) =>
            (BackoffAtMaxDensitySec - BackoffAtZeroDensitySec) * Mathf.Clamp01(density) + BackoffAtZeroDensitySec;
    }

    internal sealed class WeaponProfile
    {
        public FirearmAudio Audio;
        public Vector2 BurstSizeRange;
        public Vector2 VolumeJitter;
        public Vector2 PitchJitter;
        public Vector2 SingleShotStartJitterSec;
        public Vector2 SingleShotGapRangeSec;
        public Vector2 BurstSequenceGapRangeSec;
        public float AutoFireBias;

        public int SampleBurstSize()
        {
            int lo = Mathf.Max(1, Mathf.RoundToInt(BurstSizeRange.x));
            int hi = Mathf.Max(lo, Mathf.RoundToInt(BurstSizeRange.y));
            return Random.Range(lo, hi + 1);
        }

        public float SampleVolumeMultiplier() => Random.Range(VolumeJitter.x, VolumeJitter.y);

        public float SamplePitchMultiplier() => Random.Range(PitchJitter.x, PitchJitter.y);

        public float SampleSingleShotStartJitter() => Random.Range(SingleShotStartJitterSec.x, SingleShotStartJitterSec.y);

        public float SampleSingleShotGapSec() => ClampedRange(SingleShotGapRangeSec);

        public float SampleBurstSequenceGapSec() => ClampedRange(BurstSequenceGapRangeSec);

        public static float ClampedRange(Vector2 range)
        {
            float lo = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
            float hi = Mathf.Max(lo, Mathf.Max(range.x, range.y));
            return Random.Range(lo, hi);
        }
    }

    internal sealed class FirearmAudio
    {
        public SoundBank[] SingleShots;
        public LoopTailSet[] LoopTailSets;
        public SoundBank[] GrenadeBanks;
    }

    internal sealed class LoopTailSet
    {
        public SoundBank Loop;
        public SoundBank Tail;
        public float Rpm;

        public float ComputeBeatDurationSec(float baseLoopDurationSec)
        {
            if (Rpm > 0f)
            {
                return 1f / Mathf.Max(1f, Rpm / 60f);
            }

            return baseLoopDurationSec > 0.01f ? baseLoopDurationSec * 0.0625f : 0f;
        }
    }

    internal sealed class PriorityCalculator
    {
        private readonly int[] _table;

        public PriorityCalculator(int low, int medium, int high)
        {
            _table = new[] { low, medium, high };
        }

        public int CalculatePriority(float distance, float maxDistance)
        {
            float t = Mathf.Clamp01(maxDistance > 0f ? distance / maxDistance : 1f);
            int i = Mathf.Clamp(Mathf.RoundToInt((_table.Length - 1) * (1f - t)), 0, _table.Length - 1);
            return _table[i];
        }
    }

    internal struct Context
    {
        public Vector3 Position;
        public WeaponProfile Profile;
        public AreaController EndSync;
        public float DistanceToListener;
        public double StartOffsetSec;
    }

    internal sealed class AreaController
    {
        private readonly AmbientFirefight _director;
        private readonly List<double> _activeSequenceEndTimes = new List<double>();
        private float _cooldown;
        private bool _disposed;
        private double _nextAllowedResponseTime;
        private double _busyUntil;
        private Vector3 _forwardXZ;
        private Vector3 _dirStart;
        private Vector3 _dirEnd;

        public AreaController(AmbientFirefight director, Area area)
        {
            _director = director;
            Area = area;
            _cooldown = Random.Range(area.AudioPreset.InitialCooldownRangeSec.x, area.AudioPreset.InitialCooldownRangeSec.y);
            InitializeSectorCache();
            Gunfire += OnOtherAreaGunfire;
        }

        public Area Area { get; }

        public int MaxActiveSequences { get; private set; }

        public AmbientFirefight Director => _director;

        public void SetMaxActiveSequences(int slots) => MaxActiveSequences = Math.Max(0, slots);

        public void Dispose()
        {
            _disposed = true;
            Gunfire -= OnOtherAreaGunfire;
        }

        public void Tick(float dt)
        {
            if (_disposed || !CameraManager.Exist)
            {
                return;
            }

            CameraManager cam = CameraManager.Instance;
            double now = AudioSettings.dspTime;
            Preset preset = Area.AudioPreset;
            float density = preset.EvaluateDensity(now, Area.LocalDensity);
            if (0.001f >= density)
            {
                return;
            }

            for (int i = _activeSequenceEndTimes.Count - 1; i >= 0; i--)
            {
                if (now >= _activeSequenceEndTimes[i])
                {
                    _activeSequenceEndTimes.RemoveAt(i);
                }
            }

            TryScheduleGrenade(dt, density, cam);
            if (_busyUntil > AudioSettings.dspTime)
            {
                return;
            }

            _cooldown -= dt;
            if (_cooldown > 0f)
            {
                return;
            }

            if (!preset.TryGetRandomWeaponProfile(out WeaponProfile profile))
            {
                _cooldown = preset.NoProfileRetryCooldownSec;
                return;
            }

            int shots = profile.SampleBurstSize();
            Vector3 position = GetRandomPositionInAreaSector();
            if (MaxActiveSequences <= 0 || _activeSequenceEndTimes.Count >= MaxActiveSequences)
            {
                _cooldown = preset.EvaluateBackoffCooldownSec(density);
                return;
            }

            var context = new Context { Position = position, Profile = profile, EndSync = this, DistanceToListener = cam.Distance(position), StartOffsetSec = 0 };
            Strategies.Resolve(context, shots).PlaySequence(context, shots);
            float baseCooldown = preset.EvaluateBaseCooldownSec(density);
            float gap = shots == 1 ? profile.SampleSingleShotGapSec() : profile.SampleBurstSequenceGapSec();
            _cooldown = gap + baseCooldown + Random.Range(preset.CooldownJitterRangeSec.x, preset.CooldownJitterRangeSec.y);
            if (preset.EnableCallAndResponse)
            {
                RaiseGunfire(this, position, now, shots);
            }
        }

        private void TryScheduleGrenade(float dt, float density, CameraManager cam)
        {
            Preset preset = Area.AudioPreset;
            float rate = density * preset.GrenadeEventsPerShooterPerMinuteAtMaxDensity;
            if (0f >= rate || Random.value >= rate * dt / 60f)
            {
                return;
            }

            if (!preset.TryGetRandomWeaponProfile(out WeaponProfile profile) || profile.Audio == null || profile.Audio.GrenadeBanks.Length <= 0)
            {
                return;
            }

            if (MaxActiveSequences > 0 && _activeSequenceEndTimes.Count >= MaxActiveSequences)
            {
                return;
            }

            Vector3 position = GetRandomPositionInAreaSector();
            var context = new Context { Position = position, Profile = profile, EndSync = this, DistanceToListener = cam.Distance(position) };
            if (Strategies.Grenade.CanPlay(context, 1))
            {
                Strategies.Grenade.PlaySequence(context, 1);
            }
        }

        private void OnOtherAreaGunfire(AreaController from, Vector3 position, double time, int shots)
        {
            if (_disposed || ReferenceEquals(from, this))
            {
                return;
            }

            Preset preset = Area.AudioPreset;
            if (Random.value > preset.ResponseProbability)
            {
                return;
            }

            double now = AudioSettings.dspTime;
            if (_nextAllowedResponseTime > now || _busyUntil > now)
            {
                return;
            }

            if (MaxActiveSequences > 0 && _activeSequenceEndTimes.Count >= MaxActiveSequences)
            {
                return;
            }

            int responseShots = Random.Range((int)preset.ResponseShotsRange.x, (int)preset.ResponseShotsRange.y);
            Vector3 at = GetRandomPositionInAreaSector();
            if (!preset.TryGetRandomWeaponProfile(out WeaponProfile profile) || !CameraManager.Exist)
            {
                return;
            }

            var context = new Context
            {
                Position = at, Profile = profile, EndSync = this,
                StartOffsetSec = Random.Range(preset.ResponseDelayRange.x, preset.ResponseDelayRange.y),
                DistanceToListener = CameraManager.Instance.Distance(at)
            };
            Strategies.Resolve(context, responseShots).PlaySequence(context, responseShots);
            float cooldown = Mathf.Max(0.1f, Random.Range(preset.ResponseCooldownRange.x, preset.ResponseCooldownRange.y));
            _nextAllowedResponseTime = now + cooldown;
            _cooldown = Mathf.Max(_cooldown, cooldown);
        }

        public void ReportSequenceEnd(double end)
        {
            _activeSequenceEndTimes.Add(end);
            if (end > _busyUntil)
            {
                _busyUntil = end;
            }
        }

        public void ReportSequenceBusyUntil(double end)
        {
            if (end > _busyUntil)
            {
                _busyUntil = end;
            }
        }

        private void InitializeSectorCache()
        {
            Vector3 forward = Area.Rotation * Vector3.forward;
            forward.y = 0f;
            forward = Mathf.Approximately(forward.sqrMagnitude, 0f) ? Vector3.forward : forward.normalized;
            _forwardXZ = forward;
            float half = Mathf.Clamp(Area.SectorApertureDeg, 0f, 360f) * 0.5f;
            _dirStart = Quaternion.AngleAxis(-half, Vector3.up) * forward;
            _dirEnd = Quaternion.AngleAxis(half, Vector3.up) * forward;
        }

        private Vector3 GetRandomPositionInAreaSector()
        {
            Vector3 center = Area.Position;
            if (!Area.UseSector)
            {
                Vector2 c = Random.insideUnitCircle * Area.Radius;
                return center + new Vector3(c.x, 0f, c.y);
            }

            float offset = Mathf.Clamp01(Random.Range(Area.ForwardOffsetPercentRange.x, Area.ForwardOffsetPercentRange.y)) * Area.Radius;
            Vector3 origin = center + _forwardXZ * offset;
            float t = Mathf.Clamp01(Random.value);
            Vector3 dir = new Vector3(Mathf.Lerp(_dirStart.x, _dirEnd.x, t), 0f, Mathf.Lerp(_dirStart.z, _dirEnd.z, t));
            dir = dir.magnitude > 1e-5f ? dir / dir.magnitude : Vector3.zero;
            return origin + dir * (Random.value * Area.Radius);
        }
    }

    internal interface IStrategy
    {
        bool CanPlay(in Context context, int shots);

        void PlaySequence(in Context context, int shots);
    }

    internal static class Strategies
    {
        public static readonly IStrategy Single = new SingleShotStrategy();
        public static readonly IStrategy LoopTail = new LoopTailStrategy();
        public static readonly IStrategy Grenade = new GrenadeStrategy();
        private static readonly IStrategy Noop = new NoOpStrategy();

        public static IStrategy Resolve(in Context context, int shots)
        {
            if (shots <= 1)
            {
                return Single.CanPlay(context, shots) ? Single : Noop;
            }

            bool loop = LoopTail.CanPlay(context, shots);
            bool single = Single.CanPlay(context, shots);
            if (loop && single)
            {
                return Mathf.Clamp01(context.Profile.AutoFireBias) > Random.value ? LoopTail : Single;
            }

            return loop ? LoopTail : single ? Single : Noop;
        }
    }

    private sealed class NoOpStrategy : IStrategy
    {
        public bool CanPlay(in Context context, int shots) => true;

        public void PlaySequence(in Context context, int shots)
        {
        }
    }

    private static BetterSource Source(BetterAudio audio, SoundBank bank, Vector3 position, float distance)
    {
        BetterSource source = audio.GetSource(bank.SourceType, true, true);
        if (source == null)
        {
            return null;
        }

        source.ResetFilters();
        source.ResetOcclusion();
        source.transform.position = position;
        if (_instance != null && _instance.Group != null)
        {
            source.SetMixerGroup(_instance.Group);
        }

        source.SetRolloff(bank.Rolloff);
        source.SetPriority(_instance != null ? _instance.Priority.CalculatePriority(distance, bank.Rolloff) : 128);
        return source;
    }

    private sealed class SingleShotStrategy : IStrategy
    {
        public bool CanPlay(in Context context, int shots) => context.Profile?.Audio?.SingleShots?.Length > 0;

        public void PlaySequence(in Context context, int shots)
        {
            SoundBank[] banks = context.Profile.Audio.SingleShots;
            SoundBank bank = banks[Random.Range(0, banks.Length)];
            float volume = context.Profile.SampleVolumeMultiplier();
            if (_instance != null)
            {
                _instance.StartCoroutine(Routine(context, bank, shots, volume));
            }
        }

        private static IEnumerator Routine(Context context, SoundBank bank, int shots, float volume)
        {
            float delay = Mathf.Max(0f, (float)context.StartOffsetSec + (context.Profile != null ? context.Profile.SampleSingleShotStartJitter() : 0f));
            yield return new WaitForSecondsRealtime(delay);
            int remaining = Math.Max(1, shots);
            while (remaining-- > 0)
            {
                if (!Singleton<BetterAudio>.Instantiated)
                {
                    yield break;
                }

                BetterAudio audio = Singleton<BetterAudio>.Instance;
                BetterSource source = Source(audio, bank, context.Position, context.DistanceToListener);
                if (source == null)
                {
                    yield break;
                }

                AudioClip clip1 = null, clip2 = null;
                float balance = 1f;
                float length = bank.PickClips(context.DistanceToListener, ref clip1, ref clip2, ref balance);
                source.SetPitch(context.Profile.SamplePitchMultiplier());
                source.Play(clip1, clip2, balance, volume * VolumeFactor);
                double end = AudioSettings.dspTime + Mathf.Max(length, 1f);
                SourceTuning.Apply(source, SourceTuning.LogCurve, 180f, end);
                audio.AddToAudioSourceQueue(source, end);
                context.EndSync?.ReportSequenceBusyUntil(end);
                if (remaining == 0)
                {
                    context.EndSync?.ReportSequenceEnd(end);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(context.Profile.SampleSingleShotGapSec());
            }
        }
    }

    private sealed class LoopTailStrategy : IStrategy
    {
        public bool CanPlay(in Context context, int shots) => shots > 1 && context.Profile?.Audio?.LoopTailSets?.Length > 0;

        public void PlaySequence(in Context context, int shots)
        {
            if (!Singleton<BetterAudio>.Instantiated)
            {
                return;
            }

            BetterAudio audio = Singleton<BetterAudio>.Instance;
            LoopTailSet[] sets = context.Profile.Audio.LoopTailSets;
            LoopTailSet set = sets[Random.Range(0, sets.Length)];
            AudioClip c1 = null, c2 = null;
            float balance = 1f;
            float loopLength = set.Loop.PickClips(context.DistanceToListener, ref c1, ref c2, ref balance);
            float beat = set.ComputeBeatDurationSec(loopLength);
            if (beat <= 0f)
            {
                return;
            }

            float duration = Mathf.Max(beat, shots * beat);
            BetterSource loop = Source(audio, set.Loop, context.Position, context.DistanceToListener);
            if (loop == null)
            {
                return;
            }

            double start = AudioSettings.dspTime + Mathf.Max(0f, (float)context.StartOffsetSec);
            double loopEnd = start + duration;
            Schedule(loop, balance >= 0.5f ? c1 ?? c2 : c2 ?? c1, true, start);
            loop.SetScheduledEndTime(loopEnd);
            audio.AddToAudioSourceQueue(loop, start + Mathf.Max(duration, 1f));
            SourceTuning.Apply(loop, SourceTuning.LogCurve, 180f, start + Mathf.Max(duration, 1f));

            AudioClip t1 = null, t2 = null;
            float tailBalance = 1f;
            float tailLength = set.Tail.PickClips(context.DistanceToListener, ref t1, ref t2, ref tailBalance);
            BetterSource tail = Source(audio, set.Tail, context.Position, context.DistanceToListener);
            if (tail != null)
            {
                Schedule(tail, tailBalance >= 0.5f ? t1 ?? t2 : t2 ?? t1, false, loopEnd);
                audio.AddToAudioSourceQueue(tail, loopEnd + Mathf.Max(1f, tailLength));
                SourceTuning.Apply(tail, SourceTuning.LogCurve, 180f, loopEnd + Mathf.Max(1f, tailLength));
            }

            context.EndSync?.ReportSequenceBusyUntil(loopEnd);
            context.EndSync?.ReportSequenceEnd(loopEnd);
        }

        private static void Schedule(BetterSource source, AudioClip clip, bool loop, double time)
        {
            if (clip == null)
            {
                return;
            }

            source.SetClip(0, clip);
            source.Loop = loop;
            source.SetBaseVolume(VolumeFactor);
            source.PlayScheduled(time);
        }
    }

    private sealed class GrenadeStrategy : IStrategy
    {
        public bool CanPlay(in Context context, int shots) => context.Profile?.Audio?.GrenadeBanks?.Length > 0;

        public void PlaySequence(in Context context, int shots)
        {
            if (!Singleton<BetterAudio>.Instantiated)
            {
                return;
            }

            BetterAudio audio = Singleton<BetterAudio>.Instance;
            SoundBank[] banks = context.Profile.Audio.GrenadeBanks;
            SoundBank bank = banks[Random.Range(0, banks.Length)];
            BetterSource source = Source(audio, bank, context.Position, context.DistanceToListener);
            if (source == null)
            {
                return;
            }

            float length = bank.PlayOn(source, EnvironmentType.Outdoor, context.DistanceToListener, VolumeFactor, false, true);
            double end = AudioSettings.dspTime + Mathf.Max(0f, (float)context.StartOffsetSec) + Mathf.Max(length, 1f);
            SourceTuning.Apply(source, SourceTuning.LogCurve, 180f, end);
            audio.AddToAudioSourceQueue(source, end);
            context.EndSync?.ReportSequenceEnd(end);
        }
    }
}
