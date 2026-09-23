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
        private bool timerPaused;
        private float timerValue;
        private string activeTag = "";
        private bool completed;
        private bool heldAtEnd;

        public bool IsTimerRunning => timerRunning;
        /// <summary>Pause()로 멈춘 상태(Resume()으로 이어서 진행 가능).</summary>
        public bool IsTimerPaused => timerPaused;
        public float TimerValue => timerValue;
        public string ActiveTag => activeTag;
        /// <summary>비루프 완주가 이미 처리되었는지(OnAnimationComplete 발사 후 다음 재시작 전까지 true).</summary>
        public bool IsCompleted => completed;
        /// <summary>
        /// 비루프 완주로 EndFrame(역재생이면 StartFrame)에 멈춘 뒤 아직 아무 조작도 없는 상태.
        /// 이 동안에도 자동 전환 타이머는 흐르고, 클립 교체는 재생 상태로 이어진다(1.1 동작 호환).
        /// </summary>
        public bool IsHeldAtEnd => heldAtEnd;

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
            // 속도 0은 재생 상태로 제자리에 멈춘 것이다 — 탐색으로 끝(역재생이면 시작)에 놓였다고 wrap·완주하지 않는다
            if (core.Speed <= 0f) return;

            int frame = core.CurrentFrame;

            if (loop)
            {
                // frame은 [0, MaxFrame]으로 클램프되므로 '>'는 endFrame==MaxFrame(기본값)에서 절대 참이 될 수 없음
                // — '>=' 비교여야 루프가 실제로 wrap된다. Start >= End면 구간이 없어 wrap하지 않는다.
                if (startFrame >= endFrame) return;

                double startTime = core.FrameToTime(startFrame);
                // EndFrame이 클립 끝(MaxFrame)이면 반올림 때문에 프레임 시간이 클립 길이를 조금 넘을 수 있다 — 클립 길이로 자른다
                double endTime = Math.Min(core.FrameToTime(endFrame), core.ClipLength);
                double range = endTime - startTime;
                double time = core.ActiveTime;

                // wrap은 반올림한 프레임이 아니라 시간으로 판정한다 — 프레임은 끝 시간보다 최대 반 프레임 먼저 endFrame이 되므로
                // 프레임으로 판정하면 넘친 시간이 음수가 되어 매 루프가 반 프레임씩 짧아진다(주기가 틱 간격에 따라 달라짐).
                // 끝 프레임의 이벤트는 그 반 프레임 동안 이미 endFrame으로 표시·검사된다.
                if (!reverse && time >= endTime)
                {
                    // 끝을 넘어간 시간만큼 시작 쪽으로 이어 붙여 fps와 무관하게 주기를 유지한다
                    double over = time - endTime;
                    over = range > 0d ? over % range : 0d;
                    core.JumpToTime(startTime + over);
                    OnLoopWrapped?.Invoke();
                }
                else if (reverse && time <= startTime)
                {
                    double over = startTime - time;
                    over = range > 0d ? over % range : 0d;
                    core.JumpToTime(endTime - over);
                    OnLoopWrapped?.Invoke();
                }
                return;
            }

            if (completed) return;

            bool reachedEnd = !reverse && frame >= endFrame;
            bool reachedStart = reverse && frame <= startFrame;
            if (!reachedEnd && !reachedStart) return;

            // 비루프 완주: EndFrame(역재생이면 StartFrame)에 고정하고 정지한다 —
            // 플래그만 세우면 EndFrame < 클립 끝일 때 화면은 클립 끝까지 계속 재생되고 상태도 Playing으로 남는다.
            // 정지 알림(OnPlayStateChanged)보다 먼저 완주 대기 상태를 세워, 그 안의 핸들러도 전환을 이어 갈 수 있게 한다
            completed = true;
            heldAtEnd = true;
            core.JumpToFrame(reachedEnd ? endFrame : startFrame);
            core.Stop();
            OnAnimationComplete?.Invoke();
        }

        public void Begin(string tag)
        {
            timerRunning = true;
            timerPaused = false;
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

        /// <summary>비루프 완주 상태를 되살린다(비활성화로 지워진 상태 복원용). OnAnimationComplete는 발사하지 않는다.</summary>
        public void MarkCompleted()
        {
            completed = true;
        }

        /// <summary>완주 대기 상태를 해제한다(명시적 Play/Pause/Stop/탐색/클립 교체 시).</summary>
        public void ReleaseHold()
        {
            heldAtEnd = false;
        }

        public void Pause()
        {
            if (timerRunning) timerPaused = true;
            timerRunning = false;
        }

        public void Resume()
        {
            timerRunning = true;
            timerPaused = false;
        }

        public void Reset()
        {
            timerRunning = false;
            timerPaused = false;
            timerValue = 0f;
            activeTag = "";
            triggeredTransitions.Clear();
            completed = false;
            heldAtEnd = false;
        }

        public void Cleanup()
        {
            Reset();
        }
    }
}