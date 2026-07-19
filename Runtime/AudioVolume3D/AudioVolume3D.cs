using UnityEngine;
using System.Collections.Generic;

namespace TelleR
{
    public class AudioVolume3D : MonoBehaviour
    {
        [Header("Visualization Settings")]
        public Color ZoneColor = new Color(0f, 1f, 0f, 1f);
        public Color FadeZoneColor = new Color(1f, 0.85f, 0.4f, 1f);
        public Color OcclusionZoneColor = new Color(0f, 0.5f, 1f, 1f);

        public bool ShowLabel = true;
        public bool ShowMainVolume = true;
        public bool ShowFadeZone = true;
        public bool ShowInnerVolumes = true;
        public bool ShowOcclusionZones = true;
        [Range(0.01f, 3f)]
        public float GizmoHandleScale = 1f;
        
        [Header("Main Volume Settings")]
        public Vector3 VolumeCenter = Vector3.zero;
        public Vector3 VolumeSize = new Vector3(5, 3, 5);
        public float FadeDistance = 3.0f;
        public bool UseHeightAttenuation = true;
        [Range(0f, 1f)] public float MaxVolume = 1.0f;

        [Header("Fade Smoothing")]
        [Range(0.5f, 10f)] public float FadeInSpeed = 2.0f;
        [Range(0.5f, 10f)] public float FadeOutSpeed = 3.0f;

        [Header("Occlusion Settings")]
        public List<ManualOcclusionZone> ManualOcclusionZones = new List<ManualOcclusionZone>();
        [Range(0.5f, 10f)] public float OcclusionSmoothSpeed = 3.0f;

        [Header("Inner Volumes")]
        public List<InnerVolume> InnerVolumes = new List<InnerVolume>();

        [Header("Audio Settings")]
        public AudioClip Clip;
        public UnityEngine.Audio.AudioMixerGroup OutputGroup;
        [Range(0f, 1f)] public float SpatialBlend = 1.0f;
        public bool AutoSpatialBlend = true;
        public float MinDistance = 1.0f;
        public float MaxDistance = 500.0f;
        public bool Loop = true;
        public bool PlayOnAwake = true;

        [Header("Tracking")]
        [HideInInspector] public Transform TargetTransform;
        public string TargetTag = "Player";

        public enum VolumeShape { Sphere, Box }

        [System.Serializable]
        public class InnerVolume
        {
            public string Name = "Source Area";
            public VolumeShape Shape = VolumeShape.Box;
            public Vector3 LocalPosition;
            public Vector3 Size = new Vector3(2, 2, 2);
            public float Radius = 2.0f;
            public float FalloffDistance = 2.0f;
            [Range(0f, 1f)] public float Weight = 1.0f;
        }

        [System.Serializable]
        public class ManualOcclusionZone
        {
            public string Name = "Occlusion Area";
            public VolumeShape Shape = VolumeShape.Box;
            public Vector3 LocalPosition;
            public Vector3 Size = new Vector3(2, 2, 2);
            public float Radius = 2.0f;
            [Range(0f, 1f)] public float TargetVolume = 0.2f;
            [Range(10f, 22000f)] public float TargetCutoff = 400f;
            public float FadeDistance = 1.0f;
        }

        private GameObject emitterObject;
        private AudioSource audioSource;
        private AudioLowPassFilter lowPassFilter;
        private Transform selfTransform;
        private Transform emitterTransform;
        private Transform target;

        private float currentFadeFactor;
        private float currentOcclusionVol = 1f;
        private float currentOcclusionCutoff = 22000f;
        private float targetOcclusionVol = 1f;
        private float targetOcclusionCutoff = 22000f;

        private float occlusionCheckTimer;
        private InnerVolume[] innerVolumeCache;
        private ManualOcclusionZone[] occlusionZoneCache;

        private const float DEFAULT_CUTOFF = 22000f;
        private const float OCCLUSION_CHECK_INTERVAL = 0.1f;

        private bool isInitialized;
        private bool hasStartedPlayback;
        private bool targetIsFallback;
        private bool tagSearchFailed;
        private float nextTargetSearchTime;

        private void Awake()
        {
            selfTransform = transform;
            CreateVirtualEmitter();
            CacheVolumes();
        }

        private void Start()
        {
            InitializeTarget();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                CacheVolumes();
        }

        private void OnEnable()
        {
            if (audioSource && !audioSource.isPlaying && PlayOnAwake)
            {
                audioSource.Play();
                hasStartedPlayback = true;
            }
        }

        public void Play()
        {
            if (!audioSource) return;
            audioSource.Play();
            hasStartedPlayback = true;
        }

        public void Stop()
        {
            if (!audioSource) return;
            audioSource.Stop();
            hasStartedPlayback = false;
        }

        private void OnDisable()
        {
            if (audioSource) audioSource.Stop();
        }

        private void OnDestroy()
        {
            if (emitterObject) Destroy(emitterObject);
        }

        private void InitializeTarget()
        {
            if (TargetTransform != null)
            {
                target = TargetTransform;
                targetIsFallback = false;
                return;
            }

            if (!tagSearchFailed)
            {
                try
                {
                    var go = GameObject.FindGameObjectWithTag(TargetTag);
                    if (go)
                    {
                        target = go.transform;
                        targetIsFallback = false;
                        return;
                    }
                }
                catch (UnityException)
                {
                    tagSearchFailed = true; // 태그 미정의 — 반복 예외 방지
                }
            }

            // 카메라는 임시 폴백 — 이후 태그 대상이 스폰되면 교체된다
            if (target == null && Camera.main)
            {
                target = Camera.main.transform;
                targetIsFallback = true;
            }
        }

        private void CreateVirtualEmitter()
        {
            emitterObject = new GameObject($"{gameObject.name}_Emitter");
            emitterObject.transform.SetParent(selfTransform);
            emitterObject.transform.localPosition = Vector3.zero;
            emitterTransform = emitterObject.transform;

            audioSource = emitterObject.AddComponent<AudioSource>();
            audioSource.clip = Clip;
            audioSource.outputAudioMixerGroup = OutputGroup;
            audioSource.spatialBlend = SpatialBlend;
            audioSource.minDistance = MinDistance;
            audioSource.maxDistance = MaxDistance;
            audioSource.loop = Loop;
            audioSource.playOnAwake = false;
            audioSource.dopplerLevel = 0f;
            // 첫 Update의 페이드 계산 전까지 무음 — 기본 volume(1)로 시작하면 씬 시작 시
            // 원거리 볼륨이 최대 음량으로 터진 뒤 서서히 잦아드는 소리 사고가 남
            audioSource.volume = 0f;

            lowPassFilter = emitterObject.AddComponent<AudioLowPassFilter>();
            lowPassFilter.cutoffFrequency = DEFAULT_CUTOFF;

            if (PlayOnAwake)
            {
                audioSource.Play();
                hasStartedPlayback = true;
            }
        }

        private void CacheVolumes()
        {
            innerVolumeCache = InnerVolumes.ToArray();
            occlusionZoneCache = ManualOcclusionZones.ToArray();
        }

        private void Update()
        {
            if (!audioSource) return;

            // 타깃이 없거나 카메라 폴백 상태면 주기적으로 재탐색 — 씬 로드 후 스폰되는 플레이어 대응
            if ((target == null || targetIsFallback) && Time.time >= nextTargetSearchTime)
            {
                nextTargetSearchTime = Time.time + 1f;
                InitializeTarget();
            }
            if (target == null) return;

            Vector3 worldCenter = selfTransform.TransformPoint(VolumeCenter);
            float distSqr = (target.position - worldCenter).sqrMagnitude;
            float maxDim = Mathf.Max(VolumeSize.x, Mathf.Max(VolumeSize.y, VolumeSize.z));
            float activeRange = (maxDim * 0.5f) + FadeDistance;
            float activeRangeSqr = activeRange * activeRange;

            if (distSqr > activeRangeSqr * 4.0f)
            {
                if (audioSource.volume > 0.001f)
                    audioSource.volume = Mathf.Lerp(audioSource.volume, 0f, Time.deltaTime * 5f);
                else if (audioSource.isPlaying)
                    audioSource.Pause();
                return;
            }

            Vector3 targetLocalPos = selfTransform.InverseTransformPoint(target.position);

            CalculateEmitterPosition(targetLocalPos);
            CalculateVolumeFade(targetLocalPos);
            UpdateOcclusionTarget(targetLocalPos);
            ApplyAudioValues();
            
            isInitialized = true;  
        }

        private void CalculateEmitterPosition(Vector3 targetLocalPos)
        {
            Vector3 halfSize = VolumeSize * 0.5f;
            Vector3 relativeToCenter = targetLocalPos - VolumeCenter;

            Vector3 mainClosestLocal = VolumeCenter + new Vector3(
                Mathf.Clamp(relativeToCenter.x, -halfSize.x, halfSize.x),
                Mathf.Clamp(relativeToCenter.y, -halfSize.y, halfSize.y),
                Mathf.Clamp(relativeToCenter.z, -halfSize.z, halfSize.z)
            );

            Vector3 finalEmitterLocal = mainClosestLocal;

            if (innerVolumeCache != null && innerVolumeCache.Length > 0)
            {
                float bestWeight = 0f;
                Vector3 bestInnerPoint = mainClosestLocal;

                for (int i = 0; i < innerVolumeCache.Length; i++)
                {
                    InnerVolume vol = innerVolumeCache[i];
                    float weight = GetInnerVolumeWeight(vol, targetLocalPos);

                    if (weight > bestWeight)
                    {
                        bestWeight = weight;
                        bestInnerPoint = GetClosestPointOnInnerVolume(vol, targetLocalPos);
                    }
                }
                if (bestWeight > 0.001f)
                    finalEmitterLocal = Vector3.Lerp(mainClosestLocal, bestInnerPoint, bestWeight);
            }
            emitterTransform.position = selfTransform.TransformPoint(finalEmitterLocal);
        }

        private void CalculateVolumeFade(Vector3 targetLocalPos)
        {
            Vector3 halfSize = VolumeSize * 0.5f;
            Vector3 relativeToCenter = targetLocalPos - VolumeCenter;

            float dx = Mathf.Max(0, Mathf.Abs(relativeToCenter.x) - halfSize.x);
            float dy = Mathf.Max(0, Mathf.Abs(relativeToCenter.y) - halfSize.y);
            float dz = Mathf.Max(0, Mathf.Abs(relativeToCenter.z) - halfSize.z);

            float sqrDist = UseHeightAttenuation ? (dx * dx + dy * dy + dz * dz) : (dx * dx + dz * dz);

            float dist = Mathf.Sqrt(sqrDist);
            float targetFade;

            if (dist <= 0.001f) targetFade = 1.0f;
            else if (dist >= FadeDistance) targetFade = 0.0f;
            else
            {
                float t = 1.0f - (dist / FadeDistance);
                targetFade = t * t * t * (t * (t * 6f - 15f) + 10f);
            }

            if (!isInitialized)
            {
                currentFadeFactor = targetFade;
                return;
            }

            float fadeSpeed = targetFade > currentFadeFactor ? FadeInSpeed : FadeOutSpeed;
            currentFadeFactor = Mathf.Lerp(currentFadeFactor, targetFade, Time.deltaTime * fadeSpeed);
        }
        
        private void UpdateOcclusionTarget(Vector3 targetLocalPos)
        {
            if (isInitialized)
            {
                occlusionCheckTimer += Time.deltaTime;
                if (occlusionCheckTimer < OCCLUSION_CHECK_INTERVAL) return;
                occlusionCheckTimer = 0f;
            }

            float finalTargetVol = 1f;
            float finalTargetCutoff = DEFAULT_CUTOFF;

            if (occlusionZoneCache != null && occlusionZoneCache.Length > 0)
            {
                for (int i = 0; i < occlusionZoneCache.Length; i++)
                {
                    ManualOcclusionZone zone = occlusionZoneCache[i];
                    float intensity = GetOcclusionIntensity(zone, targetLocalPos);

                    if (intensity > 0f)
                    {
                        float zoneVol = Mathf.Lerp(1f, zone.TargetVolume, intensity);
                        float zoneCutoff = Mathf.Lerp(DEFAULT_CUTOFF, zone.TargetCutoff, intensity);

                        if (zoneVol < finalTargetVol) finalTargetVol = zoneVol;
                        if (zoneCutoff < finalTargetCutoff) finalTargetCutoff = zoneCutoff;
                    }
                }
            }

            targetOcclusionVol = finalTargetVol;
            targetOcclusionCutoff = finalTargetCutoff;

            if (!isInitialized)
            {
                currentOcclusionVol = targetOcclusionVol;
                currentOcclusionCutoff = targetOcclusionCutoff;
            }
        }
        
        private float GetOcclusionIntensity(ManualOcclusionZone zone, Vector3 targetLocal)
        {
            Vector3 relative = targetLocal - zone.LocalPosition;
            float distToEdge = 0f;

            if (zone.Shape == VolumeShape.Sphere)
            {
                float dist = relative.magnitude;
                distToEdge = Mathf.Max(0, dist - zone.Radius);
            }
            else
            {
                Vector3 half = zone.Size * 0.5f;
                float dx = Mathf.Max(0, Mathf.Abs(relative.x) - half.x);
                float dy = Mathf.Max(0, Mathf.Abs(relative.y) - half.y);
                float dz = Mathf.Max(0, Mathf.Abs(relative.z) - half.z);
                distToEdge = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            if (distToEdge <= 0.0001f) return 1.0f;
            if (distToEdge >= zone.FadeDistance) return 0.0f;
            return 1.0f - (distToEdge / zone.FadeDistance);
        }

        private void ApplyAudioValues()
        {
            if (isInitialized)
            {
                float smoothDelta = Time.deltaTime * OcclusionSmoothSpeed;
                currentOcclusionVol = Mathf.Lerp(currentOcclusionVol, targetOcclusionVol, smoothDelta);
                currentOcclusionCutoff = Mathf.Lerp(currentOcclusionCutoff, targetOcclusionCutoff, smoothDelta);
            }

            float targetSpatialBlend = SpatialBlend;
            if (AutoSpatialBlend && (innerVolumeCache == null || innerVolumeCache.Length == 0))
                targetSpatialBlend = Mathf.Lerp(SpatialBlend, 0f, currentFadeFactor);

            float finalVol = MaxVolume * currentFadeFactor * currentOcclusionVol;

            if (!isInitialized)
            {
                audioSource.spatialBlend = targetSpatialBlend;
                audioSource.volume = finalVol;
                lowPassFilter.cutoffFrequency = currentOcclusionCutoff;
            }
            else
            {
                audioSource.spatialBlend = Mathf.Lerp(audioSource.spatialBlend, targetSpatialBlend, Time.deltaTime * 2f);
                float volSpeed = finalVol > audioSource.volume ? FadeInSpeed : FadeOutSpeed;
                audioSource.volume = Mathf.Lerp(audioSource.volume, finalVol, Time.deltaTime * volSpeed);
                lowPassFilter.cutoffFrequency = Mathf.Lerp(lowPassFilter.cutoffFrequency, currentOcclusionCutoff, Time.deltaTime * OcclusionSmoothSpeed);
            }

            if (audioSource.volume < 0.001f)
            {
                if (audioSource.isPlaying) audioSource.Pause();
            }
            else
            {
                if (!audioSource.isPlaying)
                {
                    // 한 번도 Play되지 않은 소스는 UnPause로 시작되지 않음 — PlayOnAwake=false여도
                    // 범위에 들어오면 재생이 시작되도록 Play 폴백
                    if (hasStartedPlayback) audioSource.UnPause();
                    else { audioSource.Play(); hasStartedPlayback = true; }
                }
            }
        }

        private float GetInnerVolumeWeight(InnerVolume vol, Vector3 targetLocal)
        {
            Vector3 relativePos = targetLocal - vol.LocalPosition;
            float dist;

            if (vol.Shape == VolumeShape.Sphere)
            {
                dist = Mathf.Max(0, relativePos.magnitude - vol.Radius);
            }
            else
            {
                Vector3 half = vol.Size * 0.5f;
                float dX = Mathf.Max(0, Mathf.Abs(relativePos.x) - half.x);
                float dY = Mathf.Max(0, Mathf.Abs(relativePos.y) - half.y);
                float dZ = Mathf.Max(0, Mathf.Abs(relativePos.z) - half.z);
                dist = Mathf.Sqrt(dX * dX + dY * dY + dZ * dZ);
            }
            float t = 1.0f - Mathf.Clamp01(dist / vol.FalloffDistance);
            return t * t * (3f - 2f * t) * vol.Weight;
        }

        private Vector3 GetClosestPointOnInnerVolume(InnerVolume vol, Vector3 targetLocal)
        {
            if (vol.Shape == VolumeShape.Sphere)
            {
                Vector3 offset = targetLocal - vol.LocalPosition;
                float dist = offset.magnitude;
                if (dist <= vol.Radius) return targetLocal;
                return vol.LocalPosition + offset / dist * vol.Radius;
            }

            Vector3 relative = targetLocal - vol.LocalPosition;
            Vector3 half = vol.Size * 0.5f;
            Vector3 closest = new Vector3(
                Mathf.Clamp(relative.x, -half.x, half.x),
                Mathf.Clamp(relative.y, -half.y, half.y),
                Mathf.Clamp(relative.z, -half.z, half.z)
            );
            return vol.LocalPosition + closest;
        }

        private void OnDrawGizmos()
        {
            if (!ShowMainVolume) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Color c = ZoneColor;
            c.a = 0.8f;
            Gizmos.color = c;
            Gizmos.DrawWireCube(VolumeCenter, VolumeSize);
        }
    }
}