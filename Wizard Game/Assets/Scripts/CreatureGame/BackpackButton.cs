using UnityEngine;
using UnityEngine.UI;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// Put this on any UI Button to make it open/close the backpack — the
    /// hand-authorable version of the floating "Bag" button. Scenes that
    /// contain one of these get their button styled your way in the editor,
    /// and the auto-injected floating button stays hidden there (the same
    /// contract as CreatureGameButton).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BackpackButton : MonoBehaviour
    {
        void Awake()
        {
            GetComponent<Button>().onClick.AddListener(BackpackUI.ToggleBackpack);
        }
    }
}
