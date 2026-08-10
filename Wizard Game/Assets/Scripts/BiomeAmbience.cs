using UnityEngine;
using UnityEngine.Serialization;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Atmosphere per biome: crossfades each biome's ambient loop and tints the
    /// scene fog as the listener crosses regions. A lot of mood for very little
    /// code, hanging entirely off a terrain's public DominantBiomeAt.
    ///
    /// Works with ANY biome source — the infinite streamer and the finite
    /// procedural generator both implement IBiomeSource — so this one component
    /// serves both worlds without tying them to each other.
    ///
    /// Drop on any GameObject (the terrain itself is fine). Creates its own two
    /// AudioSources for the crossfade — nothing to author.
    /// </summary>
    [AddComponentMenu("Otherwise Labs/Biome Ambience")]
    public class BiomeAmbience : MonoBehaviour
    {
        [Tooltip("Terrain whose biomes drive the ambience (streamer or procedural generator). Auto-found in the scene when empty.")]
        [FormerlySerializedAs("streamer")] public MonoBehaviour biomeSource;

        [Tooltip("Whose position decides the biome — usually the player. Falls back to the main camera.")]
        public Transform listener;

        [Tooltip("Seconds between biome checks. Cheap either way; there is no need to poll every frame.")]
        [Min(0.05f)] public float pollInterval = 0.4f;

        [Tooltip("Seconds for the audio crossfade and fog tint to complete after crossing a border.")]
        [Min(0.1f)] public float fadeTime = 2.5f;

        IBiomeSource _source;
        AudioSource _fadingIn;
        AudioSource _fadingOut;
        BiomeDefinition _current;
        float _nextPoll;
        float _targetVolume;
        Color _defaultFogColor;
        bool _fogCaptured;

        void Awake()
        {
            _source = biomeSource as IBiomeSource;
            if (biomeSource != null && _source == null)
                Debug.LogWarning($"[{name}] '{biomeSource.name}' is not a biome source; looking for one in the scene instead.", this);

            if (_source == null)
            {
                // FindObjectOfType cannot search by interface, so scan components.
                // Awake-only, and scenes have a handful of MonoBehaviours' roots
                // to walk — not a per-frame cost.
                foreach (MonoBehaviour candidate in FindObjectsOfType<MonoBehaviour>())
                {
                    if (candidate is IBiomeSource found)
                    {
                        _source = found;
                        break;
                    }
                }
            }

            _fadingIn = CreateSource("Ambience A");
            _fadingOut = CreateSource("Ambience B");
        }

        AudioSource CreateSource(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f; // ambience is a bed, not a point source
            source.volume = 0f;
            return source;
        }

        void OnEnable()
        {
            _defaultFogColor = RenderSettings.fogColor;
            _fogCaptured = true;
        }

        void OnDisable()
        {
            if (_fogCaptured) RenderSettings.fogColor = _defaultFogColor;
        }

        void Update()
        {
            if (_source == null) return;

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + pollInterval;
                Poll();
            }

            // Per-frame smoothing toward targets; the poll only changes targets.
            float step = Time.deltaTime / Mathf.Max(0.1f, fadeTime);
            _fadingIn.volume = Mathf.MoveTowards(_fadingIn.volume, _targetVolume, step);
            _fadingOut.volume = Mathf.MoveTowards(_fadingOut.volume, 0f, step);
            if (_fadingOut.volume <= 0f && _fadingOut.isPlaying) _fadingOut.Stop();

            Color targetFog = _current != null && _current.overrideFogColor ? _current.fogColor : _defaultFogColor;
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, targetFog, step);
        }

        void Poll()
        {
            Transform anchor = listener != null ? listener
                : Camera.main != null ? Camera.main.transform : null;
            if (anchor == null) return;

            BiomeDefinition biome = _source.DominantBiomeAt(anchor.position);
            if (biome == _current) return;
            _current = biome;

            AudioClip clip = biome != null ? biome.ambientLoop : null;
            _targetVolume = biome != null ? biome.ambientVolume : 0f;

            if (_fadingIn.clip == clip) return;

            // Swap roles: the playing bed fades out while the new one fades in.
            (_fadingIn, _fadingOut) = (_fadingOut, _fadingIn);
            _fadingIn.clip = clip;
            _fadingIn.volume = 0f;
            if (clip != null) _fadingIn.Play();
        }
    }
}
