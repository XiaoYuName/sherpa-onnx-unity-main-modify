using System.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.SceneManagement;

public class OptionSceneButton : MonoBehaviour
{
    private bool isMicrophonePermissionGranted = false;
    
    public void OnClick()
    {
        if (isMicrophonePermissionGranted)
        {
            EnterScene();
        }
        else
        {
            EnterSceneAsync();
        }
    }

    public void EnterSceneAsync()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            var callback = new PermissionCallbacks();
            callback.PermissionDenied += OnPermissionDenied;
            callback.PermissionGranted += OnPermissionGranted;
            Permission.RequestUserPermission(Permission.Microphone, callback);
            return;
        }
        isMicrophonePermissionGranted = true;
        EnterScene();
    }

    private void OnPermissionDenied(string permissionName)
    {
        isMicrophonePermissionGranted = false;
    }

    private void OnPermissionGranted(string permissionName)
    {
        isMicrophonePermissionGranted = true;
    }

    private void EnterScene()
    {
        SceneManager.LoadScene(1);
    }
}
