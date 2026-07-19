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
        public event Action OnLoopWrapped;
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
                // Speed<=0은 "현재 재생 속도 유지" 의미로 그대로 전달 (1로 강제하면 사용자의 속도 설정이 매 전환마다 1로 덮임)
                OnStateTransitionRequested?.Invoke(t.TargetState, t.Layer, t.Speed, t.BlendDuration);
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
                // frame은 [0, MaxFrame]으로 클램프되므로 '>'는 endFrame==MaxFrame(기본값)에서 절대 참이 될 수 없음
                // — '>=' 비교여야 루프가 실제로 wrap된다
                if (!reverse && frame >= endFrame && startFrame < endFrame)
                {
                    core.JumpToFrame(startFrame);
                    OnLoopWrapped?.Invoke();
                }
                else if (reverse && frame <= startFrame && startFrame < endFrame)
                {
                    core.JumpToFrame(endFrame);
                    OnLoopWrapped?.Invoke();
                }
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
            completed = false; // 리셋하지 않으면 클립 전환 후 OnAnimationComplete가 다시는 발사되지 않음
        }

        // 클립 교체 경로에서 완료 상태를 리셋하기 위한 공개 API
        public void ResetCompletion()
        {
            completed = false;
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