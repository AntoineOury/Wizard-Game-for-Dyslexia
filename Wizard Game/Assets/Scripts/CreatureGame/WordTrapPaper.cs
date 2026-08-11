using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// A word written on a paper sheet, lying on the ground as a trap. Built
    /// entirely in code — a flat quad, the word in 3D text, and a trigger box.
    ///
    /// The WORD decides who the paper can hold: only creatures whose letter
    /// appears the MOST in it. "willow" (two w's, two l's) can snare Creature W
    /// or Creature L — and nobody else. Both are drawn toward it too, so which
    /// one you actually catch comes down to hunting craft: lay the paper in
    /// the right habitat and call the letter you want. Reading the word well
    /// and trapping well are the same skill.
    /// </summary>
    public class WordTrapPaper : MonoBehaviour
    {
        static readonly List<WordTrapPaper> Active = new List<WordTrapPaper>();

        readonly List<char> _eligible = new List<char>();

        /// <summary>The letter this trap was baited for (the word challenge's target).</summary>
        public char Letter { get; private set; }
        public string Word { get; private set; }
        public LetterCreature Snared { get; private set; }

        /// <summary>Letters tied for the most occurrences in the word — the only ones this paper can hold.</summary>
        public IReadOnlyList<char> EligibleLetters => _eligible;

        public bool IsEligible(char letter) => _eligible.Contains(char.ToUpperInvariant(letter));

        public static int ActiveCount => Active.Count;

        /// <summary>Oldest paper first, for enforcing a small maximum.</summary>
        public static WordTrapPaper Oldest => Active.Count > 0 ? Active[0] : null;

        /// <summary>Nearest unoccupied paper this letter could stick to, or null.</summary>
        public static WordTrapPaper FindBaitFor(char letter, Vector3 position, float radius)
        {
            WordTrapPaper best = null;
            float bestSqr = radius * radius;
            foreach (WordTrapPaper paper in Active)
            {
                if (paper.Snared != null || !paper.IsEligible(letter)) continue;
                float sqr = (paper.transform.position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = paper;
                }
            }
            return best;
        }

        public static WordTrapPaper Place(Vector3 groundPosition, char letter, string word)
        {
            var root = new GameObject($"Word Paper ({word})");
            root.transform.position = groundPosition + Vector3.up * 0.03f;

            var paper = root.AddComponent<WordTrapPaper>();
            paper.Letter = char.ToUpperInvariant(letter);
            paper.Word = word;
            paper.ComputeEligibility();
            paper.BuildVisual();

            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.8f, 1.2f, 1.3f);
            trigger.center = new Vector3(0f, 0.6f, 0f);

            Active.Add(paper);
            return paper;
        }

        void BuildVisual()
        {
            // The sheet: a quad laid flat. Its auto MeshCollider goes — the
            // trigger box on the root does all the sensing.
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet.name = "Sheet";
            Destroy(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(transform, false);
            sheet.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sheet.transform.localScale = new Vector3(1.7f, 1.2f, 1f);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader) { color = new Color(0.98f, 0.96f, 0.9f) };
                sheet.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            // The word, in big friendly letters just above the sheet.
            var textGo = new GameObject("Word");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            textGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var text = textGo.AddComponent<TextMeshPro>();
            text.text = Word;
            text.color = new Color(0.15f, 0.15f, 0.25f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 1f;
            text.fontSizeMax = 9f;
            text.rectTransform.sizeDelta = new Vector2(1.6f, 1.05f);
        }

        /// <summary>
        /// The eligibility rule: count each letter in the word; whoever is tied
        /// for the top count owns the paper. Simple enough to say to a child —
        /// "the biggest letters in the word win it".
        /// </summary>
        void ComputeEligibility()
        {
            _eligible.Clear();
            int best = 0;
            foreach (char raw in Word)
            {
                if (!char.IsLetter(raw)) continue;
                char letter = char.ToUpperInvariant(raw);
                int count = WordBank.CountLetter(Word, letter);
                if (count > best)
                {
                    best = count;
                    _eligible.Clear();
                }
                if (count == best && !_eligible.Contains(letter)) _eligible.Add(letter);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (Snared != null) return;
            var creature = other.GetComponentInParent<LetterCreature>();
            if (creature == null || creature.CurrentState == LetterCreature.State.Stuck
                || creature.CurrentState == LetterCreature.State.Captured) return;

            // Only a creature whose letter rules this word can stick; anyone
            // else just wanders across the paper unbothered.
            if (!IsEligible(creature.Letter)) return;

            Snared = creature;
            creature.BecomeStuck(this);
        }

        /// <summary>Remove the paper; frees any snared creature that wasn't captured.</summary>
        public void Remove(bool freeCreature)
        {
            if (freeCreature && Snared != null && Snared.CurrentState == LetterCreature.State.Stuck)
                Snared.Release();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Active.Remove(this);
        }
    }
}
