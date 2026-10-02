using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Фоновый звук сцены: музыка или атмосфера (город, лес, ветер на трассе).
    /// Громкость берётся из настроек (музыка/эффекты). Вешается на любой объект сцены.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AmbientAudioZone : MonoBehaviour
    {
        public AudioClip clip;
        [Tooltip("Это музыка (иначе — звуки окружения).")]
        public bool isMusic;
        [Range(0f, 1f)] public float volume = 0.5f;
        public float fadeInSeconds = 2f;

        private AudioSource source;
        private float fade;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            if (clip != null) source.Play();
        }

        private void Update()
        {
            if (source.clip == null) return;
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeInSeconds));
            var gm = GameManager.Instance;
            float setting = 1f;
            if (gm != null) setting = isMusic ? gm.Progress.Settings.musicVolume : gm.Progress.Settings.sfxVolume;
            source.volume = volume * setting * fade;
        }
    }
}
