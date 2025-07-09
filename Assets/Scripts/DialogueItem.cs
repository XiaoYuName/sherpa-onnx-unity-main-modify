using TMPro;
using UnityEngine;

public class DialogueItem : MonoBehaviour
{
    public TextMeshProUGUI dialogueTex;


    public void ShowDialogue(string value)
    {
        dialogueTex.text = value;
    }
}
