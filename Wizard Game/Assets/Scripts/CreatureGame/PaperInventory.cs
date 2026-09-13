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
