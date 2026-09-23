using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace TelleR
{
    public class AnimationPlaybackCore
    {
        public enum PlayState { Stopped, Playing, Paused }

        public event Action<int> OnFrameChanged;
        public event Action<PlayState> OnPlayStateChanged;

        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable[] clipPlayables = new AnimationClipPlayable[2];
        private int activeIndex;
        private bool graphReady;

        private PlayState playState = PlayState.Stopped;
        private int currentFrame;
        private int lastNotifiedFrame = -1;

        private float cachedLength;
        private float cachedFrameRate = 60f;
        private int cachedMaxFrame;

        private BlendTransition currentBlend;

        private float currentSpeed = 1f;
        private bool currentReverse;

        public PlayState CurrentPlayState => playState;
        public int CurrentFrame => currentFrame;
        public int MaxFrame => cachedMaxFrame;
        /// <summary>현재 클립의 길이(초).</summary>
        public float ClipLength => cachedLength;
        public float CurrentTime => FrameToTime(currentFrame);
        public bool IsGraphReady => graphReady && graph.IsValid() && mixer.IsValid();
        public AnimationClip CurrentClip { get; private set; }
        public bool IsBlending => currentBlend != null && currentBlend.IsActive;
        /// <summary>마지막 Play/SetSpeed로 지정된 속도(0 이상, 방향 제외).</summary>
        public float Speed => currentSpeed;
        /// <summary>마지막 Play로 지정된 재생 방향.</summary>
        public bool IsReverse => currentReverse;

        /// <summary>
        /// 현재 클립(블렌드 중이면 새로 들어오는 클립)의 재생 시간(초). 클립 길이로 자르지 않은 원시 값이다.
        /// </summary>
        public double ActiveTime
        {
            get
            {
                if (!IsGraphReady) return 0d;
                int idx = IsBlending ? currentBlend.ToIndex : activeIndex;
                return clipPlayables[idx].IsValid() ? clipPlayables[idx].GetTime() : 0d;
            }
        }

        class BlendTransition
        {
            public float Duration;
            public float Elapsed;
            public int FromIndex;
            public int ToIndex;
            // 재생 중이 아닐 때(완주 후 EndFrame 고정 등) 시작한 블렌드는 빠져나가는 클립을 그 포즈로 멈춰 둔다
            public bool FreezeFrom;
            public bool IsActive => Elapsed < Duration;
            public float Progress => Duration > 0 ? Mathf.Clamp01(Elapsed / Duration) : 1f;
        }

        public void BuildGraph(Animator animator, AnimationClip clip)
        {
            DestroyGraph();
            if (!animator || !clip) return;

            graph = PlayableGraph.Create("AnimPlaybackGraph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            output = AnimationPlayableOutput.Create(graph, "Animation", animator);

            mixer = AnimationMixerPlayable.Create(graph, 2);
            output.SetSourcePlayable(mixer);

            CurrentClip = clip;
            CacheClipData(clip);

            clipPlayables[0] = AnimationClipPlayable.Create(graph, clip);
            clipPlayables[0].SetApplyFootIK(false);
            clipPlayables[0].SetApplyPlayableIK(false);
            clipPlayables[0].SetTime(0);
            clipPlayables[0].SetSpeed(0);

            clipPlayables[1] = AnimationClipPlayable.Create(graph, clip);
            clipPlayables[1].SetApplyFootIK(false);
            clipPlayables[1].SetApplyPlayableIK(false);
            clipPlayables[1].SetTime(0);
            clipPlayables[1].SetSpeed(0);

            graph.Connect(clipPlayables[0], 0, mixer, 0);
            graph.Connect(clipPlayables[1], 0, mixer, 1);

            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 0f);
            activeIndex = 0;

            graph.Play();
            graphReady = true;

            currentSpeed = 1f;
            currentReverse = false;
        }

        public void DestroyGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            graphReady = false;
            currentBlend = null;
            playState = PlayState.Stopped;
        }

        public void ChangeClip(Animator animator, AnimationClip newClip)
        {
            if (CurrentClip == newClip && IsGraphReady) return;
            Stop();
            BuildGraph(animator, newClip);
        }

        public void ChangeClipWithBlend(Animator animator, AnimationClip newClip, float blendDuration)
        {
            if (!IsGraphReady || !newClip) return;

            // 블렌드 도중 다시 전환하면 진행 중이던 블렌드를 먼저 끝내고(새 클립을 기준으로) 다음 블렌드를 시작한다
            FinishBlend();

            float prevSpeed = currentSpeed;
            bool prevReverse = currentReverse;

            int targetIndex = 1 - activeIndex;

            if (clipPlayables[targetIndex].IsValid())
            {
                graph.Disconnect(mixer, targetIndex);
                clipPlayables[targetIndex].Destroy();
            }

            clipPlayables[targetIndex] = AnimationClipPlayable.Create(graph, newClip);
            clipPlayables[targetIndex].SetApplyFootIK(false);
            clipPlayables[targetIndex].SetApplyPlayableIK(false);
            clipPlayables[targetIndex].SetTime(0);
            clipPlayables[targetIndex].SetSpeed(0);

            graph.Connect(clipPlayables[targetIndex], 0, mixer, targetIndex);

            CurrentClip = newClip;
            CacheClipData(newClip);

            if (blendDuration > 0f)
            {
                currentBlend = new BlendTransition
                {
                    Duration = blendDuration,
                    Elapsed = 0f,
                    FromIndex = activeIndex,
                    ToIndex = targetIndex,
                    FreezeFrom = playState != PlayState.Playing
                };

                mixer.SetInputWeight(activeIndex, 1f);
                mixer.SetInputWeight(targetIndex, 0f);
            }
            else
            {
                mixer.SetInputWeight(activeIndex, 0f);
                mixer.SetInputWeight(targetIndex, 1f);
                activeIndex = targetIndex;
                currentBlend = null;
            }

            if (playState == PlayState.Playing)
            {
                float s = prevSpeed * (prevReverse ? -1f : 1f);
                clipPlayables[activeIndex].SetSpeed(s);
                if (currentBlend != null)
                    clipPlayables[currentBlend.ToIndex].SetSpeed(s);
            }
        }

        public void UpdateBlending(float deltaTime)
        {
            if (currentBlend == null || !currentBlend.IsActive) return;

            currentBlend.Elapsed += deltaTime;
            float t = currentBlend.Progress;

            mixer.SetInputWeight(currentBlend.FromIndex, 1f - t);
            mixer.SetInputWeight(currentBlend.ToIndex, t);

            if (!currentBlend.IsActive)
            {
                activeIndex = currentBlend.ToIndex;
                mixer.SetInputWeight(currentBlend.FromIndex, 0f);
                mixer.SetInputWeight(currentBlend.ToIndex, 1f);

                if (clipPlayables[currentBlend.FromIndex].IsValid())
                    clipPlayables[currentBlend.FromIndex].SetSpeed(0f);

                currentBlend = null;
            }
        }

        public void Play(float speed, bool reverse)
        {
            if (!IsGraphReady) return;

            // 속도 0은 제자리 정지(Animator.speed = 0과 같은 의미). 0.001로 올려 '기어가는' 재생을 만들지 않는다
            currentSpeed = Mathf.Max(0f, speed);
            currentReverse = reverse;

            float s = currentSpeed * (reverse ? -1f : 1f);

            if (IsBlending)
            {
                if (!currentBlend.FreezeFrom)
                    clipPlayables[currentBlend.FromIndex].SetSpeed(s);
                clipPlayables[currentBlend.ToIndex].SetSpeed(s);
            }
            else
            {
                clipPlayables[activeIndex].SetSpeed(s);
            }

            // 상태 알림은 그래프 반영이 끝난 뒤에 보낸다(Play/Pause/Stop 공통) — 핸들러가 그 안에서 Pause·클립 교체를
            // 하면, 알림 뒤에 남은 속도 설정이 핸들러의 결과를 덮어써 상태와 실제 재생이 어긋난다
            SetPlayState(PlayState.Playing);
        }

        public void Pause()
        {
            if (!IsGraphReady) return;

            for (int i = 0; i < clipPlayables.Length; i++)
            {
                if (clipPlayables[i].IsValid())
                    clipPlayables[i].SetSpeed(0f);
            }

            SetPlayState(PlayState.Paused);
        }

        public void Stop()
        {
            if (IsGraphReady)
            {
                for (int i = 0; i < clipPlayables.Length; i++)
                {
                    if (clipPlayables[i].IsValid())
                        clipPlayables[i].SetSpeed(0f);
                }

                // 블렌드 도중 정지하면 두 클립이 섞인 채로 남지 않도록 새 클립으로 가중치를 확정한다
                FinishBlend();
            }

            SetPlayState(PlayState.Stopped);
        }

        private void FinishBlend()
        {
            if (currentBlend == null) return;

            if (mixer.IsValid())
            {
                mixer.SetInputWeight(currentBlend.FromIndex, 0f);
                mixer.SetInputWeight(currentBlend.ToIndex, 1f);
            }
            if (clipPlayables[currentBlend.FromIndex].IsValid())
                clipPlayables[currentBlend.FromIndex].SetSpeed(0f);

            activeIndex = currentBlend.ToIndex;
            currentBlend = null;
        }

        public void SetSpeed(float speed)
        {
            if (!IsGraphReady) return;

            currentSpeed = Mathf.Max(0f, speed);
            float s = currentSpeed * (currentReverse ? -1f : 1f);

            if (IsBlending)
            {
                if (!currentBlend.FreezeFrom)
                    clipPlayables[currentBlend.FromIndex].SetSpeed(s);
                clipPlayables[currentBlend.ToIndex].SetSpeed(s);
            }
            else
            {
                clipPlayables[activeIndex].SetSpeed(s);
            }
        }

        public void JumpToFrame(int frame)
        {
            if (!IsGraphReady) return;
            frame = Mathf.Clamp(frame, 0, MaxFrame);
            float t = FrameToTime(frame);
            ApplyTimeToPlayable(t);
            currentFrame = frame;
            NotifyFrameChanged();
        }

        /// <summary>현재 클립을 지정 시간(초)으로 옮긴다. 루프 wrap에서 넘친 시간을 보존할 때 쓴다.</summary>
        public void JumpToTime(double time)
        {
            if (!IsGraphReady) return;
            float t = Mathf.Clamp((float)time, 0f, cachedLength);
            ApplyTimeToPlayable(t);
            currentFrame = TimeToFrame(t);
            NotifyFrameChanged();
        }

        public void UpdateCurrentFrameFromPlayable()
        {
            if (!IsGraphReady) return;

            // 블렌드 중에도 프레임은 새 클립(ToIndex)의 시간만으로 계산한다 — 서로 다른 두 클립의 시간을
            // 가중 평균하면 새 클립 길이로 잘려 즉시 루프 wrap·완료가 일어나고 초반 이벤트가 건너뛰어진다
            currentFrame = TimeToFrame((float)ActiveTime);
            NotifyFrameChanged();
        }

        public float FrameToTime(int frame)
        {
            if (!CurrentClip || cachedFrameRate <= 0f) return 0f;
            frame = Mathf.Clamp(frame, 0, MaxFrame);
            return frame / cachedFrameRate;
        }

        public int TimeToFrame(float time)
        {
            if (!CurrentClip || cachedFrameRate <= 0f) return 0;
            time = Mathf.Clamp(time, 0f, cachedLength);
            return Mathf.RoundToInt(time * cachedFrameRate);
        }

        private void CacheClipData(AnimationClip clip)
        {
            if (clip)
            {
                cachedLength = clip.length;
                cachedFrameRate = clip.frameRate > 0 ? clip.frameRate : 60f;
                cachedMaxFrame = Mathf.Max(0, Mathf.RoundToInt(cachedLength * cachedFrameRate));
            }
            else
            {
                cachedLength = 0;
                cachedFrameRate = 60f;
                cachedMaxFrame = 0;
            }
        }

        private void ApplyTimeToPlayable(float t)
        {
            if (!IsGraphReady) return;

            if (IsBlending)
            {
                // 빠져나가는 클립은 다른 클립이므로 시간을 건드리지 않는다(건드리면 블렌드 중 포즈가 튄다)
                clipPlayables[currentBlend.ToIndex].SetTime(t);
            }
            else
            {
                clipPlayables[activeIndex].SetTime(t);
            }

            graph.Evaluate(0f);
        }

        private void SetPlayState(PlayState newState)
        {
            if (playState == newState) return;
            playState = newState;
            OnPlayStateChanged?.Invoke(newState);
        }

        private void NotifyFrameChanged()
        {
            if (lastNotifiedFrame == currentFrame) return;
            lastNotifiedFrame = currentFrame;
            OnFrameChanged?.Invoke(currentFrame);
        }
    }
}