using System;
using UnityEngine;
using OtherwiseLabs.CreatureGame;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace OtherwiseLabs.BrewingGame
{
    /// <summary>
    /// The voice of the spell book: says one letter sound out loud. Three
    /// backends, best available wins per letter:
    ///
    ///  1. An authored AudioClip from <see cref="letterClips"/> (A=0 ... Z=25)
    ///     — record real phonics later and drop them in the Inspector; they
    ///     take over everywhere with no code changes.
    ///  2. In browser (WebGL) builds, the Web Speech API's built-in
    ///     text-to-speech, speaking the letter's phonic form ("wuh", "sss")
    ///     from the same table the voice-calling recognizer listens with.
    ///  3. A musical chime (each letter has its own pitch), and the caller is
    ///     told no real voice happened so it can show the letter instead.
    ///
    /// So: playable today in the editor (chimes + shown letters), speaks for
    /// real on itch.io, and upgrades to recorded phonics whenever they exist.
    /// </summary>
    public class LetterSpeaker : MonoBehaviour
    {
        [Tooltip("Optional recorded letter sounds, A=0 through Z=25. Any empty slot falls back to browser speech (WebGL) or a chime.")]
        public AudioClip[] letterClips = new AudioClip[26];

        [Tooltip("Browser text-to-speech rate (1 = normal). Slower is friendlier for young ears.")]
        [Range(0.4f, 1.5f)] public float ttsRate = 0.8f;

        [Tooltip("Browser text-to-speech pitch (1 = normal).")]
        [Range(0.5f, 1.8f)] public float ttsPitch = 1.05f;

        AudioSource _source;
        AudioClip[] _chimes;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int OtherwiseSpeech_TtsSupported();
        [DllImport("__Internal")] static extern void OtherwiseSpeech_Speak(string text, float rate, float pitch);
        [DllImport("__Internal")] static extern int OtherwiseSpeech_IsSpeaking();
        [DllImport("__Internal")] static extern void OtherwiseSpeech_StopSpeaking();

        bool TtsSupported
        {
            get { try { return OtherwiseSpeech_TtsSupported() == 1; } catch (Exception) { return false; } }
        }
        void TtsSpeak(string text) => OtherwiseSpeech_Speak(text, ttsRate, ttsPitch);
        bool TtsSpeaking
        {
            get { try { return OtherwiseSpeech_IsSpeaking() == 1; } catch (Exception) { return false; } }
        }
        void TtsStop() { try { OtherwiseSpeech_StopSpeaking(); } catch (Exception) { } }
#else
        bool TtsSupported => false;
        void TtsSpeak(string text) { }
        bool TtsSpeaking => false;
        void TtsStop() { }
#endif

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0.75f;   // mostly 3D: the book has a place in the room
            _source.maxDistance = 30f;
        }

        void OnValidate()
        {
            if (letterClips == null || letterClips.Length != 26)
                Array.Resize(ref letterClips, 26);
        }

        /// <summary>Still saying the last letter (any backend)?</summary>
        public bool IsBusy => (_source != null && _source.isPlaying) || TtsSpeaking;

        public void StopAll()
        {
            if (_source != null) _source.Stop();
            TtsStop();
        }

        /// <summary>
        /// Says the letter at this world position. Returns a duration estimate
        /// to wait; <paramref name="voiced"/> is false when only a chime played
        /// (show the letter visually so the player still gets the information).
        /// </summary>
        public float Speak(char letter, Vector3 worldPosition, out bool voiced)
        {
            letter = char.ToUpperInvariant(letter);
            transform.position = worldPosition;
            int index = letter - 'A';

            AudioClip clip = (letterClips != null && index >= 0 && index < letterClips.Length)
                ? letterClips[index] : null;
            if (clip != null)
            {
                _source.PlayOneShot(clip);
                voiced = true;
                return clip.length;
            }

            if (TtsSupported)
            {
                TtsSpeak(PhonicText(letter));
                voiced = true;
                return 0.9f; // rough; callers also poll IsBusy
            }

            _source.PlayOneShot(Chime(index));
            voiced = false;
            return 0.45f;
        }

        /// <summary>
        /// What the book says for a letter: the phonic sound where the shared
        /// table has one ("wuh"), otherwise the letter's name ("see") — same
        /// forms the voice-calling recognizer accepts, so hearing and saying
        /// stay consistent across the mini-games.
        /// </summary>
        static string PhonicText(char letter)
        {
            var forms = VoiceLetterListener.SpokenFormsFor(letter);
            return forms.Count > 0 ? forms[forms.Count - 1] : letter.ToString();
        }

        /// <summary>Per-letter chime: a soft sine pluck, pitched up the alphabet.</summary>
        AudioClip Chime(int index)
        {
            index = Mathf.Clamp(index, 0, 25);
            _chimes ??= new AudioClip[26];
            if (_chimes[index] != null) return _chimes[index];

            const int rate = 22050;
            float seconds = 0.4f;
            float frequency = 392f * Mathf.Pow(2f, index / 24f); // G4 rising ~one octave
            int samples = (int)(rate * seconds);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-4.5f * t);
                data[i] = 0.5f * envelope *
                          (Mathf.Sin(2f * Mathf.PI * frequency * t) +
                           0.35f * Mathf.Sin(4f * Mathf.PI * frequency * t));
            }

            var clip = AudioClip.Create($"Chime {(char)('A' + index)}", samples, 1, rate, false);
            clip.SetData(data, 0);
            _chimes[index] = clip;
            return clip;
        }
    }
}
