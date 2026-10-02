using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Звук машины: двигатель (три зацикленных клипа с перетеканием по оборотам или,
    /// если клипов нет, процедурный синтез), визг шин, звук покрытия, ветер, удары, переключение передач.
    /// Вешается на дочерний объект машины «EngineAudio» с AudioSource
    /// (на этом объекте не должно быть других AudioSource — их скрипт создаёт сам).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudioController : MonoBehaviour
    {
        public VehicleController vehicle;
        public DamageSystem damage;

        [Header("Клипы двигателя (необязательно)")]
        public AudioClip idleClip;
        public AudioClip lowClip;
        public AudioClip highClip;
        public float minPitch = 0.55f;
        public float maxPitch = 1.8f;
        [Range(0f, 1f)] public float engineVolume = 0.7f;

        [Header("Процедурный звук, если клипов нет")]
        public bool proceduralFallback = true;
        [Range(0f, 1f)] public float proceduralVolume = 0.22f;

        [Header("Шины, покрытие, ветер")]
        public AudioClip skidClip;
        public AudioClip windClip;
        [Range(0f, 1f)] public float skidVolume = 0.6f;
        [Range(0f, 1f)] public float surfaceVolume = 0.5f;
        [Range(0f, 1f)] public float windVolume = 0.35f;

        [Header("Удары и передачи")]
        public AudioClip[] lightImpacts;
        public AudioClip[] heavyImpacts;
        public AudioClip gearShiftClip;

        private AudioSource synthSource;
        private AudioSource idleSrc, lowSrc, highSrc, skidSrc, surfaceSrc, windSrc, oneShotSrc;
        private SurfaceData lastSurface;
        private bool useProcedural;

        // Данные для аудиопотока (OnAudioFilterRead работает не в главном потоке)
        private volatile float synthRpm01;
        private volatile float synthThrottle;
        private volatile float synthVolume;
        private int sampleRate = 48000;
        private double phaseMain, phaseSub, phaseWhine;
        private System.Random rng;

        private void Awake()
        {
            if (vehicle == null) vehicle = GetComponentInParent<VehicleController>();
            if (damage == null) damage = GetComponentInParent<DamageSystem>();
            sampleRate = AudioSettings.outputSampleRate;
            rng = new System.Random(1234);

            synthSource = GetComponent<AudioSource>();
            useProcedural = proceduralFallback && idleClip == null && lowClip == null && highClip == null;
            if (useProcedural)
            {
                synthSource.clip = AudioClip.Create("EngineSynth", sampleRate, 1, sampleRate, false);
                synthSource.loop = true;
                synthSource.spatialBlend = 0f;
                synthSource.playOnAwake = false;
                synthSource.Play();
            }
            else
            {
                synthSource.playOnAwake = false;
            }

            idleSrc = CreateSource("Idle", idleClip, true);
            lowSrc = CreateSource("Low", lowClip, true);
            highSrc = CreateSource("High", highClip, true);
            skidSrc = CreateSource("Skid", skidClip, true);
            surfaceSrc = CreateSource("Surface", null, true);
            windSrc = CreateSource("Wind", windClip, true);
            oneShotSrc = CreateSource("OneShots", null, false);
        }

        private void OnEnable()
        {
            if (damage != null) damage.Hit += OnHit;
            if (vehicle != null) vehicle.GearChanged += OnGearChanged;
        }

        private void OnDisable()
        {
            if (damage != null) damage.Hit -= OnHit;
            if (vehicle != null) vehicle.GearChanged -= OnGearChanged;
        }

        private AudioSource CreateSource(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject("Audio_" + name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0f;
            if (clip != null && loop) src.Play();
            return src;
        }

        private float SfxVolume
        {
            get
            {
                var gm = GameManager.Instance;
                return gm != null ? Mathf.Clamp01(gm.Progress.Settings.sfxVolume) : 1f;
            }
        }

        private void Update()
        {
            if (vehicle == null) return;
            float sfx = SfxVolume;
            float rpm = vehicle.Rpm01;
            float throttle = Mathf.Max(vehicle.ThrottleInput, vehicle.IsReversing ? vehicle.BrakeInput : 0f);
            float speed01 = Mathf.Clamp01(vehicle.SpeedKmh / 160f);
            float load = 0.8f + 0.2f * throttle;

            // Двигатель
            float pitch = Mathf.Lerp(minPitch, maxPitch, rpm);
            float idleVol = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, rpm));
            float lowVol = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.35f, rpm)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.85f, rpm)));
            float highVol = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.85f, rpm));
            SetLoop(idleSrc, idleVol * engineVolume * load * sfx, pitch);
            SetLoop(lowSrc, lowVol * engineVolume * load * sfx, pitch);
            SetLoop(highSrc, highVol * engineVolume * load * sfx, pitch);

            synthRpm01 = rpm;
            synthThrottle = throttle;
            synthVolume = proceduralVolume * sfx;

            // Визг шин — только на твёрдом покрытии
            SurfaceData surface = vehicle.CurrentSurface;
            bool squealSurface = surface == null || surface.tireSqueal;
            float slip = Mathf.Max(vehicle.MaxSidewaysSlip * 1.2f, vehicle.MaxForwardSlip);
            float skid = vehicle.GroundedWheelCount > 0 && vehicle.SpeedKmh > 8f && squealSurface
                ? Mathf.Clamp01((slip - 0.35f) / 0.4f) : 0f;
            SetLoop(skidSrc, skid * skidVolume * sfx, 0.9f + skid * 0.2f);

            // Звук покрытия
            if (surface != lastSurface)
            {
                lastSurface = surface;
                AudioClip clip = surface != null ? surface.rollingLoop : null;
                if (surfaceSrc.clip != clip)
                {
                    surfaceSrc.clip = clip;
                    if (clip != null) surfaceSrc.Play();
                    else surfaceSrc.Stop();
                }
            }
            float surf = vehicle.GroundedWheelCount > 0 ? Mathf.Clamp01(vehicle.SpeedKmh / 60f) : 0f;
            SetLoop(surfaceSrc, surf * surfaceVolume * sfx, 0.85f + speed01 * 0.3f);

            // Ветер
            SetLoop(windSrc, speed01 * speed01 * windVolume * sfx, 0.9f + speed01 * 0.3f);
        }

        private static void SetLoop(AudioSource src, float volume, float pitch)
        {
            if (src == null || src.clip == null) return;
            src.volume = Mathf.Lerp(src.volume, volume, 0.2f);
            src.pitch = pitch;
        }

        private void OnHit(float amount, bool heavy, Vector3 point)
        {
            AudioClip[] set = heavy ? heavyImpacts : lightImpacts;
            if (set == null || set.Length == 0) return;
            AudioClip clip = set[Random.Range(0, set.Length)];
            if (clip != null) oneShotSrc.PlayOneShot(clip, Mathf.Clamp(amount / 20f, 0.3f, 1f) * SfxVolume);
        }

        private void OnGearChanged(int gear)
        {
            if (gearShiftClip != null) oneShotSrc.PlayOneShot(gearShiftClip, 0.5f * SfxVolume);
        }

        // Процедурный синтез двигателя: основной тон + субгармоника + «свист» электромотора + шум от газа.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!useProcedural) return;
            float rpm = synthRpm01;
            float thr = synthThrottle;
            float vol = synthVolume;
            const double TwoPi = System.Math.PI * 2.0;

            double baseFreq = 28.0 + rpm * 140.0;
            double incMain = TwoPi * baseFreq / sampleRate;
            double incSub = incMain * 0.5;
            double incWhine = TwoPi * (300.0 + rpm * 1800.0) / sampleRate;

            for (int i = 0; i < data.Length; i += channels)
            {
                phaseMain += incMain;
                if (phaseMain > TwoPi) phaseMain -= TwoPi;
                phaseSub += incSub;
                if (phaseSub > TwoPi) phaseSub -= TwoPi;
                phaseWhine += incWhine;
                if (phaseWhine > TwoPi) phaseWhine -= TwoPi;

                float saw = (float)(phaseMain / TwoPi) * 2f - 1f;
                float s = 0.55f * Mathf.Sin((float)phaseMain)
                          + 0.22f * saw
                          + 0.25f * Mathf.Sin((float)phaseSub)
                          + 0.05f * Mathf.Sin((float)phaseWhine)
                          + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.06f * thr;
                s *= (0.55f + 0.45f * thr) * vol;
                s = s / (1f + Mathf.Abs(s)); // мягкое ограничение

                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
