using UnityEngine;

[CreateAssetMenu(fileName = "BackendConfig", menuName = "Edumy VR/Backend Config")]
public class BackendConfig : ScriptableObject
{
    [Tooltip("Default backend API base URL used when no PlayerPrefs override is set.")]
    public string apiBaseUrl = "https://edumy.onrender.com";
}
