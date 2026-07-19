using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TelleR
{
    [Serializable]
    public class FrameEvent
    {
        public int Frame;
        public string Label;
        public UnityEvent OnTriggered;

        [NonSerialized] public bool FiredThisCycle;

        public FrameEvent()
        {
            Frame = 0;
            Label = "";
            OnTriggered = new UnityEvent();
        }

        public FrameEvent(int frame)
        {
            Frame = frame;
            Label = "";
            OnTriggered = new UnityEvent();
        }
    }

    [Serializable]
    public class FrameEventSystem
    {
        [SerializeField] private List<FrameEvent> events = new List<FrameEvent>();

        private int lastCheckedFrame = -1;
        private bool isReverse;

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

        public void RemoveEvent(int index)
        {
            if (index < 0 || index >= events.Count) return;
            events.RemoveAt(index);
        }

        public void SortByFrame()
        {
            events.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }

        public void ResetCycle()
        {
            lastCheckedFrame = -1;
            for (int i = 0; i < events.Count; i++)
                events[i].FiredThisCycle = false;
        }

        public void CheckAndFire(int currentFrame, bool reverse)
        {
            if (events.Count == 0) return;

            if (isReverse != reverse)
            {
                isReverse = reverse;
                ResetCycle();
            }

            if (lastCheckedFrame < 0)
            {
                // 첫 검사 틱: baseline만 잡으면 시작 프레임(0 등)에 놓인 이벤트가 영구 미발사되므로
                // 현재 프레임과 정확히 일치하는 이벤트는 즉시 발사한다
                lastCheckedFrame = currentFrame;
                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    if (!ev.FiredThisCycle && ev.Frame == currentFrame)
                    {
                        ev.FiredThisCycle = true;
                        ev.OnTriggered?.Invoke();
                    }
                }
                return;
            }

            int from = lastCheckedFrame;
            int to = currentFrame;

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev.FiredThisCycle) continue;

                bool shouldFire = false;

                // 진행 방향과 반대로 프레임이 움직인 경우는 루프 wrap이 아니라 블렌드 전환·seek로 인한
                // 역행이다 — wrap으로 추론해 발사하면 전환 순간 등록 이벤트가 연쇄 오발사된다.
                // 실제 루프 wrap은 ResetCycle() 신호(OnLoopWrapped)로 처리된다.
                if (!reverse)
                {
                    if (from < to)
                        shouldFire = ev.Frame > from && ev.Frame <= to;
                }
                else
                {
                    if (from > to)
                        shouldFire = ev.Frame < from && ev.Frame >= to;
                }

                if (shouldFire)
                {
                    ev.FiredThisCycle = true;
                    ev.OnTriggered?.Invoke();
                }
            }

            lastCheckedFrame = currentFrame;
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