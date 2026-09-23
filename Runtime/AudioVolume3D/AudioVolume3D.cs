using UnityEngine;
using System.Collections.Generic;

namespace TelleR
{
    [AddComponentMenu("TelleR/Audio Volume 3D")]
    public class AudioVolume3D : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [Tooltip("메인 볼륨과 Inner Volume의 씬 뷰 표시 색")]
        public Color ZoneColor = new Color(0f, 1f, 0f, 1f);
        [Tooltip("페이드 영역의 씬 뷰 표시 색")]
        public Color FadeZoneColor = new Color(1f, 0.85f, 0.4f, 1f);
        [Tooltip("Occlusion Zone의 씬 뷰 표시 색")]
        public Color OcclusionZoneColor = new Color(0f, 0.5f, 1f, 1f);

        [Tooltip("씬 뷰에 오브젝트 이름 라벨을 표시합니다")]
        public bool ShowLabel = true;
        [Tooltip("씬 뷰에 메인 볼륨을 표시합니다")]
        public bool ShowMainVolume = true;
        [Tooltip("씬 뷰에 페이드 영역(메인 볼륨 + Fade Distance)을 표시합니다")]
        public bool ShowFadeZone = true;
        [Tooltip("씬 뷰에 Inner Volume을 표시합니다")]
        public bool ShowInnerVolumes = true;
        [Tooltip("씬 뷰에 Occlusion Zone을 표시합니다")]
        public bool ShowOcclusionZones = true;
        [Tooltip("씬 뷰 크기 조절 핸들의 크기 배율")]
        [Range(0.01f, 3f)]
        public float GizmoHandleScale = 1f;

        [Header("Main Volume Settings")]
        [Tooltip("볼륨 중심 (로컬 좌표)")]
        public Vector3 VolumeCenter = Vector3.zero;
        [Tooltip("볼륨 크기 (로컬 단위 — 오브젝트·부모 스케일이 곱해짐). 대상이 이 안에 있으면 최대 음량")]
        public Vector3 VolumeSize = new Vector3(5, 3, 5);
        [Tooltip("볼륨 경계에서 무음이 될 때까지의 거리 (로컬 단위 — 오브젝트·부모 스케일이 곱해짐)")]
        public float FadeDistance = 3.0f;
        [Tooltip("끄면 높이(Y) 차이를 무시하고 수평(XZ) 거리로만 페이드합니다")]
        public bool UseHeightAttenuation = true;
        [Tooltip("볼륨 안에서의 최대 음량")]
        [Range(0f, 1f)] public float MaxVolume = 1.0f;

        [Header("Fade Smoothing")]
        [Tooltip("음량이 커질 때 따라가는 속도 (클수록 빠름)")]
        [Range(0.5f, 10f)] public float FadeInSpeed = 2.0f;
        [Tooltip("음량이 작아질 때 따라가는 속도 (클수록 빠름)")]
        [Range(0.5f, 10f)] public float FadeOutSpeed = 3.0f;
        [Tooltip("켜면 Time.timeScale의 영향을 받지 않습니다. 일시정지(timeScale 0) 중에도 페이드가 멈추지 않습니다")]
        public bool UseUnscaledTime = false;

        [Header("Occlusion Settings")]
        [Tooltip("대상이 이 영역 안에 있으면 음량을 줄이고 고음을 깎습니다. 레이캐스트가 아니라 직접 지정한 영역으로 판정합니다")]
        public List<ManualOcclusionZone> ManualOcclusionZones = new List<ManualOcclusionZone>();
        [Tooltip("차폐 음량·필터가 바뀌는 속도 (클수록 빠름)")]
        [Range(0.5f, 10f)] public float OcclusionSmoothSpeed = 3.0f;

        [Header("Inner Volumes")]
        [Tooltip("실제 소리가 나는 지점. 대상이 가까워지면 발음 위치가 이 영역 쪽으로 끌려갑니다")]
        public List<InnerVolume> InnerVolumes = new List<InnerVolume>();

        [Header("Audio Settings")]
        [Tooltip("재생할 클립. 실행 중에 바꾸면(인스펙터·스크립트) 재생 상태를 유지한 채 교체됩니다. 처음부터 바로 재생하려면 SetClip(clip, true)")]
        public AudioClip Clip;
        [Tooltip("출력 Audio Mixer Group (비우면 기본 출력)")]
        public UnityEngine.Audio.AudioMixerGroup OutputGroup;
        [Tooltip("0 = 2D, 1 = 3D")]
        [Range(0f, 1f)] public float SpatialBlend = 1.0f;
        [Tooltip("볼륨 안으로 들어갈수록 2D로 전환해 소리가 주변을 감싸게 합니다. Inner Volume이 하나라도 있으면 적용되지 않습니다")]
        public bool AutoSpatialBlend = true;
        [Tooltip("AudioSource 3D 감쇠가 시작되는 거리")]
        public float MinDistance = 1.0f;
        [Tooltip("AudioSource 3D 감쇠가 끝나는 거리")]
        public float MaxDistance = 500.0f;
        [Tooltip("클립 반복 재생")]
        public bool Loop = true;
        [Tooltip("켜면 시작 시와 오브젝트가 다시 활성화될 때마다 재생합니다 (범위 밖에서는 일시정지 상태로 대기). AudioSource의 Play On Awake처럼 Stop() 후에도 다시 활성화되면 재생합니다. 끄면 Auto Play On Enter 설정을 따릅니다")]
        public bool PlayOnAwake = true;
        [Tooltip("Play On Awake가 꺼져 있을 때: 켜면 대상이 범위에 처음 들어올 때 재생을 시작하고, 끄면 Play()를 호출할 때까지 재생하지 않습니다")]
        public bool AutoPlayOnEnter = true;

        [Header("Tracking")]
        [Tooltip("스크립트에서 지정하는 추적 대상. 지정하면 태그·리스너보다 우선합니다")]
        [HideInInspector] public Transform TargetTransform;
        [Tooltip("음량 계산 기준이 되는 대상의 태그. 찾지 못하면 Main Camera를 임시 대상으로 쓰고 1초마다 다시 찾습니다")]
        public string TargetTag = "Player";
        [Tooltip("켜면 태그 대신 씬의 활성 AudioListener 위치로 음량을 계산합니다. 3인칭·탑다운처럼 카메라와 플레이어 위치가 다를 때 '범위 안인데 조용함'을 막습니다. 기본값: 꺼짐")]
        public bool UseListenerAsTarget = false;

        public enum VolumeShape { Sphere, Box }

        /// <summary>InnerVolume·ManualOcclusionZone 공통 형상 접근 (에디터 핸들용).</summary>
        public interface ISubVolume
        {
            string Name { get; }
            VolumeShape Shape { get; }
            Vector3 LocalPosition { get; set; }
            Vector3 Size { get; set; }
            float Radius { get; set; }
        }

        [System.Serializable]
        public class InnerVolume : ISubVolume
        {
            [Tooltip("구분용 이름")]
            public string Name = "Source Area";
            [Tooltip("영역 형태")]
            public VolumeShape Shape = VolumeShape.Box;
            [Tooltip("영역 중심 (로컬 좌표)")]
            public Vector3 LocalPosition;
            [Tooltip("Box 크기 (로컬 단위)")]
            public Vector3 Size = new Vector3(2, 2, 2);
            [Tooltip("Sphere 반지름 (로컬 단위)")]
            public float Radius = 2.0f;
            [Tooltip("영역 경계에서 영향이 0이 될 때까지의 거리 (로컬 단위)")]
            public float FalloffDistance = 2.0f;
            [Tooltip("발음 위치를 이 영역 쪽으로 끌어당기는 강도")]
            [Range(0f, 1f)] public float Weight = 1.0f;

            string ISubVolume.Name => Name;
            VolumeShape ISubVolume.Shape => Shape;
            Vector3 ISubVolume.LocalPosition { get => LocalPosition; set => LocalPosition = value; }
            Vector3 ISubVolume.Size { get => Size; set => Size = value; }
            float ISubVolume.Radius { get => Radius; set => Radius = value; }
        }

        [System.Serializable]
        public class ManualOcclusionZone : ISubVolume
        {
            [Tooltip("구분용 이름")]
            public string Name = "Occlusion Area";
            [Tooltip("영역 형태")]
            public VolumeShape Shape = VolumeShape.Box;
            [Tooltip("영역 중심 (로컬 좌표)")]
            public Vector3 LocalPosition;
            [Tooltip("Box 크기 (로컬 단위)")]
            public Vector3 Size = new Vector3(2, 2, 2);
            [Tooltip("Sphere 반지름 (로컬 단위)")]
            public float Radius = 2.0f;
            [Tooltip("대상이 영역 안에 있을 때의 음량 배율")]
            [Range(0f, 1f)] public float TargetVolume = 0.2f;
            [Tooltip("대상이 영역 안에 있을 때의 Low-pass 컷오프 주파수 (Hz)")]
            [Range(10f, 22000f)] public float TargetCutoff = 400f;
            [Tooltip("영역 경계 밖에서 효과가 사라질 때까지의 거리 (로컬 단위)")]
            public float FadeDistance = 1.0f;

            string ISubVolume.Name => Name;
            VolumeShape ISubVolume.Shape => Shape;
            Vector3 ISubVolume.LocalPosition { get => LocalPosition; set => LocalPosition = value; }
            Vector3 ISubVolume.Size { get => Size; set => Size = value; }
            float ISubVolume.Radius { get => Radius; set => Radius = value; }
        }

        private GameObject emitterObject;
        private AudioSource audioSource;
        private AudioLowPassFilter lowPassFilter;
        private Transform selfTransform;
        private Transform emitterTransform;
        private Transform target;
        private AudioListener targetListener;

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
        private const float MIN_FALLOFF = 0.0001f;

        private bool isInitialized;
        private bool hasStartedPlayback;   // 현재 클립이 Play된 적 있음 (일시정지는 UnPause로 재개)
        private bool playRequested;        // Play() 또는 PlayOnAwake로 재생이 요청됨
        private bool stopHeld;             // Stop() 호출 또는 끝난 원샷 클립 교체 — Play() 전까지 자동 재생 금지
        private bool pausedByVolume;       // 음량 0이라 이 컴포넌트가 Pause함 (클립이 스스로 끝난 정지와 구분)
        private bool targetIsFallback;
        private bool targetIsListener;
        private bool retargetRequested;
        private string failedTag;          // 미정의로 판명된 태그 — TargetTag가 바뀌면 다시 찾는다
        private float nextTargetSearchTime;
        // InitializeTarget이 마지막으로 반영한 추적 옵션 — 스크립트가 실행 중에 바꾸면 Update에서 감지해 재탐색
        private bool appliedUseListenerAsTarget;
        private string appliedTargetTag;

        // ApplySourceSettings가 마지막으로 반영한 값 — 스크립트가 실행 중 공개 필드를 바꾸면
        // Update에서 관리 코드 비교만으로(네이티브 호출 없이) 감지해 AudioSource에 다시 반영한다
        private AudioClip appliedClip;
        private UnityEngine.Audio.AudioMixerGroup appliedOutputGroup;
        private float appliedMinDistance;
        private float appliedMaxDistance;
        private bool appliedLoop;

        private float DeltaTime => UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

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
            if (!Application.isPlaying) return;
            CacheVolumes();
            ApplySourceSettings();
            retargetRequested = true; // 추적 옵션(태그·리스너) 변경 즉시 반영
            failedTag = null;         // 실행 중 Tag Manager에 태그를 추가한 경우도 다시 시도
        }

        private void OnEnable()
        {
            if (audioSource && !audioSource.isPlaying && PlayOnAwake)
                Play();
        }

        /// <summary>재생을 시작(또는 재개)한다. Stop() 이후 자동 재생 금지도 해제된다.</summary>
        public void Play()
        {
            stopHeld = false;
            playRequested = true;
            pausedByVolume = false;
            // 비활성 상태의 AudioSource는 Play가 무시됨 ("Can not play a disabled audio source").
            // 요청만 남겨 두면 다시 켜진 뒤 Update(ApplyAudioValues)가 Play로 시작한다
            if (!audioSource || !isActiveAndEnabled) return;
            audioSource.Play();
            hasStartedPlayback = true;
        }

        /// <summary>
        /// 재생을 멈춘다. 대상이 범위 안에 있어도 Play()를 다시 호출할 때까지 재생하지 않는다.
        /// 단, PlayOnAwake가 켜져 있으면 오브젝트를 비활성화했다가 다시 켤 때(OnEnable)
        /// AudioSource.playOnAwake와 같은 규칙으로 처음부터 다시 재생한다.
        /// </summary>
        public void Stop()
        {
            stopHeld = true;
            playRequested = false;
            pausedByVolume = false;
            if (!audioSource) return;
            audioSource.Stop();
            hasStartedPlayback = false;
        }

        /// <summary>
        /// 실행 중 클립을 교체한다. play가 true면 즉시 처음부터 재생하고,
        /// false면 재생 상태를 유지한다 (재생 중이었으면 새 클립으로 이어서 재생, 멈춰 있었으면 멈춘 채 유지).
        /// 루프가 아닌 클립이 스스로 끝난 상태도 '멈춤'으로 본다 — 새 클립은 Play() 전까지 재생하지 않는다.
        /// 범위 밖이라 일시정지된 상태는 '재생 중'으로 본다 (범위에 들어오면 새 클립으로 재생).
        /// play가 false이고 이미 같은 클립이면 아무것도 하지 않는다 (재생 위치 유지).
        /// </summary>
        public void SetClip(AudioClip clip, bool play = false)
        {
            Clip = clip;
            appliedClip = clip;
            if (!audioSource) return;
            // 같은 클립을 반복 지정(상태 변경마다·매 프레임)해도 처음부터 다시 시작하며 끊기지 않도록
            if (!play && audioSource.clip == clip) return;

            // 루프가 아닌 클립이 스스로 끝나 멈춘 상태: 끝난 소스는 timeSamples가 0으로 돌아가고,
            // 이 컴포넌트의 Pause·AudioListener.pause는 재생 위치를 유지하거나 pausedByVolume으로 구분된다.
            // 클립 없이 Play된 상태(AddComponent 직후 Clip 지정)는 끝난 것이 아니라 재생 대기로 본다
            bool finished = hasStartedPlayback && audioSource.clip != null && !audioSource.isPlaying
                            && !pausedByVolume && audioSource.timeSamples == 0;
            bool wasActive = hasStartedPlayback && !stopHeld && !finished;
            audioSource.Stop();
            audioSource.clip = clip;
            hasStartedPlayback = false;
            pausedByVolume = false;

            if (play) Play();
            else if (wasActive)
            {
                if (isActiveAndEnabled)
                {
                    audioSource.Play();
                    hasStartedPlayback = true;
                }
                else playRequested = true; // 비활성 중이면 다시 켜질 때 새 클립으로 시작
            }
            else if (finished)
            {
                // 끝난 원샷을 교체하면 새 클립은 멈춘 채 대기 — 범위 진입·Play 요청으로 자동 시작하지 않는다
                stopHeld = true;
                playRequested = false;
            }
        }

        private void OnDisable()
        {
            if (audioSource) audioSource.Stop();
            // 정지된 소스는 UnPause로 재개되지 않음 — 다시 켜질 때 Play부터 하도록 초기화
            hasStartedPlayback = false;
            pausedByVolume = false;
        }

        private void OnDestroy()
        {
            if (emitterObject) Destroy(emitterObject);
        }

        private void InitializeTarget()
        {
            retargetRequested = false;
            targetIsListener = false;
            appliedUseListenerAsTarget = UseListenerAsTarget;
            appliedTargetTag = TargetTag;

            if (TargetTransform != null)
            {
                target = TargetTransform;
                targetIsFallback = false;
                return;
            }

            if (UseListenerAsTarget)
            {
                targetListener = FindActiveListener();
                if (targetListener)
                {
                    target = targetListener.transform;
                    targetIsFallback = false;
                    targetIsListener = true;
                    return;
                }
            }

            if (!string.IsNullOrEmpty(TargetTag) && TargetTag != failedTag)
            {
                try
                {
                    var go = GameObject.FindGameObjectWithTag(TargetTag);
                    if (go)
                    {
                        target = go.transform;
                        // 리스너 모드인데 리스너가 없으면 태그 대상은 임시 — 리스너가 생기면 교체
                        targetIsFallback = UseListenerAsTarget;
                        return;
                    }
                }
                catch (UnityException)
                {
                    failedTag = TargetTag; // 태그 미정의 — 같은 태그로 반복 예외 방지
                }
            }

            // 카메라는 임시 폴백 — 이후 태그 대상이 스폰되면 교체된다
            Camera cam = Camera.main;
            if (cam)
            {
                target = cam.transform;
                targetIsFallback = true;
            }
            else if (target != null)
            {
                targetIsFallback = true;
            }
        }

        private static AudioListener FindActiveListener()
        {
#if UNITY_6000_6_OR_NEWER
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
#elif UNITY_2022_2_OR_NEWER
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
#pragma warning disable CS0618 // 2022.2 미만에는 FindObjectsByType이 없음
            AudioListener[] listeners = FindObjectsOfType<AudioListener>();
#pragma warning restore CS0618
#endif
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i].isActiveAndEnabled) return listeners[i];
            }
            return null;
        }

        private void CreateVirtualEmitter()
        {
            emitterObject = new GameObject($"{gameObject.name}_Emitter");
            emitterObject.transform.SetParent(selfTransform);
            emitterObject.transform.localPosition = Vector3.zero;
            emitterTransform = emitterObject.transform;

            audioSource = emitterObject.AddComponent<AudioSource>();
            audioSource.clip = Clip;
            audioSource.spatialBlend = SpatialBlend;
            audioSource.playOnAwake = false;
            audioSource.dopplerLevel = 0f;
            // 첫 Update의 페이드 계산 전까지 무음 — 기본 volume(1)로 시작하면 씬 시작 시
            // 원거리 볼륨이 최대 음량으로 터진 뒤 서서히 잦아드는 소리 사고가 남
            audioSource.volume = 0f;
            ApplySourceSettings();

            lowPassFilter = emitterObject.AddComponent<AudioLowPassFilter>();
            lowPassFilter.cutoffFrequency = DEFAULT_CUTOFF;

            if (PlayOnAwake)
                Play();
        }

        /// <summary>인스펙터 값을 AudioSource에 반영한다. 클립이 바뀌었으면 재생 상태를 유지한 채 교체한다.</summary>
        private void ApplySourceSettings()
        {
            if (!audioSource) return;
            if (audioSource.clip != Clip) SetClip(Clip);
            audioSource.outputAudioMixerGroup = OutputGroup;
            audioSource.minDistance = MinDistance;
            audioSource.maxDistance = MaxDistance;
            audioSource.loop = Loop;

            appliedClip = Clip;
            appliedOutputGroup = OutputGroup;
            appliedMinDistance = MinDistance;
            appliedMaxDistance = MaxDistance;
            appliedLoop = Loop;
        }

        /// <summary>마지막 반영 이후 Clip·OutputGroup·Min/MaxDistance·Loop가 스크립트에서 바뀌었는지 (빌드에서도 동작).</summary>
        private bool SourceSettingsChanged()
        {
            // ReferenceEquals: UnityEngine.Object의 == 는 네이티브 생존 확인을 거치므로 매 프레임 비교에는 참조 비교를 쓴다
            return !ReferenceEquals(Clip, appliedClip)
                || !ReferenceEquals(OutputGroup, appliedOutputGroup)
                || MinDistance != appliedMinDistance
                || MaxDistance != appliedMaxDistance
                || Loop != appliedLoop;
        }

        private void CacheVolumes()
        {
            innerVolumeCache = InnerVolumes != null ? InnerVolumes.ToArray() : null;
            occlusionZoneCache = ManualOcclusionZones != null ? ManualOcclusionZones.ToArray() : null;
        }

        private void Update()
        {
            if (!audioSource) return;

            // 스크립트가 실행 중에 바꾼 AudioSource 설정 반영 (OnValidate는 에디터 전용이라 빌드에서는 여기서만 반영됨)
            if (SourceSettingsChanged()) ApplySourceSettings();

            // 스크립트가 실행 중에 TargetTransform을 바꾸면 즉시 반영
            if (TargetTransform != null && target != TargetTransform)
                InitializeTarget();

            // 스크립트가 실행 중에 추적 옵션(리스너 사용·태그)을 바꾸면 재탐색 (OnValidate는 에디터 전용)
            if (UseListenerAsTarget != appliedUseListenerAsTarget || TargetTag != appliedTargetTag)
            {
                retargetRequested = true;
                failedTag = null;
            }

            // 타깃이 없거나 임시 폴백·리스너 소실 상태면 주기적으로 재탐색 — 씬 로드 후 스폰되는 플레이어 대응
            bool listenerLost = targetIsListener && (!targetListener || !targetListener.isActiveAndEnabled);
            if (retargetRequested ||
                ((target == null || targetIsFallback || listenerLost) && Time.unscaledTime >= nextTargetSearchTime))
            {
                nextTargetSearchTime = Time.unscaledTime + 1f;
                InitializeTarget();
            }
            if (target == null) return;

            // 판정은 전부 로컬 공간 — VolumeSize·FadeDistance가 로컬 단위라 스케일된 부모에서도 일치
            Vector3 targetLocalPos = selfTransform.InverseTransformPoint(target.position);
            Vector3 fromCenter = targetLocalPos - VolumeCenter;
            float maxDim;
            if (UseHeightAttenuation)
            {
                maxDim = Mathf.Max(VolumeSize.x, Mathf.Max(VolumeSize.y, VolumeSize.z));
            }
            else
            {
                fromCenter.y = 0f; // 높이 무시 모드에서는 원거리 판정도 수평 거리만
                maxDim = Mathf.Max(VolumeSize.x, VolumeSize.z);
            }
            float activeRange = (maxDim * 0.5f) + FadeDistance;
            float activeRangeSqr = activeRange * activeRange;

            if (fromCenter.sqrMagnitude > activeRangeSqr * 4.0f)
            {
                if (audioSource.volume > 0.001f)
                    audioSource.volume = Mathf.Lerp(audioSource.volume, 0f, DeltaTime * 5f);
                else if (audioSource.isPlaying)
                {
                    audioSource.Pause();
                    pausedByVolume = true;
                }
                return;
            }

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
                    if (vol == null) continue;
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
            currentFadeFactor = Mathf.Lerp(currentFadeFactor, targetFade, DeltaTime * fadeSpeed);
        }

        private void UpdateOcclusionTarget(Vector3 targetLocalPos)
        {
            if (isInitialized)
            {
                occlusionCheckTimer += DeltaTime;
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
                    if (zone == null) continue;
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
            float dt = DeltaTime;
            if (isInitialized)
            {
                float smoothDelta = dt * OcclusionSmoothSpeed;
                currentOcclusionVol = Mathf.Lerp(currentOcclusionVol, targetOcclusionVol, smoothDelta);
                currentOcclusionCutoff = Mathf.Lerp(currentOcclusionCutoff, targetOcclusionCutoff, smoothDelta);
            }

            float targetSpatialBlend = SpatialBlend;
            // Inner Volume이 있으면 발음 위치가 의미를 가지므로 3D를 유지한다
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
                audioSource.spatialBlend = Mathf.Lerp(audioSource.spatialBlend, targetSpatialBlend, dt * 2f);
                float volSpeed = finalVol > audioSource.volume ? FadeInSpeed : FadeOutSpeed;
                audioSource.volume = Mathf.Lerp(audioSource.volume, finalVol, dt * volSpeed);
                lowPassFilter.cutoffFrequency = Mathf.Lerp(lowPassFilter.cutoffFrequency, currentOcclusionCutoff, dt * OcclusionSmoothSpeed);
            }

            if (audioSource.volume < 0.001f)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Pause();
                    pausedByVolume = true;
                }
            }
            else if (!audioSource.isPlaying && !stopHeld)
            {
                // 일시정지된 소스는 UnPause로 재개. 한 번도 Play되지 않은 소스는 UnPause로 시작되지 않으므로
                // Play 요청이 있었거나 Auto Play On Enter(PlayOnAwake 꺼짐 시 기존 동작)일 때만 Play로 시작
                if (hasStartedPlayback)
                {
                    audioSource.UnPause();
                    pausedByVolume = false;
                }
                else if (playRequested || AutoPlayOnEnter)
                {
                    audioSource.Play();
                    hasStartedPlayback = true;
                    pausedByVolume = false;
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
            // FalloffDistance 0이면 0/0 = NaN으로 영역 안에서도 무시되던 문제 방지
            float t = 1.0f - Mathf.Clamp01(dist / Mathf.Max(MIN_FALLOFF, vol.FalloffDistance));
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
