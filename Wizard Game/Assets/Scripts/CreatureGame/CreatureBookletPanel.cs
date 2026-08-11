using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The creature booklet as a scene object, so its whole look lives in the
    /// Hierarchy instead of code: the panel, its background, title, close
    /// button and the row template are all ordinary UI objects to art-direct
    /// in the editor. At runtime this component only clones the row template
    /// per creature and fills in the words — content, never layout.
    ///
    /// The mini-game finds this panel automatically (B key / Book button); if
    /// no scene booklet exists, it falls back to its code-built one. Wire a
    /// Close button to Close() via OnClick.
    /// </summary>
    public class CreatureBookletPanel : MonoBehaviour
    {
        [Tooltip("The styled template row. Kept inactive; one active clone is made per creature, stepping downward by Row Spacing.")]
        public CreatureBookletRow rowTemplate;

        [Tooltip("Optional 'Total caught: N' label.")]
        public TMP_Text totalText;

        [Tooltip("Vertical distance between rows, in the template's units.")]
        [Min(10f)] public float rowSpacing = 118f;

        readonly List<GameObject> _rows = new List<GameObject>();
        CreatureGameController _controller;

        public bool IsOpen => gameObject.activeSelf;

        public void Open()
        {
            gameObject.SetActive(true);
            Rebuild();
            CaptureJournal.Changed += OnJournalChanged;
        }

        public void Close()
        {
            CaptureJournal.Changed -= OnJournalChanged;
            gameObject.SetActive(false);
        }

        void OnDisable()
        {
            CaptureJournal.Changed -= OnJournalChanged;
        }

        void OnJournalChanged(char letter) => Rebuild();

        void Rebuild()
        {
            foreach (GameObject row in _rows)
                if (row != null) Destroy(row);
            _rows.Clear();

            if (rowTemplate == null) return;
            if (_controller == null) _controller = FindObjectOfType<CreatureGameController>();
            if (_controller == null) return;

            var templateRect = (RectTransform)rowTemplate.transform;
            int index = 0;
            foreach (CreatureDefinition definition in _controller.creatures)
            {
                if (definition == null) continue;

                CreatureBookletRow row = Instantiate(rowTemplate, rowTemplate.transform.parent);
                var rect = (RectTransform)row.transform;
                rect.anchoredPosition = templateRect.anchoredPosition - new Vector2(0f, index * rowSpacing);
                row.gameObject.SetActive(true);
                row.Fill(definition, CaptureJournal.CountOf(definition.Letter));
                _rows.Add(row.gameObject);
                index++;
            }

            if (totalText != null) totalText.text = $"Total caught: {CaptureJournal.TotalCaught}";
        }
    }
}
