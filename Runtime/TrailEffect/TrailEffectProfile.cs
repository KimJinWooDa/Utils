using UnityEngine;

namespace TelleR
{
    public enum TrailMode
    {
        Color = 0,
        TextureStamp = 2
    }

    public enum StampStyle
    {
        Follow = 0,
        Trail = 1
    }

    [CreateAssetMenu(fileName = "TrailProfile", menuName = "TelleR/Trail Profile")]
    public class TrailEffectProfile : ScriptableObject
    {
        public TrailMode Mode = TrailMode.Color;
        public Color TrailColor = new Color(0f, 0.5f, 1f, 0.6f);
        public Gradient ColorOverLifetime;
        [Range(0.05f, 5f)] public float Duration = 0.5f;
        [Range(1, 60)] public int SnapshotsPerSecond = 30;
        [Range(0f, 2f)] public float ScaleStart = 1f;
        [Range(0f, 2f)] public float ScaleEnd = 1f;
        [Range(0f, 5f)] public float FresnelPower = 3f;
        [Range(0f, 2f)] public float FresnelIntensity = 0f;
        public Texture2D StampTexture;
        [Range(0.1f, 10f)] public float StampSize = 1f;
        public StampStyle StampStyle = StampStyle.Follow;
        [Range(1, 10)] public int StampCount = 4;
        [Range(1f, 30f)] public float StampFollowSpeed = 8f;
        [Range(0.1f, 5f)] public float StampSpacing = 0.5f;
        public bool PreventOverlap = true;
        [Range(4, 128)] public int MaxSnapshots = 32;
        [Range(0.001f, 1f)] public float MinDistance = 0.01f;

        void Reset()
        {
            ColorOverLifetime = new Gradient();
            ColorOverLifetime.SetKeys(
                new[] { new GradientColorKey(Color.cyan, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) }
            );
        }
    }
}
