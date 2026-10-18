using System;
using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The player's satchel of word-trap papers, crafted in the brewing
    /// mini-game and (in a later step) spent on hunting trips. Stored as
    /// word -> count, persisted through PlayerPrefs like the capture journal.
    ///
    /// The creature game does not consume from here yet — its paper supply is
    /// still infinite — so brewing is purely additive today. When hunts start
    /// consuming papers, TryConsume() is the single call the trap flow needs.
    /// </summary>
    public static class PaperInventory
    {
        const string PrefsKey = "OtherwiseLabs.CreatureGame.PaperInventory";

        /// <summary>Raised whenever a paper is added or spent.</summary>
        public static event Action Changed;

        static Dictionary<string, int> _counts;

        /// <summary>How many papers of this exact word are in the satchel.</summary>
        public static int Count(string word)
        {
            Load();
            return _counts.TryGetValue(Normalize(word), out int count) ? count : 0;
        }

        public static int TotalCount
        {
            get
            {
                Load();
                int total = 0;
                foreach (int count in _counts.Values) total += count;
                return total;
            }
        }

        /// <summary>Every word in the satchel with its count (uppercase words).</summary>
        public static IReadOnlyDictionary<string, int> All
        {
            get { Load(); return _counts; }
        }

        /// <summary>
        /// The letter's trap supply: how many papers contain this letter.
        /// One paper can serve any letter inside its word — flexible ammo.
        /// </summary>
        public static int CountForLetter(char letter)
        {
            Load();
            letter = char.ToUpperInvariant(letter);
            int total = 0;
            foreach (KeyValuePair<string, int> pair in _counts)
                if (pair.Key.IndexOf(letter) >= 0) total += pair.Value;
            return total;
        }

        /// <summary>The words (with counts) backing a letter's supply, for UI.</summary>
        public static List<KeyValuePair<string, int>> WordsFor(char letter)
        {
            Load();
            letter = char.ToUpperInvariant(letter);
            var words = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in _counts)
                if (pair.Key.IndexOf(letter) >= 0) words.Add(pair);
            words.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return words;
        }

        /// <summary>
        /// Spends one paper containing the letter and reports which word was
        /// spent. Prefers the exact word when the satchel has it, otherwise
        /// the paper with the FEWEST of the letter — sticky papers stay saved
        /// for hunts that need them. False (and null) when the supply is dry.
        /// </summary>
        public static bool TryConsumeForLetter(char letter, string preferredWord, out string consumedWord)
        {
            Load();
            letter = char.ToUpperInvariant(letter);
            consumedWord = null;

            if (!string.IsNullOrEmpty(preferredWord))
            {
                string exact = Normalize(preferredWord);
                if (exact.IndexOf(letter) >= 0 && _counts.TryGetValue(exact, out int have) && have > 0)
                    consumedWord = exact;
            }

            if (consumedWord == null)
            {
                int loosest = int.MaxValue;
                foreach (KeyValuePair<string, int> pair in _counts)
                {
                    int occurrences = 0;
                    foreach (char c in pair.Key)
                        if (c == letter) occurrences++;
                    if (occurrences > 0 && occurrences < loosest)
                    {
                        loosest = occurrences;
                        consumedWord = pair.Key;
                    }
                }
            }

            if (consumedWord == null) return false;
            return TryConsume(consumedWord);
        }

        public static void Add(string word, int amount = 1)
        {
            Load();
            string key = Normalize(word);
            if (string.IsNullOrEmpty(key) || amount <= 0) return;
            _counts.TryGetValue(key, out int count);
            _counts[key] = count + amount;
            Save();
            Changed?.Invoke();
        }

        /// <summary>Spends one paper of this word. False if none left.</summary>
        public static bool TryConsume(string word)
        {
            Load();
            string key = Normalize(word);
            if (!_counts.TryGetValue(key, out int count) || count <= 0) return false;
            if (count == 1) _counts.Remove(key);
            else _counts[key] = count - 1;
            Save();
            Changed?.Invoke();
            return true;
        }

        static string Normalize(string word) =>
            string.IsNullOrWhiteSpace(word) ? "" : word.Trim().ToUpperInvariant();

        static void Load()
        {
            if (_counts != null) return;
            _counts = new Dictionary<string, int>();
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            foreach (string entry in raw.Split('|'))
            {
                int colon = entry.LastIndexOf(':');
                if (colon <= 0) continue;
                string word = entry.Substring(0, colon);
                if (int.TryParse(entry.Substring(colon + 1), out int count) && count > 0)
                    _counts[word] = count;
            }
        }

        static void Save()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in _counts)
                parts.Add($"{pair.Key}:{pair.Value}");
            PlayerPrefs.SetString(PrefsKey, string.Join("|", parts));
            PlayerPrefs.Save();
        }

        // Domain reloads are disabled-able in the editor; drop the cache so
        // each play session rereads persisted state.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => _counts = null;
    }
}
