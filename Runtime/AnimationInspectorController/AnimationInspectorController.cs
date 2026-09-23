using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TelleR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("TelleR/Animation Inspector Controller")]
    public class AnimationInspectorController : MonoBehaviour
    {
        [Serializable]
        public class PlaybackSettings
        {
            [Range(0f, 4f)] public float PlaybackSpeed = 1f;
            public bool Loop = true;
            public bool AutoPlay = true;
            public bool ReversePlayback;
            [Min(0)] public int StartFrame;
            [Min(-1)] public int EndFrame = -1;
            public bool UseInitialFrameOnStart = true;
            public int InitialFrameOnStart = -1;
        }

        [SerializeField] private Animator animator;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private PlaybackSettings playback = new PlaybackSettings();
        [SerializeField] private List<AnimationTransitionSystem.AutoTransition> autoTransitions = new List<AnimationTransitionSystem.AutoTransition>();
        // 1.2부터 숨긴 클립은 에디터 환경설정(EditorPrefs)에 저장한다. 기존 씬·프리팹 값을 한 번 읽어 옮기기 위해서만 남겨 둔 필드.
        [SerializeField, HideInInspector] private List<string> hiddenClips = new List<string>();
        [SerializeField] private FrameEventSystem frameEvents = new FrameEventSystem();

        public UnityEvent<int> OnFrameChanged;
        public UnityEvent<AnimationPlaybackCore.PlayState> OnPlayStateChanged;
        public UnityEvent OnAnimationComplete;

        private AnimationPlaybackCore core;
        private AnimationTransitionSystem transitionSystem;
        private bool initialized;

        // 비활성화→재활성화 시 재생 상태 복원용 (그래프는 OnDisable에서 파괴된다).
        // 비활성(또는 첫 OnEnable 전) 동안 받은 Play/Pause/Stop/JumpToFrame/ChangeClip도 그래프를 만들지 않고 여기에 기록했다가
        // 다시 켜질 때 적용한다 — 비활성 중에 그래프를 다시 만들면 Update가 돌지 않아 그 포즈로 Animator를 계속 붙잡는다
        private bool started;
        private bool hasResumeState;
        private int resumeFrame;
        private AnimationPlaybackCore.PlayState resumeState;
        // 비활성 중 클립 교체: 기록한 프레임 대신 재생 방향의 구간 시작에서 새 이벤트 구간을 연다
        private bool resumeAtEntry;
        // 비활성 중 정지 상태에서 Play(): 복원할 때 정지 상태의 Play()처럼 재생한다(완주 위치면 되감고 새 이벤트 구간)
        private bool resumeRestart;
        // 꺼질 때 비루프 완주가 이미 처리된 상태였는지(OnDisable의 Cleanup이 완주 표시를 지운다) — 켜질 때 되살린다
        private bool resumeCompleted;

        /// <summary>Play Mode에서 컴포넌트나 GameObject가 꺼져 있는지. 이때 재생 API는 그래프를 만들지 않고 기록만 한다.</summary>
        private bool IsInactiveRuntime => Application.isPlaying && this != null && !isActiveAndEnabled;

        /// <summary>Play Mode에서 컴포넌트가 이미 파괴되었는지. 이때 재생 API는 아무것도 하지 않는다(그래프를 다시 만들면 아무도 파괴하지 않는다).</summary>
        private bool IsDestroyedRuntime => Application.isPlaying && this == null;

        public Animator AnimatorComponent => animator;
        public AnimationClip CurrentClip => clip;
        public AnimationPlaybackCore.PlayState CurrentPlayState => core?.CurrentPlayState ?? AnimationPlaybackCore.PlayState.Stopped;
        public int CurrentFrame => core?.CurrentFrame ?? 0;
        public int MaxFrame => core?.MaxFrame ?? (clip ? Mathf.RoundToInt(clip.length * clip.frameRate) : 0);
        public int EffectiveStartFrame => Mathf.Clamp(playback.StartFrame, 0, MaxFrame);
        public int EffectiveEndFrame => playback.EndFrame < 0 ? MaxFrame : Mathf.Min(playback.EndFrame, MaxFrame);
        public float CurrentTime => core?.CurrentTime ?? 0f;
        public bool IsTransitionRunning => transitionSystem?.IsTimerRunning ?? false;
        public float TransitionTimer => transitionSystem?.TimerValue ?? 0f;
        public string TransitionTag => transitionSystem?.ActiveTag ?? "";
        public bool IsTransitionPaused => transitionSystem?.IsTimerPaused ?? false;
        public FrameEventSystem FrameEvents => frameEvents;

        /// <summary>재생 속도 배율(0~4). 0이면 재생 상태를 유지한 채 제자리에 멈춘다.</summary>
        public float PlaybackSpeed
        {
            get => playback.PlaybackSpeed;
            set
            {
                playback.PlaybackSpeed = Mathf.Clamp(value, 0f, 4f);
                if (core != null && core.IsGraphReady && CurrentPlayState == AnimationPlaybackCore.PlayState.Playing)
                    core.Play(EffectivePlaybackSpeed, playback.ReversePlayback);
            }
        }

        public bool Loop
        {
            get => playback.Loop;
            set => playback.Loop = value;
        }

        /// <summary>역재생 여부. 재생 중에 바꾸면 다음 틱부터 반영된다.</summary>
        public bool ReversePlayback
        {
            get => playback.ReversePlayback;
            set => playback.ReversePlayback = value;
        }

        private float EffectivePlaybackSpeed => Mathf.Clamp(playback.PlaybackSpeed, 0f, 4f);

        private void Reset()
        {
            animator = GetComponent<Animator>();
            AutoDetectClipFromController();
        }

        private void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!clip) AutoDetectClipFromController();
        }

        private void OnEnable()
        {
            if (!animator) animator = GetComponent<Animator>();

            if (Application.isPlaying)
            {
                InitializeSystems();
                if (started && hasResumeState)
                    RestoreAfterReenable();
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                if (core != null && core.IsGraphReady)
                {
                    hasResumeState = true;
                    resumeFrame = core.CurrentFrame;
                    resumeState = core.CurrentPlayState;
                    resumeAtEntry = false;
                    resumeRestart = false;
                    resumeCompleted = transitionSystem != null && transitionSystem.IsCompleted;
                }

                transitionSystem?.Cleanup();
                core?.DestroyGraph();
                // 그래프를 파괴했으므로 다음 OnEnable에서 다시 만들도록 초기화 플래그를 내린다
                initialized = false;
            }
        }

        private void RestoreAfterReenable()
        {
            hasResumeState = false;
            bool atEntry = resumeAtEntry;
            bool restart = resumeRestart;
            bool completed = resumeCompleted;
            resumeAtEntry = false;
            resumeRestart = false;
            resumeCompleted = false;
            if (!ValidateSetup()) return;

            bool reverse = playback.ReversePlayback;
            if (atEntry)
            {
                // 비활성 중 클립 교체: SwitchClip과 같이 새 클립의 구간 시작에서 새 이벤트 구간을 연다
                core.JumpToFrame(reverse ? EffectiveEndFrame : EffectiveStartFrame);
                frameEvents.BeginCycle(core.CurrentFrame, reverse);
            }
            else
            {
                core.JumpToFrame(resumeFrame);
                frameEvents.SetBaseline(core.CurrentFrame, reverse);
            }

            if (restart)
            {
                // 그래프를 새로 만들어 core는 정지 상태다 — 정지 상태의 Play() 경로(완주 위치면 되감기, 새 이벤트 구간)를 그대로 탄다
                PlayNow();
                if (resumeState == AnimationPlaybackCore.PlayState.Paused)
                    core.Pause();
            }
            else if (resumeState == AnimationPlaybackCore.PlayState.Playing)
                core.Play(EffectivePlaybackSpeed, reverse);
            else if (resumeState == AnimationPlaybackCore.PlayState.Paused)
                core.Pause();

            // 완주 뒤 멈춘 상태로 복원했으면 완주 표시도 되살린다 — 이후 Play()가 이어 재생이 아니라 처음부터 재생하고
            // OnAnimationComplete가 다시 발사되지 않게 한다(꺼지지 않았을 때와 같은 동작)
            if (completed && !restart && resumeState != AnimationPlaybackCore.PlayState.Playing)
                transitionSystem?.MarkCompleted();
        }

        /// <summary>비활성 중 재생 API 호출을 기록하기 전에, 기록이 없으면 현재 상태(정지)로 초기화한다.</summary>
        private void BeginPendingState()
        {
            if (hasResumeState) return;
            hasResumeState = true;
            resumeFrame = CurrentFrame;
            resumeState = AnimationPlaybackCore.PlayState.Stopped;
            resumeAtEntry = false;
            resumeRestart = false;
            resumeCompleted = false;
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                transitionSystem?.Cleanup();
                core?.Stop();
                core?.DestroyGraph();
                initialized = false;
            }
        }

        private void Start()
        {
            if (!Application.isPlaying) return;

            InitializeSystems();
            started = true;

            // 첫 활성화 전에 받은 호출: 클립 교체는 이미 clip에 반영되어 있고, 시작 프레임은 아래에서 정한다.
            // Play()만 이어 간다(1.1에서는 그 호출이 그래프를 미리 만들어 재생 상태가 Start 뒤까지 유지됐다)
            bool playRequested = hasResumeState && resumeState == AnimationPlaybackCore.PlayState.Playing;
            hasResumeState = false;
            resumeAtEntry = false;
            resumeRestart = false;
            resumeCompleted = false;

            if (!ValidateSetup()) return;

            int startFrame = playback.ReversePlayback ? EffectiveEndFrame : EffectiveStartFrame;
            if (playback.UseInitialFrameOnStart && playback.InitialFrameOnStart >= 0)
                startFrame = Mathf.Clamp(playback.InitialFrameOnStart, 0, MaxFrame);

            JumpToFrameNow(startFrame);
            frameEvents.ResetCycle();

            if (playback.AutoPlay || playRequested)
                PlayNow();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            RuntimeTick(Time.deltaTime);
        }

        private void InitializeSystems()
        {
            if (initialized) return;

            if (core == null)
            {
                core = new AnimationPlaybackCore();
                core.OnFrameChanged += f => OnFrameChanged?.Invoke(f);
                core.OnPlayStateChanged += s => OnPlayStateChanged?.Invoke(s);
            }

            if (transitionSystem == null)
            {
                transitionSystem = new AnimationTransitionSystem(core, autoTransitions);
                transitionSystem.OnAnimationComplete += () => OnAnimationComplete?.Invoke();
                transitionSystem.OnStateTransitionRequested += HandleStateTransition;
                // 루프 wrap 시 재시작 프레임부터 새 구간을 연다 — 기준을 지우기만 하면(ResetCycle) wrap 직후 틱에서
                // 재시작 프레임~그 틱의 프레임 사이 이벤트가 fps에 따라 건너뛰어진다
                transitionSystem.OnLoopWrapped += () =>
                    frameEvents.BeginCycle(playback.ReversePlayback ? EffectiveEndFrame : EffectiveStartFrame, playback.ReversePlayback);
            }

            if (animator && clip)
                core.BuildGraph(animator, clip);

            initialized = true;
        }

        private void RuntimeTick(float dt)
        {
            if (core == null || !core.IsGraphReady || !clip || transitionSystem == null) return;

            core.UpdateBlending(dt);
            core.UpdateCurrentFrameFromPlayable();

            if (CurrentPlayState != AnimationPlaybackCore.PlayState.Playing)
            {
                // 비루프 완주 후 끝 프레임에 멈춰 있는 동안에도 전환 타이머는 흐른다 — Delay가 클립보다 긴 자동 전환이나
                // OnAnimationComplete에서 호출한 PlayTransition()이 완주 시점에 얼어붙지 않게 한다(1.1 동작 유지)
                if (transitionSystem.IsHeldAtEnd && CurrentPlayState == AnimationPlaybackCore.PlayState.Stopped)
                    transitionSystem.Update(true, dt);
                return;
            }

            // 인스펙터에서 속도·방향을 바꾼 경우 재생 중인 그래프에 반영
            float speed = EffectivePlaybackSpeed;
            if (!Mathf.Approximately(core.Speed, speed) || core.IsReverse != playback.ReversePlayback)
                core.Play(speed, playback.ReversePlayback);

            frameEvents.CheckAndFire(CurrentFrame, playback.ReversePlayback, clip, EffectiveStartFrame, EffectiveEndFrame);

            // 이벤트 핸들러가 Pause/Stop/클립 교체를 했을 수 있으므로 상태를 다시 확인한다
            if (CurrentPlayState != AnimationPlaybackCore.PlayState.Playing || !core.IsGraphReady) return;

            transitionSystem.Update(true, dt);
            if (CurrentPlayState != AnimationPlaybackCore.PlayState.Playing || !core.IsGraphReady) return;

            transitionSystem.CheckLoopAndCompletion(true, playback.Loop, playback.ReversePlayback, EffectiveStartFrame, EffectiveEndFrame);
        }

        private void OnValidate()
        {
            if (!animator)
                animator = GetComponent<Animator>();
        }

        private void AutoDetectClipFromController()
        {
            if (clip || !animator || !animator.runtimeAnimatorController) return;
            var clips = animator.runtimeAnimatorController.animationClips;
            if (clips != null && clips.Length > 0) clip = clips[0];
        }

        private bool ValidateSetup()
        {
            if (IsDestroyedRuntime || !animator || !clip) return false;

            if (!initialized)
                InitializeSystems();

            if (core == null) return false;

            if (!core.IsGraphReady)
                core.BuildGraph(animator, clip);

            return core.IsGraphReady;
        }

        private void HandleStateTransition(string targetState, int layer, float speed, float blendDuration)
        {
            if (string.IsNullOrEmpty(targetState) || !animator) return;

            var clipInfos = ClipCatalog.CollectWithInfo(animator);
            ClipCatalog.ClipInfo targetInfo = null;

            int count = clipInfos.Count;
            for (int i = 0; i < count; i++)
            {
                var info = clipInfos[i];
                if (info.StateName == targetState && (layer < 0 || info.Layer == layer))
                {
                    targetInfo = info;
                    break;
                }
            }

            if (targetInfo == null)
            {
                // 빌드에서는 AnimatorController 접근이 불가해 StateName이 클립명으로 폴백됨 —
                // State명 매칭 실패 시 클립명으로 재시도해야 빌드에서도 자동 전환이 동작한다
                for (int i = 0; i < count; i++)
                {
                    var info = clipInfos[i];
                    if (info.Clip != null && info.Clip.name == targetState)
                    {
                        targetInfo = info;
                        break;
                    }
                }
            }

            if (targetInfo?.Clip == null)
            {
                Debug.LogWarning($"[TelleR/AnimationInspectorController] 전환 대상 '{targetState}'를 찾지 못해 자동 전환을 건너뜁니다. " +
                                 "(빌드에서는 State명이 클립명으로 대체되므로 State명과 클립명이 다르면 매칭에 실패할 수 있습니다)");
                return;
            }

            PerformTransition(targetInfo, blendDuration, speed);
        }

        private void PerformTransition(ClipCatalog.ClipInfo targetInfo, float blendDuration, float overrideSpeed)
        {
            if (targetInfo?.Clip == null) return;
            SwitchClip(targetInfo.Clip, blendDuration, overrideSpeed);
        }

        /// <summary>
        /// 클립 교체 공통 경로. overrideSpeed &gt; 0이면 재생 속도도 바꾼다(0 이하는 현재 속도 유지).
        /// 새 클립은 재생 방향에 맞는 구간 시작(역재생이면 EndFrame)에서 시작하고, 이벤트 구간도 그 프레임부터 연다.
        /// 비루프 완주로 끝 프레임에 멈춘 직후(완주 대기)에 오는 교체는 재생 중이던 것으로 보고 새 클립을 재생한다 —
        /// OnAnimationComplete에서 ChangeClip으로 다음 클립을 잇거나 자동 전환이 완주 뒤에 오는 경우(1.1 동작 유지).
        /// </summary>
        private void SwitchClip(AnimationClip newClip, float blendDuration, float overrideSpeed)
        {
            if (!newClip) return;

            bool heldAtEnd = transitionSystem != null && transitionSystem.IsHeldAtEnd
                             && CurrentPlayState == AnimationPlaybackCore.PlayState.Stopped;
            bool wasPlaying = CurrentPlayState == AnimationPlaybackCore.PlayState.Playing || heldAtEnd;
            transitionSystem?.ReleaseHold();
            if (overrideSpeed > 0f)
                playback.PlaybackSpeed = Mathf.Clamp(overrideSpeed, 0f, 4f);

            bool blend = blendDuration > 0f && clip != null && core != null && core.IsGraphReady;
            clip = newClip;

            if (blend)
            {
                core.ChangeClipWithBlend(animator, newClip, blendDuration);
            }
            else
            {
                if (core != null && core.IsGraphReady)
                    core.Stop();
                core?.ChangeClip(animator, newClip);
            }

            if (core != null && core.IsGraphReady)
            {
                bool reverse = playback.ReversePlayback;
                int entryFrame = reverse ? EffectiveEndFrame : EffectiveStartFrame;
                core.JumpToFrame(entryFrame);
                if (wasPlaying)
                    core.Play(EffectivePlaybackSpeed, reverse);
                frameEvents.BeginCycle(core.CurrentFrame, reverse);
            }
            else
            {
                frameEvents.ResetCycle();
            }

            transitionSystem?.ResetCompletion();
        }

        public void Play()
        {
            if (!Application.isPlaying || IsDestroyedRuntime) return;
            if (IsInactiveRuntime)
            {
                BeginPendingState();
                // 정지 상태이거나 비루프 완주 뒤의 일시정지면, 활성 상태의 Play()처럼 처음부터 다시 재생한다
                if (resumeState == AnimationPlaybackCore.PlayState.Stopped || (resumeCompleted && !playback.Loop))
                    resumeRestart = true;
                resumeState = AnimationPlaybackCore.PlayState.Playing;
                return;
            }

            PlayNow();
        }

        private void PlayNow()
        {
            if (!ValidateSetup()) return;

            transitionSystem?.ReleaseHold();

            // 일시정지에서 이어서 재생할 때는 이벤트 기준을 유지한다 — 리셋하면 멈춘 프레임의 이벤트가
            // 다시 발사되어, 핸들러에서 Pause()한 뒤 나중에 Play()하는 패턴이 무한 반복된다.
            // 단, 완주 뒤에 Pause()로 들어간 일시정지는 이어 갈 재생이 없으므로 정지 상태처럼 처음부터 다시 재생한다
            // (그대로 이어 가면 완주 판정이 이미 끝나 EndFrame을 지나 클립 끝까지 흘러간다)
            bool completedPause = core.CurrentPlayState == AnimationPlaybackCore.PlayState.Paused
                                  && transitionSystem != null && transitionSystem.IsCompleted && !playback.Loop;
            if (core.CurrentPlayState == AnimationPlaybackCore.PlayState.Stopped || completedPause)
            {
                bool reverse = playback.ReversePlayback;
                int start = EffectiveStartFrame;
                int end = EffectiveEndFrame;
                int frame = core.CurrentFrame;

                // 비루프 완주로 끝(역재생이면 시작)에 멈춰 있으면 처음부터 다시 재생
                if (start < end)
                {
                    if (!reverse && frame >= end) core.JumpToFrame(start);
                    else if (reverse && frame <= start) core.JumpToFrame(end);
                }

                frameEvents.BeginCycle(core.CurrentFrame, reverse);
                transitionSystem?.ResetCompletion();
            }

            core.Play(EffectivePlaybackSpeed, playback.ReversePlayback);
        }

        public void Pause()
        {
            if (!Application.isPlaying || IsDestroyedRuntime) return;
            if (IsInactiveRuntime)
            {
                BeginPendingState();
                resumeState = AnimationPlaybackCore.PlayState.Paused;
                return;
            }
            if (core == null || !core.IsGraphReady) return;

            transitionSystem?.ReleaseHold();
            core.Pause();
        }

        public void Stop()
        {
            if (!Application.isPlaying || IsDestroyedRuntime) return;
            if (IsInactiveRuntime)
            {
                // 정지는 구간 시작으로 되돌린다(클립 길이는 다시 켜질 때 그래프가 자른다)
                BeginPendingState();
                resumeState = AnimationPlaybackCore.PlayState.Stopped;
                resumeFrame = Mathf.Max(0, playback.StartFrame);
                resumeAtEntry = false;
                resumeRestart = false;
                resumeCompleted = false;
                transitionSystem?.Reset();
                return;
            }
            if (core == null) return;

            core.Stop();
            if (clip && core.IsGraphReady) JumpToFrameNow(EffectiveStartFrame);
            transitionSystem?.Reset();
            frameEvents.ResetCycle();
        }

        public void TogglePlayPause()
        {
            if (!Application.isPlaying || IsDestroyedRuntime) return;

            // 비활성 중에는 그래프가 없어 CurrentPlayState가 항상 Stopped다 — 기록해 둔 상태로 판단한다
            bool playing = IsInactiveRuntime
                ? hasResumeState && resumeState == AnimationPlaybackCore.PlayState.Playing
                : CurrentPlayState == AnimationPlaybackCore.PlayState.Playing;

            if (playing) Pause();
            else Play();
        }

        public void JumpToFrame(int frame)
        {
            if (!Application.isPlaying || IsDestroyedRuntime) return;
            if (IsInactiveRuntime)
            {
                BeginPendingState();
                resumeFrame = Mathf.Max(0, frame);
                resumeAtEntry = false;
                resumeRestart = false;
                return;
            }

            JumpToFrameNow(frame);
        }

        private void JumpToFrameNow(int frame)
        {
            if (!ValidateSetup()) return;

            // 명시적 탐색은 완주 대기를 끝낸다(탐색한 자리에 정지한 채로 남는다)
            transitionSystem?.ReleaseHold();
            core.JumpToFrame(frame);
            // 탐색한 프레임부터 다시 검사(건너뛴 구간의 이벤트는 발사하지 않고, 되감은 구간은 다시 발사 가능)
            frameEvents.SetBaseline(core.CurrentFrame, playback.ReversePlayback);
        }

        public void ChangeClip(AnimationClip newClip)
        {
            ChangeClip(newClip, 0f);
        }

        public void ChangeClip(AnimationClip newClip, float blendDuration)
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!newClip) return;

            // 에디터에서는 clip만 변경
            if (!Application.isPlaying)
            {
                clip = newClip;
                return;
            }

            if (IsDestroyedRuntime) return;

            // 비활성 중에는 clip만 바꾸고, 다시 켜질 때 새 클립의 구간 시작에서 이어 간다(재생 중이었으면 계속 재생)
            if (IsInactiveRuntime)
            {
                clip = newClip;
                BeginPendingState();
                resumeAtEntry = true;
                resumeRestart = false;
                resumeCompleted = false; // 클립 교체는 완주 상태를 지운다(SwitchClip과 같음)
                if (resumeState != AnimationPlaybackCore.PlayState.Playing)
                    resumeState = AnimationPlaybackCore.PlayState.Stopped;
                return;
            }

            InitializeSystems();
            SwitchClip(newClip, blendDuration, 0f);
        }

        public void PlayTransition(string tag = "")
        {
            if (!Application.isPlaying) return;
            transitionSystem?.Begin(tag);
        }

        public void PauseTransition()
        {
            if (!Application.isPlaying) return;
            transitionSystem?.Pause();
        }

        public void ResumeTransition()
        {
            if (!Application.isPlaying) return;
            transitionSystem?.Resume();
        }

        public void StopTransition()
        {
            if (!Application.isPlaying) return;
            transitionSystem?.Reset();
        }

        [Obsolete("Use PlayTransition instead")]
        public void StartTransitionTimer(string tag = "") => PlayTransition(tag);

        [Obsolete("Use StopTransition instead")]
        public void ResetTransitionTimer() => StopTransition();
    }
}