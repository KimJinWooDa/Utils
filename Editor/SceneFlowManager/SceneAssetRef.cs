using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "SceneAssetRef", menuName = "TelleR/Scene Asset Reference", order = 1)]
public class SceneAssetRef : ScriptableObject
{
#if UNITY_EDITOR
    [Header("Scene Configuration")]
    [SerializeField] private SceneAsset sceneAsset;
#endif
    
    [Header("Scene Flow")]
    [HideInInspector]
    [SerializeField] private string scenePath;
    [SerializeField] private SceneAssetRef previousScene;
    [SerializeField] private SceneAssetRef nextScene;
    public SceneAssetRef PreviousAssetRef => previousScene;
    public SceneAssetRef NextAssetRef => nextScene;
    public int CurrentSceneIndex => GetBuildIndex(scenePath);
    public int PreviousSceneIndex => previousScene != null ? previousScene.CurrentSceneIndex : -1;
    public int NextSceneIndex => nextScene != null ? nextScene.CurrentSceneIndex : -1;
    
    public string SceneName => string.IsNullOrEmpty(scenePath) ? "None" : System.IO.Path.GetFileNameWithoutExtension(scenePath);
    
#if UNITY_EDITOR
    public SceneAsset SceneAsset => sceneAsset;
    
    public void SetPreviousScene(SceneAssetRef prev)
    {
        previousScene = prev;
        EditorUtility.SetDirty(this);
    }
    
    public void SetNextScene(SceneAssetRef next)
    {
        nextScene = next;
        EditorUtility.SetDirty(this);
    }
    
    private void OnValidate()
    {
        scenePath = sceneAsset ? AssetDatabase.GetAssetPath(sceneAsset) : "";
    }
#endif
    
    private static int GetBuildIndex(string path)
    {
        if (string.IsNullOrEmpty(path)) return -1;

        int total = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < total; i++)
        {
            if (SceneUtility.GetScenePathByBuildIndex(i) == path)
                return i;
        }

        Debug.LogWarning($"Scene path '{path}' is not in Build Settings.");
        return -1;
    }
}