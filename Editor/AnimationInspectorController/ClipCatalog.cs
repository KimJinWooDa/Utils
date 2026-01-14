using System.Collections.Generic;
using System.Linq;
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
                var paramDict = new Dictionary<string, AnimatorControllerParameterType>();
                foreach (var param in controller.parameters)
                    paramDict[param.name] = param.type;

                for (int layerIdx = 0; layerIdx < controller.layers.Length; layerIdx++)
                {
                    var layer = controller.layers[layerIdx];
                    var stateMachine = layer.stateMachine;

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

                            AnalyzeTransitions(controller, state.state, paramDict, info);
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

#if UNITY_EDITOR
        private static void AnalyzeTransitions(AnimatorController controller, AnimatorState targetState, 
            Dictionary<string, AnimatorControllerParameterType> paramDict, ClipInfo info)
        {
            var foundTypes = new HashSet<TriggerType>();
            string firstParam = "";

            foreach (var layer in controller.layers)
            {
                foreach (var transition in layer.stateMachine.anyStateTransitions)
                {
                    if (transition.destinationState == targetState)
                        AnalyzeConditions(transition.conditions, paramDict, foundTypes, ref firstParam);
                }

                foreach (var state in layer.stateMachine.states)
                {
                    foreach (var transition in state.state.transitions)
                    {
                        if (transition.destinationState == targetState)
                            AnalyzeConditions(transition.conditions, paramDict, foundTypes, ref firstParam);
                    }
                }
            }
        }

        private static void AnalyzeConditions(AnimatorCondition[] conditions, Dictionary<string, AnimatorControllerParameterType> paramDict, 
            HashSet<TriggerType> foundTypes, ref string firstParam)
        {
            foreach (var cond in conditions)
            {
                if (!paramDict.TryGetValue(cond.parameter, out var paramType)) continue;

                if (paramType == AnimatorControllerParameterType.Bool)
                {
                    foundTypes.Add(TriggerType.Bool);
                    if (string.IsNullOrEmpty(firstParam)) firstParam = cond.parameter;
                }
                else if (paramType == AnimatorControllerParameterType.Trigger)
                {
                    foundTypes.Add(TriggerType.Trigger);
                    if (string.IsNullOrEmpty(firstParam)) firstParam = cond.parameter;
                }
            }
        }
#endif
    }
}