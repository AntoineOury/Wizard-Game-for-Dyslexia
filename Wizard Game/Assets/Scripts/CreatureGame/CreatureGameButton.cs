using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// Attach to a UI Button to make it open one of the mini-game's screens —
    /// the same recipe as ControlSchemeToggleButton: drop a button anywhere in
    /// the scene, add this, pick the action, done. No OnClick wiring needed.
    ///
    /// This is what puts the Book / Trap / Call buttons in the Hierarchy where
    /// their look, size and position can be art-directed in the editor. When a
    /// scene contains ANY of these, the mini-game skips building its own code
    /// -made side buttons and defers to the authored ones entirely.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class CreatureGameButton : MonoBehaviour
    {
        public enum GameAction
        {
            Booklet = 0,
            Trap = 1,
            Call = 2,
        }

        [Tooltip("Which mini-game screen this button opens.")]
        public GameAction action = GameAction.Booklet;

        Button _button;
        CreatureGameController _controller;

        void Awake()
        {
            _button = GetComponent<Button>();
        }

        void OnEnable() => _button.onClick.AddListener(OnClicked);
        void OnDisable() => _button.onClick.RemoveListener(OnClicked);

        void OnClicked()
        {
            // Found lazily at click time, so scene load order never matters.
            if (_controller == null) _controller = FindObjectOfType<CreatureGameController>();
            if (_controller == null) return;

            switch (action)
            {
                case GameAction.Booklet: _controller.ToggleBooklet(); break;
                case GameAction.Trap: _controller.OpenTrapFlow(); break;
                case GameAction.Call: _controller.OpenCallFlow(); break;
            }

            // Clicked buttons stay "selected" and Space (jump) would re-press
            // them — same belt-and-braces as the scheme toggle buttons.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
