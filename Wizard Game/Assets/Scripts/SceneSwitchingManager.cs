using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


public class SceneSwitchingManager : MonoBehaviour
{
  public void SceneSwitch(string sceneName)
    {
        // The menu scene has no gameplay script to release a locked cursor, so
        // release it here or the menu would arrive unclickable.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(sceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
