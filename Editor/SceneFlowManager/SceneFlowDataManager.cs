#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TelleR.SceneFlow
{
    public class SceneFlowDataManager
    {
        private List<SceneAssetRef> allScenes = new List<SceneAssetRef>();
        
        public List<SceneAssetRef> AllScenes => allScenes;
        
        public void LoadScenes()
        {
            allScenes.Clear();
            
            // Find all SceneAssetRef assets in the project
            string[] guids = AssetDatabase.FindAssets("t:SceneAssetRef");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                SceneAssetRef sceneRef = AssetDatabase.LoadAssetAtPath<SceneAssetRef>(path);
                if (sceneRef != null)
                {
                    allScenes.Add(sceneRef);
                }
            }
            
            allScenes = allScenes.OrderBy(s => s.SceneName).ToList();
        }
        
        public SceneAssetRef GetSceneByName(string name)
        {
            return allScenes.FirstOrDefault(s => s.SceneName.ToLower() == name.ToLower());
        }
        
        public (SceneAssetRef awake, SceneAssetRef lobby, SceneAssetRef room) GetCoreScenes()
        {
            var awake = GetSceneByName("Awake");
            var lobby = GetSceneByName("Lobby");
            var room = GetSceneByName("Room");
            
            return (awake, lobby, room);
        }
        
        public List<SceneAssetRef> GetScenesWithoutPrevious()
        {
            return allScenes.Where(s => s.PreviousAssetRef == null).ToList();
        }
    }
}
#endif