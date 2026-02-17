using UnityEngine;

namespace TelleR
{
    public enum DebugMotionPattern
    {
        UpDown,
        LeftRight,
        Circle,
        Figure8,
        Spiral
    }

    public class TrailDebugOscillator : MonoBehaviour
    {
        public DebugMotionPattern Pattern = DebugMotionPattern.UpDown;
        [Range(0.1f, 20f)] public float Speed = 3f;
        [Range(0.01f, 10f)] public float Distance = 0.5f;

        private Vector3 startPosition;
        private float elapsedTime;

        void OnEnable()
        {
            startPosition = transform.localPosition;
            elapsedTime = 0f;
        }

        void Update()
        {
            elapsedTime += Time.deltaTime * Speed;
            Vector3 offset = CalculateOffset(elapsedTime);
            transform.localPosition = startPosition + offset;
        }

        void OnDisable()
        {
            transform.localPosition = startPosition;
        }

        Vector3 CalculateOffset(float t)
        {
            switch (Pattern)
            {
                case DebugMotionPattern.UpDown:
                    return Vector3.up * (Mathf.Sin(t) * Distance);

                case DebugMotionPattern.LeftRight:
                    return Vector3.right * (Mathf.Sin(t) * Distance);

                case DebugMotionPattern.Circle:
                    return new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * Distance;

                case DebugMotionPattern.Figure8:
                    return new Vector3(Mathf.Sin(t) * Distance, 0f, Mathf.Sin(t * 2f) * Distance * 0.5f);

                case DebugMotionPattern.Spiral:
                    float radius = Distance * (0.5f + 0.5f * Mathf.Sin(t * 0.3f));
                    return new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t * 0.5f) * Distance * 0.5f, Mathf.Sin(t) * radius);

                default:
                    return Vector3.zero;
            }
        }
    }
}
