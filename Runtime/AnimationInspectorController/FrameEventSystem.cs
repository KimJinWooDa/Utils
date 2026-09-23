using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TelleR
{
    [Serializable]
    public class FrameEvent : ISerializationCallbackReceiver
    {
        /// <summary>발사 범위. Unspecified는 이 값이 생기기 전(1.2.0 이하) 데이터로, Clip 참조로 범위를 추론한다.</summary>
        public enum ClipScope { Unspecified = 0, AllClips = 1, SingleClip = 2 }

        public int Frame;
        public string Label;

        [Tooltip("이 이벤트가 발사될 클립. 비워 두면(None) 모든 클립에서 같은 Frame에 발사됩니다.")]
        public AnimationClip Clip;

        // 범위를 참조와 따로 저장한다 — 빌드에서는 삭제된 클립 참조가 진짜 null로 저장되어 '모든 클립'과 구분할 수 없다.
        // 에디터에서 직렬화할 때(OnBeforeSerialize) 참조로부터 채워 두므로 빌드에도 에디터와 같은 범위가 들어간다
        [SerializeField, HideInInspector] private ClipScope scope;

        public UnityEvent OnTriggered;

        [NonSerialized] public bool FiredThisCycle;

        public FrameEvent()
        {
            // 역직렬화도 이 생성자를 거친다 — 여기서 scope를 정하면 scope가 없는 기존 데이터가 그 값으로 읽힌다
            Frame = 0;
            Label = "";
            OnTriggered = new UnityEvent();
        }

        public FrameEvent(int frame)
        {
            Frame = frame;
            Label = "";
            OnTriggered = new UnityEvent();
            scope = ClipScope.AllClips;
        }

        public FrameEvent(int frame, AnimationClip clip) : this(frame)
        {
            SetClip(clip);
        }

        public ClipScope Scope => scope;

        /// <summary>발사 범위를 지정한다. null이면 모든 클립, 클립이면 그 클립에서만 발사된다.</summary>
        public void SetClip(AnimationClip clip)
        {
            Clip = clip;
            // 이미 파괴된 클립을 넘기면 Missing(SingleClip)으로 둔다 — 직렬화 때 참조로부터 채우는 값과 같게
            scope = HasReference(clip) ? ClipScope.SingleClip : ClipScope.AllClips;
        }

        /// <summary>
        /// Clip이 비어 있으면(모든 클립) 또는 같은 클립이면 true.
        /// 지정했던 클립 에셋이 삭제되어 Missing이 된 이벤트는 어떤 클립에서도(null 포함) 발사되지 않는다('모든 클립'으로 바뀌지 않음).
        /// </summary>
        public bool AppliesTo(AnimationClip clip) => IsAllClips || (!IsClipMissing && Clip == clip);

        /// <summary>Clip을 지정하지 않아(None) 모든 클립에 적용되는지. Missing 참조는 == null이 true지만 여기서는 false다.</summary>
        public bool IsAllClips
        {
            get
            {
                if (Clip != null) return false; // 살아 있는 클립이 지정되어 있으면 범위 값과 무관하게 그 클립 전용
                if (scope == ClipScope.SingleClip) return false;
                if (scope == ClipScope.AllClips) return true;
                return !HasReference(Clip);
            }
        }

        // 에디터에서 역직렬화된 빈 필드는 진짜 null이 아니라 인스턴스 ID 0인 가짜 null이다 — ReferenceEquals만으로 판정하면
        // 기존 'All Clips' 이벤트가 전부 꺼진다. 삭제된 클립(Missing)은 0이 아닌 인스턴스 ID를 유지한다(에디터 한정 — 빌드에서는 진짜 null).
        private static bool HasReference(AnimationClip clip) => !ReferenceEquals(clip, null) && HasObjectId(clip);

        // GetInstanceID는 Unity 6000.6에서 컴파일 오류(obsolete-as-error)라 6000.3+에서는 GetEntityId를 쓴다
        private static bool HasObjectId(UnityEngine.Object obj)
        {
#if UNITY_6000_3_OR_NEWER
            return obj.GetEntityId() != EntityId.None;
#else
            return obj.GetInstanceID() != 0;
#endif
        }

        /// <summary>지정했던 클립이 삭제되어 참조가 Missing인지.</summary>
        public bool IsClipMissing => !IsAllClips && Clip == null;

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
#if UNITY_EDITOR
            // 저장·빌드 직전에 참조로부터 범위를 채운다. 참조 ID는 에디터에서만 남아 있으므로 여기서 기록해야 빌드에서도 구분된다.
            // 직렬화는 다른 스레드에서도 불릴 수 있어 Unity API(인스턴스 ID)는 메인 스레드에서만 읽는다
            if (!EditorMainThread.IsCurrent) return;
            if (HasReference(Clip)) scope = ClipScope.SingleClip;
            else if (scope == ClipScope.Unspecified) scope = ClipScope.AllClips;
#endif
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() { }

#if UNITY_EDITOR
        private static class EditorMainThread
        {
            private static int mainThreadId = -1;

            [UnityEditor.InitializeOnLoadMethod]
            private static void Capture() => mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            public static bool IsCurrent => System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId;
        }
#endif
    }

    [Serializable]
    public class FrameEventSystem
    {
        [SerializeField] private List<FrameEvent> events = new List<FrameEvent>();

        private int lastCheckedFrame = -1;
        private bool hasBaseline;
        private bool isReverse;
        // 핸들러 안에서 Stop()/JumpToFrame() 등으로 기준이 다시 잡히면 진행 중인 검사를 중단하기 위한 세대 번호
        private int cycleVersion;

        public List<FrameEvent> Events => events;
        public int Count => events.Count;

        public FrameEvent GetEvent(int index)
        {
            if (index < 0 || index >= events.Count) return null;
            return events[index];
        }

        public int AddEvent(int frame)
        {
            var ev = new FrameEvent(frame);
            events.Add(ev);
            return events.Count - 1;
        }

        public int AddEvent(int frame, AnimationClip clip)
        {
            var ev = new FrameEvent(frame, clip);
            events.Add(ev);
            return events.Count - 1;
        }

        public void RemoveEvent(int index)
        {
            if (index < 0 || index >= events.Count) return;
            events.RemoveAt(index);
        }

        public void SortByFrame()
        {
            events.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }

        /// <summary>
        /// 기준을 지운다. 다음 검사 틱은 그 틱의 프레임과 정확히 같은 이벤트만 발사하고 기준을 잡는다.
        /// </summary>
        public void ResetCycle()
        {
            hasBaseline = false;
            lastCheckedFrame = -1;
            ClearFired();
        }

        /// <summary>
        /// 새 재생 구간을 시작한다. startFrame 자체도 '지나갈' 프레임으로 보고, 다음 검사에서
        /// startFrame부터 그 틱의 프레임까지 사이의 이벤트를 모두 발사한다(루프 wrap·재생 시작·클립 교체).
        /// </summary>
        public void BeginCycle(int startFrame, bool reverse)
        {
            isReverse = reverse;
            hasBaseline = true;
            lastCheckedFrame = reverse ? startFrame + 1 : startFrame - 1;
            ClearFired();
        }

        /// <summary>
        /// 탐색(seek) 후의 기준. frame에 놓인 이벤트는 발사하지 않고 그 다음 프레임부터 검사한다.
        /// </summary>
        public void SetBaseline(int frame, bool reverse)
        {
            isReverse = reverse;
            hasBaseline = true;
            lastCheckedFrame = frame;
            ClearFired();
        }

        private void ClearFired()
        {
            cycleVersion++;
            for (int i = 0; i < events.Count; i++)
                if (events[i] != null) events[i].FiredThisCycle = false;
        }

        /// <summary>모든 이벤트를 클립 구분·프레임 범위 제한 없이 검사한다(1.1 호환).</summary>
        public void CheckAndFire(int currentFrame, bool reverse)
        {
            CheckAndFireInternal(currentFrame, reverse, null, false, int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// clip에 해당하는 이벤트(Clip이 비어 있거나 같은 클립)만, [minFrame, maxFrame] 범위 안에서 검사한다.
        /// </summary>
        public void CheckAndFire(int currentFrame, bool reverse, AnimationClip clip, int minFrame, int maxFrame)
        {
            CheckAndFireInternal(currentFrame, reverse, clip, true, minFrame, maxFrame);
        }

        private void CheckAndFireInternal(int currentFrame, bool reverse, AnimationClip clip, bool filterClip, int minFrame, int maxFrame)
        {
            if (isReverse != reverse)
            {
                // 재생 방향이 바뀐 틱: 현재 위치를 기준으로 다시 잡는다(방향 전환만으로 이벤트를 발사하지 않음)
                SetBaseline(currentFrame, reverse);
                return;
            }

            if (events.Count == 0)
            {
                // 이벤트가 없어도 기준은 따라가야 재생 중에 추가된 이벤트가 과거 구간까지 몰아서 발사되지 않는다
                lastCheckedFrame = currentFrame;
                hasBaseline = true;
                return;
            }

            int version = cycleVersion;

            if (!hasBaseline)
            {
                // 첫 검사 틱: 현재 프레임과 정확히 일치하는 이벤트만 발사하고 기준을 잡는다
                lastCheckedFrame = currentFrame;
                hasBaseline = true;
                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    if (ev == null || ev.FiredThisCycle || ev.Frame != currentFrame) continue;
                    if (filterClip && !ev.AppliesTo(clip)) continue;
                    if (ev.Frame < minFrame || ev.Frame > maxFrame) continue;

                    ev.FiredThisCycle = true;
                    ev.OnTriggered?.Invoke();
                    if (version != cycleVersion) return; // 핸들러가 기준을 다시 잡음
                }
                return;
            }

            int from = lastCheckedFrame;
            int to = currentFrame;

            // 진행 방향과 반대로 프레임이 움직인 경우는 루프 wrap이 아니라 seek·블렌드로 인한 역행이다 —
            // wrap으로 추론해 발사하면 오발사된다. 실제 루프 wrap은 BeginCycle() 신호로 처리된다.
            bool advanced = reverse ? from > to : from < to;
            lastCheckedFrame = currentFrame;
            if (!advanced) return;

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null || ev.FiredThisCycle) continue;
                if (filterClip && !ev.AppliesTo(clip)) continue;

                int f = ev.Frame;
                if (f < minFrame || f > maxFrame) continue;

                bool crossed = reverse ? (f < from && f >= to) : (f > from && f <= to);
                if (!crossed) continue;

                ev.FiredThisCycle = true;
                ev.OnTriggered?.Invoke();
                if (version != cycleVersion) return; // 핸들러가 Stop/JumpToFrame 등으로 기준을 다시 잡음
            }
        }

        public List<int> GetEventIndicesAtFrame(int frame)
        {
            var result = new List<int>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Frame == frame)
                    result.Add(i);
            }
            return result;
        }

        public bool HasEventAtFrame(int frame)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Frame == frame)
                    return true;
            }
            return false;
        }
    }
}
