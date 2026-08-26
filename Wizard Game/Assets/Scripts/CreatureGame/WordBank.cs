using System;
using System.Collections.Generic;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The words behind trap baiting, and the "which word has the most of my
    /// letter?" challenge built from them.
    ///
    /// The pool is deliberately small, concrete and phonically regular — words
    /// a 6-8 year old with dyslexia has a fair shot at: short, familiar things
    /// (animals, food, weather), with some double-letter words because those
    /// make the counting genuinely interesting (is it "wow" or "cat" that has
    /// more Ws?). Every letter of the alphabet appears in at least one word,
    /// so any creature letter can always build a challenge.
    /// </summary>
    public static class WordBank
    {
        /// <summary>One trap challenge: word options, and which one wins.</summary>
        public struct Challenge
        {
            public string[] words;
            public int correctIndex;

            public string CorrectWord => words[correctIndex];
        }

        static readonly string[] Words =
        {
            "apple", "banana", "grass", "sunset", "window", "wow", "bubble",
            "rabbit", "puppy", "kitten", "hello", "yellow", "green", "tree",
            "little", "mummy", "daddy", "book", "moon", "spoon", "star",
            "water", "willow", "swim", "fish", "shell", "sand", "wave",
            "cloud", "rain", "snow", "frog", "duck", "bird", "nest",
            "egg", "jam", "jelly", "juice", "pizza", "fizz", "buzz",
            "zebra", "fox", "box", "six", "queen", "quiz", "king",
            "dragon", "magic", "wand", "wizard", "cave", "rock", "hill",
            "path", "door", "keep", "seed", "leaf", "root", "mud",
            "sun", "sky", "dog", "cat", "cow", "pig", "hen",
            "vivid", "seven", "puzzle", "happy", "summer", "winter", "kick",
        };

        /// <summary>
        /// Builds a pick-one-of-three challenge for a letter: one word strictly
        /// richer in that letter than both distractors. Distractors prefer a mix
        /// of "fewer" and "none", so the player is really counting letters, not
        /// just spotting presence.
        /// </summary>
        public static Challenge Build(char letter, Random rng)
        {
            letter = char.ToUpperInvariant(letter);

            // Bucket the pool by how many times the letter appears.
            var byCount = new Dictionary<int, List<string>>();
            int bestCount = 0;
            foreach (string word in Words)
            {
                int count = CountLetter(word, letter);
                if (!byCount.TryGetValue(count, out List<string> bucket))
                    byCount[count] = bucket = new List<string>();
                bucket.Add(word);
                if (count > bestCount) bestCount = count;
            }

            // The winner comes from the richest bucket available; every letter
            // has at least a count-1 word in the pool, so this never fails.
            string correct = Pick(byCount[bestCount], rng);

            // Two distractors with strictly fewer occurrences. One "has some but
            // fewer" distractor when possible makes it a counting exercise; a
            // zero-count word keeps at least one clearly wrong option.
            var distractors = new List<string>();
            for (int count = bestCount - 1; count > 0 && distractors.Count < 1; count--)
                if (byCount.TryGetValue(count, out List<string> bucket))
                    distractors.Add(Pick(bucket, rng));
            while (distractors.Count < 2)
            {
                for (int count = bestCount - 1; count >= 0; count--)
                {
                    if (!byCount.TryGetValue(count, out List<string> bucket)) continue;
                    string candidate = Pick(bucket, rng);
                    if (candidate != correct && !distractors.Contains(candidate))
                    {
                        distractors.Add(candidate);
                        break;
                    }
                }
            }

            // Shuffle the three into a random order.
            var words = new List<string> { correct, distractors[0], distractors[1] };
            for (int i = words.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (words[i], words[j]) = (words[j], words[i]);
            }

            return new Challenge { words = words.ToArray(), correctIndex = words.IndexOf(correct) };
        }

        public static int CountLetter(string word, char letter)
        {
            letter = char.ToUpperInvariant(letter);
            int count = 0;
            foreach (char c in word)
                if (char.ToUpperInvariant(c) == letter) count++;
            return count;
        }

        static string Pick(List<string> list, Random rng) => list[rng.Next(list.Count)];
    }
}
