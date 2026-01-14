using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TelleR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
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
        [SerializeField] private List<string> hiddenClips = new List<string>();
        [SerializeField] private FrameEventSystem frameEvents = new FrameEventSystem();

        public UnityEvent<int> OnFrameChanged;
        public UnityEvent<AnimationPlaybackCore.PlayState> OnPlayStateChanged;
        public UnityEvent OnAnimationComplete;

        private AnimationPlaybackCore core;
        private AnimationTransitionSystem transitionSystem;
        private bool initialized;

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
        public FrameEventSystem FrameEvents => frameEvents;

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

        private float EffectivePlaybackSpeed => Mathf.Max(0.001f, playback.PlaybackSpeed);

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
                InitializeSystems();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                transitionSystem?.Cleanup();
                core?.DestroyGraph();
            }
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

            if (!ValidateSetup()) return;

            int startFrame = playback.ReversePlayback ? EffectiveEndFrame : EffectiveStartFrame;
            if (playback.UseInitialFrameOnStart && playback.InitialFrameOnStart >= 0)
                startFrame = Mathf.Clamp(playback.InitialFrameOnStart, 0, MaxFrame);

            JumpToFrame(startFrame);
            frameEvents.ResetCycle();

            if (playback.AutoPlay)
                Play();
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

            if (CurrentPlayState == AnimationPlaybackCore.PlayState.Playing)
            {
                frameEvents.CheckAndFire(CurrentFrame, playback.ReversePlayback);
                transitionSystem.Update(true, dt);
                transitionSystem.CheckLoopAndCompletion(true, playback.Loop, playback.ReversePlayback, EffectiveStartFrame, EffectiveEndFrame);
            }
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
            if (!animator || !clip) return false;

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

            if (targetInfo?.Clip == null) return;

            PerformTransition(targetInfo, blendDuration, speed);
        }

        private void PerformTransition(ClipCatalog.ClipInfo targetInfo, float blendDuration, float overrideSpeed)
        {
            if (targetInfo?.Clip == null) return;

            bool wasPlaying = CurrentPlayState == AnimationPlaybackCore.PlayState.Playing;

            if (blendDuration > 0f && core != null && core.IsGraphReady)
            {
                clip = targetInfo.Clip;
                core.ChangeClipWithBlend(animator, targetInfo.Clip, blendDuration);

                float finalSpeed = overrideSpeed > 0f ? overrideSpeed : playback.PlaybackSpeed;
                playback.PlaybackSpeed = finalSpeed;
                core.SetSpeed(0f);

                if (wasPlaying)
                    core.Play(EffectivePlaybackSpeed, playback.ReversePlayback);
            }
            else
            {
                ChangeClipImmediate(targetInfo.Clip, wasPlaying, overrideSpeed);
            }

            frameEvents.ResetCycle();
        }

        private void ChangeClipImmediate(AnimationClip newClip, bool wasPlaying, float overrideSpeed)
        {
            if (core != null && core.IsGraphReady)
                core.Stop();

            clip = newClip;
            core?.ChangeClip(animator, newClip);

            float finalSpeed = overrideSpeed > 0f ? overrideSpeed : playback.PlaybackSpeed;
            playback.PlaybackSpeed = finalSpeed;

            if (clip != null && core != null && core.IsGraphReady)
            {
                JumpToFrame(EffectiveStartFrame);
                if (wasPlaying)
                    core.Play(EffectivePlaybackSpeed, playback.ReversePlayback);
            }

            frameEvents.ResetCycle();
        }

        public void Play()
        {
            if (!Application.isPlaying) return;
            if (!ValidateSetup()) return;

            frameEvents.ResetCycle();
            core.Play(EffectivePlaybackSpeed, playback.ReversePlayback);
        }

        public void Pause()
        {
            if (!Application.isPlaying) return;
            if (core == null || !core.IsGraphReady) return;

            core.Pause();
        }

        public void Stop()
        {
            if (!Application.isPlaying) return;
            if (core == null) return;

            core.Stop();
            if (clip && core.IsGraphReady) JumpToFrame(EffectiveStartFrame);
            transitionSystem?.Reset();
            frameEvents.ResetCycle();
        }

        public void TogglePlayPause()
        {
            if (!Application.isPlaying) return;

            if (CurrentPlayState == AnimationPlaybackCore.PlayState.Playing) Pause();
            else Play();
        }

        public void JumpToFrame(int frame)
        {
            if (!Application.isPlaying) return;
            if (!ValidateSetup()) return;

            core.JumpToFrame(frame);
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

            InitializeSystems();

            bool wasPlaying = CurrentPlayState == AnimationPlaybackCore.PlayState.Playing;

            if (blendDuration > 0f && clip != null && core != null && core.IsGraphReady)
            {
                clip = newClip;
                core.ChangeClipWithBlend(animator, newClip, blendDuration);
                if (wasPlaying)
                    core.Play(EffectivePlaybackSpeed, playback.ReversePlayback);
            }
            else
            {
                ChangeClipImmediate(newClip, wasPlaying, 0f);
            }

            frameEvents.ResetCycle();
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