using System;
using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The satchel of letter-potion vials, ground at the gem table's mortar
    /// (the constellation mini-game) and spent as ingredients when brewing
    /// trap words at the cauldron. Letter -> count, persisted like the paper
    /// inventory and the capture journal.
    ///
    /// Economy chain: gem constellations make VIALS (letters) -> the cauldron
    /// brews vials into PAPERS (words) -> papers are spent as traps on hunts.
    /// </summary>
    public static class VialInventory
    {
        const string PrefsKey = "OtherwiseLabs.CreatureGame.VialInventory";

        /// <summary>Raised whenever a vial is added or spent.</summary>
        public static event Action Changed;

        static Dictionary<char, int> _counts;

        public static int Count(char letter)
        {
            Load();
            return _counts.TryGetValue(char.ToUpperInvariant(letter), out int count) ? count : 0;
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

        public static IReadOnlyDictionary<char, int> All
        {
            get { Load(); return _counts; }
        }

        public static void Add(char letter, int amount = 1)
        {
            Load();
            letter = char.ToUpperInvariant(letter);
            if (letter < 'A' || letter > 'Z' || amount <= 0) return;
            _counts.TryGetValue(letter, out int count);
            _counts[letter] = count + amount;
            Save();
            Changed?.Invoke();
        }

        /// <summary>Spends one vial of this letter. False if none left.</summary>
        public static bool TryConsume(char letter)
        {
            Load();
            letter = char.ToUpperInvariant(letter);
            if (!_counts.TryGetValue(letter, out int count) || count <= 0) return false;
            if (count == 1) _counts.Remove(letter);
            else _counts[letter] = count - 1;
            Save();
            Changed?.Invoke();
            return true;
        }

        static void Load()
        {
            if (_counts != null) return;
            _counts = new Dictionary<char, int>();
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            foreach (string entry in raw.Split('|'))
            {
                int colon = entry.LastIndexOf(':');
                if (colon != 1) continue;   // entries look like "S:3"
                char letter = char.ToUpperInvariant(entry[0]);
                if (letter >= 'A' && letter <= 'Z'
                    && int.TryParse(entry.Substring(colon + 1), out int count) && count > 0)
                    _counts[letter] = count;
            }
        }

        static void Save()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<char, int> pair in _counts)
                parts.Add($"{pair.Key}:{pair.Value}");
            PlayerPrefs.SetString(PrefsKey, string.Join("|", parts));
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => _counts = null;
    }
}
