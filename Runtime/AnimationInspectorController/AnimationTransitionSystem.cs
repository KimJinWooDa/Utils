using System;
using System.Collections.Generic;

namespace TelleR
{
    public class AnimationTransitionSystem
    {
        [Serializable]
        public class AutoTransition
        {
            public string Tag = "";
            public float Delay;
            public float BlendDuration = 0.25f;
            public string TargetState = "";
            public int Layer = -1;
            public float Speed = 1f;
        }

        public event Action OnAnimationComplete;
        public event Action<string, int, float, float> OnStateTransitionRequested;

        private readonly AnimationPlaybackCore core;
        private readonly List<AutoTransition> transitions;
        private readonly HashSet<int> triggeredTransitions = new HashSet<int>();

        private bool timerRunning;
        private float timerValue;
        private string activeTag = "";
        private bool completed;

        public bool IsTimerRunning => timerRunning;
        public float TimerValue => timerValue;
        public string ActiveTag => activeTag;

        public AnimationTransitionSystem(AnimationPlaybackCore playbackCore, List<AutoTransition> autoTransitions)
        {
            core = playbackCore;
            transitions = autoTransitions ?? new List<AutoTransition>();
        }

        public void Update(bool isPlaying, float deltaTime)
        {
            if (!timerRunning || core == null || transitions == null || transitions.Count == 0) return;

            timerValue += deltaTime;

            int count = transitions.Count;
            for (int i = 0; i < count; i++)
            {
                if (triggeredTransitions.Contains(i)) continue;

                AutoTransition t = transitions[i];
                if (t == null) continue;
                if (!TagMatches(t.Tag, activeTag)) continue;
                if (timerValue < t.Delay) continue;

                triggeredTransitions.Add(i);
                float speed = t.Speed <= 0f ? 1f : t.Speed;
                OnStateTransitionRequested?.Invoke(t.TargetState, t.Layer, speed, t.BlendDuration);
            }
        }

        private bool TagMatches(string transitionTag, string active)
        {
            if (string.IsNullOrEmpty(active)) return true;
            return string.Equals(transitionTag, active, StringComparison.Ordinal);
        }

        public void CheckLoopAndCompletion(bool isPlaying, bool loop, bool reverse, int startFrame, int endFrame)
        {
            if (!isPlaying || core == null) return;

            int frame = core.CurrentFrame;

            if (loop)
            {
                if (!reverse && frame > endFrame)
                    core.JumpToFrame(startFrame);
                else if (reverse && frame < startFrame)
                    core.JumpToFrame(endFrame);
                return;
            }

            if (completed) return;

            if (!reverse && frame >= endFrame)
            {
                completed = true;
                OnAnimationComplete?.Invoke();
            }
            else if (reverse && frame <= startFrame)
            {
                completed = true;
                OnAnimationComplete?.Invoke();
            }
        }

        public void Begin(string tag)
        {
            timerRunning = true;
            timerValue = 0f;
            activeTag = tag ?? "";
            triggeredTransitions.Clear();
        }

        public void Pause()
        {
            timerRunning = false;
        }

        public void Resume()
        {
            timerRunning = true;
        }

        public void Reset()
        {
            timerRunning = false;
            timerValue = 0f;
            activeTag = "";
            triggeredTransitions.Clear();
            completed = false;
        }

        public void Cleanup()
        {
            Reset();
        }
    }
}