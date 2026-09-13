using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
    /// Backends, both keyless: in WebGL builds, the BROWSER's built-in speech
    /// recognition (the Web Speech API, via Plugins/WebGL/WebSpeech.jslib) —
    /// Chrome/Edge/Android use Google's recognizer, Safari on Mac/iPhone/iPad
    /// uses Siri's, so browser play hears voice on laptops, tablets and phones
    /// alike (Firefox has no such API and falls back). Elsewhere, Unity's
    /// built-in Windows speech keyword recognizer — offline, works in the
    /// editor where the game is play-tested. On anything else IsSupported is
    /// false and callers fall back to tap-the-letter, so the game never
    /// depends on a microphone being available.
    ///
    /// Swapping in a cloud engine (e.g. Google Cloud Speech-to-Text, which this
    /// project has used before) means replacing a platform block below: start
    /// streaming in StartListening, and on a transcript call ReportPhrase() —
    /// everything above that line stays as it is.
    /// </summary>
    public class VoiceLetterListener : MonoBehaviour
    {
        /// <summary>Raised on the main thread with the letter that was heard.</summary>
        public event Action<char> LetterHeard;

        public bool IsListening { get; private set; }

        /// <summary>
        /// Human-readable state of the voice pipeline, shown on the Call screen
        /// and written to the Console — the debugging window into why speech
        /// is or isn't working on a given machine.
        /// </summary>
        public string StatusReport { get; private set; } = "Voice not started yet.";

        /// <summary>What to say, e.g. "W (\"double u\" / \"wuh\")  S (\"ess\" / \"sss\")".</summary>
        public string ListeningSummary { get; private set; } = "";

        [Tooltip("BCP-47 language the browser recognizer listens in (WebGL builds only; " +
                 "Windows uses the system speech language).")]
        public string language = "en-US";

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

        /// <summary>
        /// How a letter may be spoken (name forms first, phonic sounds after) —
        /// shared with the other mini-games so hearing and saying stay
        /// consistent: the brewing book SPEAKS from the same table this
        /// listener RECOGNIZES from.
        /// </summary>
        public static IReadOnlyList<string> SpokenFormsFor(char letter)
        {
            return SpokenForms.TryGetValue(char.ToUpperInvariant(letter), out string[] forms)
                ? forms
                : Array.Empty<string>();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Browser backend: the Web Speech API, bridged by WebSpeech.jslib.
        // Unity polls the bridge's message queue every frame — no SendMessage,
        // so this component keeps working on any GameObject under any name.
        [DllImport("__Internal")] static extern int OtherwiseSpeech_IsSupported();
        [DllImport("__Internal")] static extern void OtherwiseSpeech_Start(string language);
        [DllImport("__Internal")] static extern void OtherwiseSpeech_Stop();
        [DllImport("__Internal")] static extern string OtherwiseSpeech_Poll();

        string _lastHeardPayload;
        float _lastHeardAt;

        public bool IsSupported
        {
            get
            {
                try { return OtherwiseSpeech_IsSupported() == 1; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Begin listening for the given letters. Safe to call again with a new set.</summary>
        public void StartListening(IEnumerable<char> letters)
        {
            StopListening();
            BuildPhraseTable(letters);

            if (!IsSupported)
            {
                SetStatus("This browser has no built-in speech recognition (Firefox is the " +
                          "usual one) — tap the letter buttons instead, or use Chrome/Safari.");
                return;
            }
            if (_phraseToLetter.Count == 0)
            {
                SetStatus("No letters to listen for.");
                return;
            }

            // The browser starts the recognizer asynchronously (it may need to
            // ask for microphone permission first), so engage optimistically;
            // the polled status messages correct the picture within a frame
            // or two, on the same live status line the Call screen shows.
            IsListening = true;
            SetStatus("Waking the microphone... (the browser may ask permission — say Allow!)");
            OtherwiseSpeech_Start(language);
        }

        public void StopListening()
        {
            IsListening = false;
            OtherwiseSpeech_Stop();
        }

        void Update()
        {
            if (!IsListening) return;

            string message;
            while (!string.IsNullOrEmpty(message = OtherwiseSpeech_Poll()))
            {
                if (message.StartsWith("R:", StringComparison.Ordinal))
                    HandleTranscripts(message.Substring(2));
                else if (message == "S:listening")
                    SetStatus($"Listening! {ListeningSummary}");
                else if (message.StartsWith("E:", StringComparison.Ordinal))
                    HandleBrowserError(message.Substring(2));
            }
        }

        /// <summary>One utterance, tab-separated alternatives; first letter match wins.</summary>
        void HandleTranscripts(string payload)
        {
            // Some mobile recognizers emit the same final twice in a row —
            // one shout should never call twice.
            if (payload == _lastHeardPayload && Time.unscaledTime - _lastHeardAt < 1f) return;
            _lastHeardPayload = payload;
            _lastHeardAt = Time.unscaledTime;

            string[] alternatives = payload.Split('\t');
            foreach (string alternative in alternatives)
            {
                if (!TryExtractLetter(alternative, out char letter)) continue;
                SetStatus($"Heard \"{alternative.Trim()}\"!");
                LetterHeard?.Invoke(letter);
                return;
            }

            // Heard, but matched nothing: show it, so players see the ears work.
            SetStatus($"Heard \"{alternatives[0].Trim()}\" — try the letter's name, " +
                      "like \"double u\" or \"ess\".");
        }

        void HandleBrowserError(string code)
        {
            switch (code)
            {
                case "not-allowed":
                case "service-not-allowed":
                    SetStatus("The browser blocked the microphone — allow it from the icon " +
                              "near the address bar, then tap the game once.");
                    break;
                case "audio-capture":
                    SetStatus("No microphone found — plug one in, or tap the letter buttons.");
                    break;
                case "network":
                    SetStatus("The browser's speech service needs internet — check the connection.");
                    break;
                case "no-speech":
                case "aborted":
                    break; // Benign pauses; the bridge auto-restarts around them.
                default:
                    SetStatus($"Browser speech error: {code}");
                    break;
            }
        }

        void OnDestroy() => StopListening();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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

            if (!IsSupported)
            {
                SetStatus("Windows says speech recognition is unavailable — check the Windows " +
                          "(system) Settings app: Privacy & security > Microphone, and that a " +
                          "speech language pack is installed under Time & language.");
                return;
            }
            if (_phraseToLetter.Count == 0)
            {
                SetStatus("No letters to listen for.");
                return;
            }

            try
            {
                var phrases = new string[_phraseToLetter.Count];
                _phraseToLetter.Keys.CopyTo(phrases, 0);

                // Low confidence on purpose: young voices, and a wrong letter
                // only calls the wrong creature — a shrug, not a failure.
                _recognizer = new KeywordRecognizer(phrases, ConfidenceLevel.Low);
                _recognizer.OnPhraseRecognized += OnPhraseRecognized;
                PhraseRecognitionSystem.OnError += OnSystemError;
                _recognizer.Start();
                IsListening = _recognizer.IsRunning;
                SetStatus(IsListening
                    ? $"Listening! {ListeningSummary}"
                    : "Recognizer created but did not start — see the Console.");
            }
            catch (Exception exception)
            {
                // Typically a missing speech language pack; the letter buttons
                // still work, so report loudly and carry on rather than break.
                SetStatus($"Speech failed to start: {exception.Message}");
                StopListening();
            }
        }

        public void StopListening()
        {
            IsListening = false;
            if (_recognizer == null) return;
            PhraseRecognitionSystem.OnError -= OnSystemError;
            _recognizer.OnPhraseRecognized -= OnPhraseRecognized;
            if (_recognizer.IsRunning) _recognizer.Stop();
            _recognizer.Dispose();
            _recognizer = null;
        }

        void OnPhraseRecognized(PhraseRecognizedEventArgs args)
        {
            SetStatus($"Heard \"{args.text}\" (confidence: {args.confidence})");
            ReportPhrase(args.text);
        }

        void OnSystemError(SpeechError error)
        {
            SetStatus($"Windows speech error: {error}");
        }

        void OnDestroy() => StopListening();
#else
        public bool IsSupported => false;

        public void StartListening(IEnumerable<char> letters)
        {
            BuildPhraseTable(letters);
            SetStatus("Voice here needs a Windows build or a BROWSER (WebGL) build — " +
                      "or a cloud speech backend plugged into VoiceLetterListener.");
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
            if (TryExtractLetter(phrase, out char letter))
                LetterHeard?.Invoke(letter);
        }

        /// <summary>
        /// Maps a transcript to a listened-for letter. Free-form engines (the
        /// browser, cloud STT) dress the letter up — "W.", "Double U", "the
        /// letter w" — so after an exact match this also accepts a spoken form
        /// standing as its own word, but only in a two-or-three-word utterance
        /// so background chatter never calls a creature.
        /// </summary>
        bool TryExtractLetter(string phrase, out char letter)
        {
            letter = default;
            if (string.IsNullOrEmpty(phrase)) return false;

            string normalized = Normalize(phrase);
            if (_phraseToLetter.TryGetValue(normalized, out letter)) return true;

            string[] words = normalized.Split(' ');
            if (words.Length < 2 || words.Length > 3) return false;
            foreach (string word in words)
                if (_phraseToLetter.TryGetValue(word, out letter)) return true;
            return false;
        }

        /// <summary>Lowercase letters and single spaces only: "Double U." -> "double u".</summary>
        static string Normalize(string phrase)
        {
            var builder = new StringBuilder(phrase.Length);
            foreach (char c in phrase)
            {
                if (char.IsLetter(c))
                    builder.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c) && builder.Length > 0 && builder[builder.Length - 1] != ' ')
                    builder.Append(' ');
            }
            return builder.ToString().TrimEnd();
        }

        void BuildPhraseTable(IEnumerable<char> letters)
        {
            _phraseToLetter.Clear();
            var summary = new List<string>();
            foreach (char raw in letters)
            {
                char letter = char.ToUpperInvariant(raw);
                if (!SpokenForms.TryGetValue(letter, out string[] forms)) continue;
                foreach (string form in forms)
                    if (!_phraseToLetter.ContainsKey(form))
                        _phraseToLetter[form] = letter;

                // Skip the bare character; show the sayable forms.
                summary.Add(forms.Length > 1
                    ? $"{letter} (say \"{forms[1]}\")"
                    : letter.ToString());
            }
            ListeningSummary = string.Join("   ", summary);
        }

        void SetStatus(string status)
        {
            if (status == StatusReport) return; // browser restarts repeat themselves
            StatusReport = status;
            Debug.Log($"[VoiceLetterListener] {status}");
        }
    }
}
