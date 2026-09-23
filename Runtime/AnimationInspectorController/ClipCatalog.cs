using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor.Animations;
#endif

namespace TelleR
{
    public static class ClipCatalog
    {
        public enum TriggerType { None, Bool, Trigger, Mixed }

        public class ClipInfo
        {
            public AnimationClip Clip;
            public string StateName;
            public int Layer;
            public string LayerName;
            public bool IsDefault;
        }

        public static List<ClipInfo> CollectWithInfo(Animator animator)
        {
            var result = new List<ClipInfo>();
            if (!animator || !animator.runtimeAnimatorController) return result;

#if UNITY_EDITOR
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller != null)
            {
                for (int layerIdx = 0; layerIdx < controller.layers.Length; layerIdx++)
                {
                    var layer = controller.layers[layerIdx];
                    var stateMachine = layer.stateMachine;
                    if (stateMachine == null) continue; // Synced Layer는 stateMachine이 null

                    foreach (var state in stateMachine.states)
                    {
                        var clip = state.state.motion as AnimationClip;
                        if (clip)
                        {
                            var info = new ClipInfo
                            {
                                Clip = clip,
                                StateName = state.state.name,
                                Layer = layerIdx,
                                LayerName = layer.name,
                                IsDefault = layerIdx == 0 && state.state == stateMachine.defaultState,
                            };

                            result.Add(info);
                        }
                    }
                }
            }
            else
#endif
            {
                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips != null)
                {
                    var set = new HashSet<AnimationClip>();
                    foreach (var c in clips)
                    {
                        if (!c || !set.Add(c)) continue;
                        result.Add(new ClipInfo
                        {
                            Clip = c,
                            StateName = c.name,
                            Layer = 0,
                            LayerName = "Base Layer",
                            IsDefault = result.Count == 0,
                        });
                    }
                }
            }

            return result;
        }
    }
}