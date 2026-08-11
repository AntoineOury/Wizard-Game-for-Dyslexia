using TMPro;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// One booklet entry's text fields. Lives on the row TEMPLATE authored in
    /// the scene: style the template — fonts, colors, layout, decorations —
    /// and every creature's row is a clone of it filled with that creature's
    /// data. Any field left empty is simply skipped, so a minimal row (say,
    /// just letter + count) is fine.
    ///
    /// Its own file (not tucked into CreatureBookletPanel.cs) because scenes
    /// can only reference a MonoBehaviour whose file carries its name.
    /// </summary>
    public class CreatureBookletRow : MonoBehaviour
    {
        public TMP_Text letterText;
        public TMP_Text nameText;
        public TMP_Text countText;
        public TMP_Text blurbText;

        public void Fill(CreatureDefinition definition, int caught)
        {
            if (letterText != null) letterText.text = definition.Letter.ToString();
            if (nameText != null) nameText.text = definition.DisplayName;
            if (countText != null) countText.text = $"Caught: {caught}";
            if (blurbText != null)
                blurbText.text = caught > 0
                    ? definition.blurb
                    : "Not caught yet! Lay a word trap and trace its letter.";
        }
    }
}
