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
        public float CurrentTime => FrameToTime(currentFrame);
        public bool IsGraphReady => graphReady && graph.IsValid() && mixer.IsValid();
        public AnimationClip CurrentClip { get; private set; }
        public bool IsBlending => currentBlend != null && currentBlend.IsActive;

        class BlendTransition
        {
            public float Duration;
            public float Elapsed;
            public int FromIndex;
            public int ToIndex;
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
                    ToIndex = targetIndex
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

            currentSpeed = Mathf.Max(0.001f, speed);
            currentReverse = reverse;

            SetPlayState(PlayState.Playing);
            float s = currentSpeed * (reverse ? -1f : 1f);

            if (IsBlending)
            {
                clipPlayables[currentBlend.FromIndex].SetSpeed(s);
                clipPlayables[currentBlend.ToIndex].SetSpeed(s);
            }
            else
            {
                clipPlayables[activeIndex].SetSpeed(s);
            }
        }

        public void Pause()
        {
            if (!IsGraphReady) return;
            SetPlayState(PlayState.Paused);

            for (int i = 0; i < clipPlayables.Length; i++)
            {
                if (clipPlayables[i].IsValid())
                    clipPlayables[i].SetSpeed(0f);
            }
        }

        public void Stop()
        {
            SetPlayState(PlayState.Stopped);
            if (!IsGraphReady) return;

            for (int i = 0; i < clipPlayables.Length; i++)
            {
                if (clipPlayables[i].IsValid())
                    clipPlayables[i].SetSpeed(0f);
            }

            currentBlend = null;
        }

        public void SetSpeed(float speed)
        {
            if (!IsGraphReady) return;

            currentSpeed = Mathf.Max(0f, speed);
            float s = currentSpeed * (currentReverse ? -1f : 1f);

            if (IsBlending)
            {
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

        public void UpdateCurrentFrameFromPlayable()
        {
            if (!IsGraphReady) return;

            double t;
            if (IsBlending)
            {
                float weight0 = mixer.GetInputWeight(currentBlend.FromIndex);
                float weight1 = mixer.GetInputWeight(currentBlend.ToIndex);
                double time0 = clipPlayables[currentBlend.FromIndex].GetTime();
                double time1 = clipPlayables[currentBlend.ToIndex].GetTime();
                t = time0 * weight0 + time1 * weight1;
            }
            else
            {
                t = clipPlayables[activeIndex].GetTime();
            }

            currentFrame = TimeToFrame((float)t);
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
                clipPlayables[currentBlend.FromIndex].SetTime(t);
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