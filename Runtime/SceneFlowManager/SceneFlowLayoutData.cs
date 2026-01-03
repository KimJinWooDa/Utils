#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TelleR.SceneFlow
{
    [Serializable]
    public class NodePositionEntry
    {
        public string Guid;
        public Vector2 Position;
        public bool HasCustomPosition;
    }

    public class SceneFlowLayoutData : ScriptableObject
    {
        [SerializeField] private List<NodePositionEntry> entries = new List<NodePositionEntry>();

        private Dictionary<string, NodePositionEntry> cache;

        public Vector2? GetPosition(string guid)
        {
            BuildCacheIfNeeded();
            if (cache.TryGetValue(guid, out var entry) && entry.HasCustomPosition)
            {
                return entry.Position;
            }
            return null;
        }

        public void SetPosition(string guid, Vector2 position)
        {
            BuildCacheIfNeeded();
            
            if (cache.TryGetValue(guid, out var entry))
            {
                entry.Position = position;
                entry.HasCustomPosition = true;
            }
            else
            {
                var newEntry = new NodePositionEntry
                {
                    Guid = guid,
                    Position = position,
                    HasCustomPosition = true
                };
                entries.Add(newEntry);
                cache[guid] = newEntry;
            }
        }

        public void ClearPosition(string guid)
        {
            BuildCacheIfNeeded();
            
            if (cache.TryGetValue(guid, out var entry))
            {
                entry.HasCustomPosition = false;
            }
        }

        public void ClearAllPositions()
        {
            entries.Clear();
            cache?.Clear();
        }

        private void BuildCacheIfNeeded()
        {
            if (cache != null) return;
            
            cache = new Dictionary<string, NodePositionEntry>();
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    cache[entry.Guid] = entry;
                }
            }
        }

        private void OnEnable()
        {
            cache = null;
        }
    }
}
#endif