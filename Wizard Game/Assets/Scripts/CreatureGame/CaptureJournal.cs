using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The player's capture record — what the booklet reads: how many of each
    /// letter-creature have been caught. Persisted in PlayerPrefs so a young
    /// player's collection survives restarts, which is most of the reward.
    ///
    /// Static on purpose, mirroring PlayerControlScheme: the journal is global
    /// game state that UI and gameplay both watch via the Changed event, with
    /// no scene wiring to forget.
    /// </summary>
    public static class CaptureJournal
    {
        const string PrefKey = "OtherwiseLabs.CaptureJournal";

        static Dictionary<char, int> _counts;

        /// <summary>Raised with the letter whose count changed.</summary>
        public static event Action<char> Changed;

        // Statics survive play sessions when Enter Play Mode's domain reload is
        // off; reset explicitly so every run starts clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _counts = null;
            Changed = null;
        }

        public static int CountOf(char letter)
        {
            EnsureLoaded();
            return _counts.TryGetValue(char.ToUpperInvariant(letter), out int count) ? count : 0;
        }

        public static int TotalCaught
        {
            get
            {
                EnsureLoaded();
                int total = 0;
                foreach (int count in _counts.Values) total += count;
                return total;
            }
        }

        public static void RecordCapture(char letter)
        {
            EnsureLoaded();
            letter = char.ToUpperInvariant(letter);
            _counts[letter] = CountOf(letter) + 1;
            Save();
            Changed?.Invoke(letter);
        }

        // Stored as "W:3|S:1" — trivially readable in the editor and safe to
        // hand-edit while balancing, unlike a binary blob.
        static void EnsureLoaded()
        {
            if (_counts != null) return;
            _counts = new Dictionary<char, int>();

            string stored = PlayerPrefs.GetString(PrefKey, "");
            foreach (string entry in stored.Split('|'))
            {
                string[] parts = entry.Split(':');
                if (parts.Length == 2 && parts[0].Length == 1 && int.TryParse(parts[1], out int count))
                    _counts[char.ToUpperInvariant(parts[0][0])] = count;
            }
        }

        static void Save()
        {
            var text = new StringBuilder();
            foreach (KeyValuePair<char, int> pair in _counts)
            {
                if (text.Length > 0) text.Append('|');
                text.Append(pair.Key).Append(':').Append(pair.Value);
            }
            PlayerPrefs.SetString(PrefKey, text.ToString());
            PlayerPrefs.Save();
        }
    }
}
