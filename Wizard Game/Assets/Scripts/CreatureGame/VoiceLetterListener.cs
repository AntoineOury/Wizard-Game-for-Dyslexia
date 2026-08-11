using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// Hears the player say a letter out loud and reports which one — the voice
    /// behind "call the creature's name". Listens for each active letter's NAME
    /// ("double u", "ess") and, where a speech engine can plausibly match them,
    /// its phonic SOUND ("wuh", "sss"), so both ways a child says a letter count.
    ///
    /// Backend: Unity's built-in Windows speech keyword recognizer — offline,
    /// no keys, works in the editor where the game is play-tested. On other
    /// platforms IsSupported is false and callers fall back to tap-the-letter,
    /// so the game never depends on a microphone being available.
    ///
    /// Swapping in a cloud engine (e.g. Google Cloud Speech-to-Text, which this
    /// project has used before) means replacing the platform block below: start
    /// streaming in StartListening, and on a transcript call ReportPhrase() —
    /// everything above that line stays as it is.
    /// </summary>
    public class VoiceLetterListener : MonoBehaviour
    {
        /// <summary>Raised on the main thread with the letter that was heard.</summary>
        public event Action<char> LetterHeard;

        public bool IsListening { get; private set; }

        readonly Dictionary<string, char> _phraseToLetter = new Dictionary<string, char>();

        // How each letter may be spoken. Names first (how letters are usually
        // said aloud); then phonic sounds, but only ones a word recognizer has
        // a real chance at, and only where the sound is unambiguous — C and K
        // share "kuh", so neither claims it and their names carry them instead.
        static readonly Dictionary<char, string[]> SpokenForms = new Dictionary<char, string[]>
        {
            ['A'] = new[] { "a", "ay" },
            ['B'] = new[] { "b", "bee", "buh" },
            ['C'] = new[] { "c", "see" },
            ['D'] = new[] { "d", "dee", "duh" },
            ['E'] = new[] { "e", "ee" },
            ['F'] = new[] { "f", "eff", "fff" },
            ['G'] = new[] { "g", "gee", "guh" },
            ['H'] = new[] { "h", "aitch", "huh" },
            ['I'] = new[] { "i", "eye" },
            ['J'] = new[] { "j", "jay", "juh" },
            ['K'] = new[] { "k", "kay" },
            ['L'] = new[] { "l", "ell", "lll" },
            ['M'] = new[] { "m", "em", "mmm" },
            ['N'] = new[] { "n", "en", "nnn" },
            ['O'] = new[] { "o", "oh" },
            ['P'] = new[] { "p", "pee", "puh" },
            ['Q'] = new[] { "q", "cue" },
            ['R'] = new[] { "r", "are", "rrr" },
            ['S'] = new[] { "s", "ess", "sss" },
            ['T'] = new[] { "t", "tee", "tuh" },
            ['U'] = new[] { "u", "you" },
            ['V'] = new[] { "v", "vee", "vvv" },
            ['W'] = new[] { "w", "double u", "double you", "wuh" },
            ['X'] = new[] { "x", "ex" },
            ['Y'] = new[] { "y", "why", "yuh" },
            ['Z'] = new[] { "z", "zee", "zed", "zzz" },
        };

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        KeywordRecognizer _recognizer;

        public bool IsSupported
        {
            get
            {
                try { return PhraseRecognitionSystem.isSupported; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Begin listening for the given letters. Safe to call again with a new set.</summary>
        public void StartListening(IEnumerable<char> letters)
        {
            StopListening();
            BuildPhraseTable(letters);
            if (!IsSupported || _phraseToLetter.Count == 0) return;

            try
            {
                var phrases = new string[_phraseToLetter.Count];
                _phraseToLetter.Keys.CopyTo(phrases, 0);

                // Low confidence on purpose: young voices, and a wrong letter
                // only calls the wrong creature — a shrug, not a failure.
                _recognizer = new KeywordRecognizer(phrases, ConfidenceLevel.Low);
                _recognizer.OnPhraseRecognized += OnPhraseRecognized;
                _recognizer.Start();
                IsListening = true;
            }
            catch (Exception exception)
            {
                // Typically a missing speech language pack; the letter buttons
                // still work, so log and carry on rather than break the flow.
                Debug.LogWarning($"[VoiceLetterListener] Speech recognition unavailable: {exception.Message}");
                StopListening();
            }
        }

        public void StopListening()
        {
            IsListening = false;
            if (_recognizer == null) return;
            _recognizer.OnPhraseRecognized -= OnPhraseRecognized;
            if (_recognizer.IsRunning) _recognizer.Stop();
            _recognizer.Dispose();
            _recognizer = null;
        }

        void OnPhraseRecognized(PhraseRecognizedEventArgs args)
        {
            ReportPhrase(args.text);
        }

        void OnDestroy() => StopListening();
#else
        public bool IsSupported => false;

        public void StartListening(IEnumerable<char> letters)
        {
            BuildPhraseTable(letters);
        }

        public void StopListening()
        {
            IsListening = false;
        }
#endif

        /// <summary>
        /// Feed a recognized phrase or transcript in from ANY engine — this is
        /// the single entry point a cloud backend needs to call.
        /// </summary>
        public void ReportPhrase(string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return;
            if (_phraseToLetter.TryGetValue(phrase.Trim().ToLowerInvariant(), out char letter))
                LetterHeard?.Invoke(letter);
        }

        void BuildPhraseTable(IEnumerable<char> letters)
        {
            _phraseToLetter.Clear();
            foreach (char raw in letters)
            {
                char letter = char.ToUpperInvariant(raw);
                if (!SpokenForms.TryGetValue(letter, out string[] forms)) continue;
                foreach (string form in forms)
                    if (!_phraseToLetter.ContainsKey(form))
                        _phraseToLetter[form] = letter;
            }
        }
    }
}
