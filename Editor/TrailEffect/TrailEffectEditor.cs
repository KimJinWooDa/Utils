using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace TelleR
{
    [CustomEditor(typeof(TrailEffect))]
    [CanEditMultipleObjects]
    public class TrailEffectEditor : Editor
    {
        const string LogPrefix = "[TelleR/TrailEffect] ";

        SerializedProperty profile, mode, colorMode;
        SerializedProperty trailColor, colorOverLifetime, duration, snapshotsPerSecond;
        SerializedProperty scaleStart, scaleEnd;
        SerializedProperty fresnelPower, fresnelIntensity;
        SerializedProperty stampTexture, stampSizeStart, stampSizeEnd, stampStyle, stampCount, stampFollowSpeed, stampSpacing;
        SerializedProperty preventOverlap;
        SerializedProperty maxSnapshots, minDistance;
        SerializedProperty useUnscaledTime;
        SerializedProperty trailMaterial;
        SerializedProperty targetMeshFilter, searchRoot;

        Dictionary<string, SerializedProperty> overrideCache;
        SerializedObject cachedProfileSO;
        TrailEffectProfile cachedProfileRef;

        // 대상 메쉬 요약 (Layout 이벤트에서만 갱신, 리스트 재사용, 개수가 바뀔 때만 문자열을 다시 만든다)
        readonly List<MeshFilter> scanFilters = new List<MeshFilter>();
        readonly List<SkinnedMeshRenderer> scanSkinned = new List<SkinnedMeshRenderer>();
        int scanFilterCount = -1, scanSkinnedCount = -1, scanEmptyTargets = -1, scanTargetCount = -1;
        string scanSummary;
        MessageType scanSummaryType;

        // 읽기 전용 머티리얼 판정 캐시 (문제가 있는 머티리얼에 대해서만 계산)
        Material readOnlyCheckedMat;
        bool readOnlyCheckedValue;

        // ─── Labels / tooltips (캐시) ───
        static readonly GUIContent ProfileContent = new GUIContent("Profile", "여러 오브젝트가 같은 트레일 설정을 공유할 때 쓰는 에셋. 비우면 이 컴포넌트 값만 사용합니다.");
        static readonly GUIContent CreateProfileContent = new GUIContent("Create", "현재 값으로 새 Trail Profile 에셋을 만들어 연결합니다.");
        static readonly GUIContent CreateProfileMultiContent = new GUIContent("Create", "여러 오브젝트를 선택한 상태에서는 Profile을 만들 수 없습니다. 하나만 선택하세요.");
        static readonly GUIContent OverrideToggleTip = new GUIContent(string.Empty, "체크: 이 컴포넌트 값 사용\n해제: Profile 값 사용");

        static readonly GUIContent ModeContent = new GUIContent("Mode", "Color: 대상 메쉬 모양 그대로 잔상을 남깁니다.\nTexture Stamp: 카메라를 향하는 텍스쳐 빌보드를 남깁니다.");
        static readonly GUIContent ColorModeContent = new GUIContent("Color Mode", "Solid Color: 단색 (알파가 기본 투명도)\nGradient: 수명에 따라 색·투명도가 바뀝니다.");
        static readonly GUIContent TrailColorContent = new GUIContent("Trail Color", "잔상 기본색. 알파로 기본 투명도를 조절합니다.");
        static readonly GUIContent ColorOverLifetimeContent = new GUIContent("Color Over Lifetime", "수명(왼쪽=생성, 오른쪽=소멸)에 따른 색·투명도 변화");
        static readonly GUIContent ScaleStartContent = new GUIContent("Scale Start", "잔상이 생길 때의 배율 (1 = 원본 크기)");
        static readonly GUIContent ScaleEndContent = new GUIContent("Scale End", "잔상이 사라질 때의 배율 (0.5 = 줄어들며 소멸, 1.5 = 커지며 소멸)");
        static readonly GUIContent FresnelIntensityContent = new GUIContent("Intensity", "가장자리 발광 세기 (0 = 끔, 0.5 = 은은한 림라이트, 1.5 = 강한 네온)");
        static readonly GUIContent FresnelPowerContent = new GUIContent("Power", "발광 선명도. 값이 클수록 가장자리가 얇고 날카로워집니다.");
        static readonly GUIContent StampTextureContent = new GUIContent("Texture", "빌보드로 찍을 알파 텍스쳐 (원형·별·하트 등). 비우면 원형으로 그립니다.");
        static readonly GUIContent StampSizeStartContent = new GUIContent("Size Start", "Follow: 첫 번째 스탬프 크기\nTrail: 생성 직후 크기");
        static readonly GUIContent StampSizeEndContent = new GUIContent("Size End", "Follow: 마지막 스탬프 크기\nTrail: 소멸 직전 크기");
        static readonly GUIContent StampStyleContent = new GUIContent("Style", "Follow: 대상 뒤를 따라다니는 스탬프 체인\nTrail: 지나간 자리에 남아 서서히 사라지는 스탬프");
        static readonly GUIContent StampCountContent = new GUIContent("Count", "따라다니는 스탬프 개수 (3 = 가벼운 효과, 8 = 긴 꼬리)");
        static readonly GUIContent StampFollowSpeedContent = new GUIContent("Follow Speed", "스탬프가 대상을 따라가는 속도 (3 = 느긋하게, 20 = 빠르게 추적)");
        static readonly GUIContent StampSpacingContent = new GUIContent("Spacing", "스탬프 사이 최소 간격 (m)");
        static readonly GUIContent DurationContent = new GUIContent("Duration", "잔상 수명 (초)");
        static readonly GUIContent SnapshotsPerSecondContent = new GUIContent("Snapshots/Sec", "초당 잔상 생성 수. 높을수록 촘촘하지만 그리는 양이 늘어납니다.");
        static readonly GUIContent PreventOverlapContent = new GUIContent("Prevent Overlap", "켜면 같은 트레일의 잔상끼리 겹쳐도 진해지지 않습니다 (스텐실 사용).");
        static readonly GUIContent MaxSnapshotsContent = new GUIContent("Max Snapshots", "동시에 유지하는 최대 잔상 수 (버퍼 크기). Duration × Snapshots/Sec 이상이어야 꼬리가 잘리지 않습니다.");
        static readonly GUIContent MinDistanceContent = new GUIContent("Min Distance", "이 거리(m)보다 적게 움직이면 잔상을 만들지 않습니다.");
        static readonly GUIContent UseUnscaledTimeContent = new GUIContent("Use Unscaled Time", "켜면 Time.timeScale의 영향을 받지 않습니다 (일시정지·슬로모션 중에도 잔상 유지).");
        static readonly GUIContent TrailMaterialContent = new GUIContent("Trail Material", "'TelleR/Trail' 셰이더 머티리얼 (GPU Instancing 필요). 비우면 Profile의 Trail Material을 씁니다.");
        static readonly GUIContent UseDefaultMaterialContent = new GUIContent("Use Default", "패키지에 포함된 기본 Trail 머티리얼을 연결합니다.");
        static readonly GUIContent CreateMaterialContent = new GUIContent("Create Material", "프로젝트에 새 Trail 머티리얼 에셋을 만들어 연결합니다.");
        static readonly GUIContent UseProfileMaterialContent = new GUIContent("Use Profile Material", "이 컴포넌트의 Trail Material을 비워 Profile의 Trail Material을 쓰게 합니다.");
        static readonly GUIContent ChangeShaderContent = new GUIContent("Change Shader...", "이 머티리얼 에셋의 셰이더를 'TelleR/Trail'로 바꿉니다. 이 머티리얼을 쓰는 모든 오브젝트에 적용되므로 확인 창이 뜹니다.");
        static readonly GUIContent EnableInstancingContent = new GUIContent("Enable GPU Instancing", "이 머티리얼의 GPU Instancing을 켭니다 (보이는 모습은 바뀌지 않습니다).");
        static readonly GUIContent TargetMeshFilterContent = new GUIContent("Target Mesh Filter", "지정하면 이 MeshFilter 하나만 잔상으로 복제합니다 (MeshRenderer가 없어도 됨).\n비우면 Search Root 아래의 메쉬를 모두 씁니다.");
        static readonly GUIContent SearchRootContent = new GUIContent("Search Root", "이 Transform 아래의 MeshFilter·SkinnedMeshRenderer를 잔상 대상으로 모읍니다 (최대 " + TrailEffect.MaxParts + "개).\n비우면 이 오브젝트 아래를 찾습니다. Target Mesh Filter를 지정하면 쓰지 않습니다.\nTexture Stamp 모드에서는 첫 번째 대상의 위치를 따라갑니다.");

        // Debug Motion 버튼 (OnGUI마다 배열·문자열을 만들지 않도록 캐시)
        static readonly DebugMotionPattern[] MotionPatterns = (DebugMotionPattern[])System.Enum.GetValues(typeof(DebugMotionPattern));
        static readonly GUIContent[] MotionPatternContents = BuildMotionPatternContents();

        static GUIContent[] BuildMotionPatternContents()
        {
            var contents = new GUIContent[MotionPatterns.Length];
            for (int i = 0; i < MotionPatterns.Length; i++)
                contents[i] = new GUIContent("▶ " + MotionPatterns[i], "Play Mode 동안 이 오브젝트를 '" + MotionPatterns[i] + "' 패턴으로 움직입니다.");
            return contents;
        }

        void OnEnable()
        {
            profile = serializedObject.FindProperty("Profile");
            mode = serializedObject.FindProperty("Mode");
            colorMode = serializedObject.FindProperty("ColorMode");
            trailColor = serializedObject.FindProperty("TrailColor");
            colorOverLifetime = serializedObject.FindProperty("ColorOverLifetime");
            duration = serializedObject.FindProperty("Duration");
            snapshotsPerSecond = serializedObject.FindProperty("SnapshotsPerSecond");
            scaleStart = serializedObject.FindProperty("ScaleStart");
            scaleEnd = serializedObject.FindProperty("ScaleEnd");
            fresnelPower = serializedObject.FindProperty("FresnelPower");
            fresnelIntensity = serializedObject.FindProperty("FresnelIntensity");
            stampTexture = serializedObject.FindProperty("StampTexture");
            stampSizeStart = serializedObject.FindProperty("StampSizeStart");
            stampSizeEnd = serializedObject.FindProperty("StampSizeEnd");
            stampStyle = serializedObject.FindProperty("StampStyle");
            stampCount = serializedObject.FindProperty("StampCount");
            stampFollowSpeed = serializedObject.FindProperty("StampFollowSpeed");
            stampSpacing = serializedObject.FindProperty("StampSpacing");
            preventOverlap = serializedObject.FindProperty("PreventOverlap");
            maxSnapshots = serializedObject.FindProperty("MaxSnapshots");
            minDistance = serializedObject.FindProperty("MinDistance");
            useUnscaledTime = serializedObject.FindProperty("UseUnscaledTime");
            trailMaterial = serializedObject.FindProperty("trailMaterial");
            targetMeshFilter = serializedObject.FindProperty("TargetMeshFilter");
            searchRoot = serializedObject.FindProperty("SearchRoot");

            overrideCache = new Dictionary<string, SerializedProperty>();
            string[] flags =
            {
                "overrideMode", "overrideColorMode", "overrideTrailColor", "overrideColorOverLifetime",
                "overrideDuration", "overrideSnapshotsPerSecond", "overrideScaleStart", "overrideScaleEnd",
                "overrideFresnelPower", "overrideFresnelIntensity", "overrideStampTexture",
                "overrideStampSizeStart", "overrideStampSizeEnd", "overrideStampStyle",
                "overrideStampCount", "overrideStampFollowSpeed", "overrideStampSpacing",
                "overridePreventOverlap", "overrideMaxSnapshots", "overrideMinDistance"
            };
            foreach (var f in flags)
                overrideCache[f] = serializedObject.FindProperty(f);
        }

        SerializedObject GetProfileSO()
        {
            // 선택한 오브젝트들의 Profile이 서로 다르면 어느 값을 보여줄지 정할 수 없으므로 로컬 값만 표시한다
            if (profile.hasMultipleDifferentValues) return null;

            var profileRef = (TrailEffectProfile)profile.objectReferenceValue;
            if (profileRef == null) return null;

            if (cachedProfileRef != profileRef || cachedProfileSO == null)
            {
                cachedProfileSO = new SerializedObject(profileRef);
                cachedProfileRef = profileRef;
            }
            cachedProfileSO.Update();
            return cachedProfileSO;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var fx = (TrailEffect)target;
            bool multi = targets.Length > 1;
            bool targetsChanged = false;

            // ─── Profile ───
            TelleRGUI.Section("Profile");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(profile, ProfileContent);
            using (new EditorGUI.DisabledScope(multi))
            {
                if (GUILayout.Button(multi ? CreateProfileMultiContent : CreateProfileContent, GUILayout.Width(56)))
                    CreateProfile(fx);
            }
            EditorGUILayout.EndHorizontal();

            SerializedObject profileSO = GetProfileSO();
            bool hasProfile = profileSO != null;

            if (profile.hasMultipleDifferentValues)
                EditorGUILayout.HelpBox("선택한 오브젝트의 Profile이 서로 달라 이 컴포넌트 값만 표시합니다.", MessageType.Info);
            else if (hasProfile)
                EditorGUILayout.HelpBox("왼쪽 체크박스를 켠 항목은 이 컴포넌트 값을, 끈 항목은 Profile 값을 사용합니다.", MessageType.Info);

            // ─── Mode ───
            TelleRGUI.Section("Mode");
            Field(hasProfile, profileSO, mode, "Mode", "overrideMode", ModeContent);

            TrailMode currentMode = ResolveEnum<TrailMode>(hasProfile, profileSO, mode, "Mode", "overrideMode");
            bool isColor = currentMode == TrailMode.Color;
            bool isStamp = currentMode == TrailMode.TextureStamp;

            StampStyle currentStyle = StampStyle.Follow;
            if (isStamp)
                currentStyle = ResolveEnum<StampStyle>(hasProfile, profileSO, stampStyle, "StampStyle", "overrideStampStyle");

            bool isStampFollow = isStamp && currentStyle == StampStyle.Follow;
            bool isStampTrail = isStamp && currentStyle == StampStyle.Trail;
            bool usesSnapshots = isColor || isStampTrail;

            // ─── Target ───
            TelleRGUI.Section("Target");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(targetMeshFilter, TargetMeshFilterContent);
            using (new EditorGUI.DisabledScope(!targetMeshFilter.hasMultipleDifferentValues && targetMeshFilter.objectReferenceValue != null))
                EditorGUILayout.PropertyField(searchRoot, SearchRootContent);
            targetsChanged = EditorGUI.EndChangeCheck();

            if (isColor) DrawTargetSummary();

            // ─── Appearance ───
            TelleRGUI.Section("Appearance");
            Field(hasProfile, profileSO, colorMode, "ColorMode", "overrideColorMode", ColorModeContent);

            TrailColorMode currentColorMode = ResolveEnum<TrailColorMode>(hasProfile, profileSO, colorMode, "ColorMode", "overrideColorMode");

            if (currentColorMode == TrailColorMode.SolidColor)
                Field(hasProfile, profileSO, trailColor, "TrailColor", "overrideTrailColor", TrailColorContent);
            else
                Field(hasProfile, profileSO, colorOverLifetime, "ColorOverLifetime", "overrideColorOverLifetime", ColorOverLifetimeContent);

            if (isColor)
            {
                Field(hasProfile, profileSO, scaleStart, "ScaleStart", "overrideScaleStart", ScaleStartContent);
                Field(hasProfile, profileSO, scaleEnd, "ScaleEnd", "overrideScaleEnd", ScaleEndContent);

                TelleRGUI.Section("Fresnel");
                Field(hasProfile, profileSO, fresnelIntensity, "FresnelIntensity", "overrideFresnelIntensity", FresnelIntensityContent);

                float fVal = ResolveFloat(hasProfile, profileSO, fresnelIntensity, "FresnelIntensity", "overrideFresnelIntensity");
                if (fVal > 0.001f)
                    Field(hasProfile, profileSO, fresnelPower, "FresnelPower", "overrideFresnelPower", FresnelPowerContent);
            }

            if (isStamp)
            {
                TelleRGUI.Section("Texture Stamp");
                Field(hasProfile, profileSO, stampTexture, "StampTexture", "overrideStampTexture", StampTextureContent);
                Field(hasProfile, profileSO, stampSizeStart, "StampSizeStart", "overrideStampSizeStart", StampSizeStartContent);
                Field(hasProfile, profileSO, stampSizeEnd, "StampSizeEnd", "overrideStampSizeEnd", StampSizeEndContent);
                Field(hasProfile, profileSO, stampStyle, "StampStyle", "overrideStampStyle", StampStyleContent);

                if (isStampFollow)
                {
                    Field(hasProfile, profileSO, stampCount, "StampCount", "overrideStampCount", StampCountContent);
                    Field(hasProfile, profileSO, stampFollowSpeed, "StampFollowSpeed", "overrideStampFollowSpeed", StampFollowSpeedContent);
                    Field(hasProfile, profileSO, stampSpacing, "StampSpacing", "overrideStampSpacing", StampSpacingContent);
                }
            }

            // ─── Performance ───
            if (usesSnapshots)
            {
                TelleRGUI.Section("Performance");
                Field(hasProfile, profileSO, duration, "Duration", "overrideDuration", DurationContent);
                Field(hasProfile, profileSO, snapshotsPerSecond, "SnapshotsPerSecond", "overrideSnapshotsPerSecond", SnapshotsPerSecondContent);
                Field(hasProfile, profileSO, preventOverlap, "PreventOverlap", "overridePreventOverlap", PreventOverlapContent);
                Field(hasProfile, profileSO, maxSnapshots, "MaxSnapshots", "overrideMaxSnapshots", MaxSnapshotsContent);
                Field(hasProfile, profileSO, minDistance, "MinDistance", "overrideMinDistance", MinDistanceContent);
                DrawTruncationWarning(hasProfile, profileSO);
            }

            // ─── Time ───
            TelleRGUI.Section("Time");
            EditorGUILayout.PropertyField(useUnscaledTime, UseUnscaledTimeContent);

            // ─── Material ───
            TelleRGUI.Section("Material");
            EditorGUILayout.PropertyField(trailMaterial, TrailMaterialContent);
            DrawMaterialValidation(hasProfile);

            serializedObject.ApplyModifiedProperties();

            // 플레이 중에 대상을 바꾸면 바로 다시 수집한다 (편집 모드에서는 Play 시작 때 수집)
            if (targetsChanged && Application.isPlaying)
            {
                foreach (var t in targets)
                    ((TrailEffect)t).RefreshTargets();
            }

            // ─── Runtime / Debug Motion ───
            if (Application.isPlaying && !AllTargetsInPlayingScene())
            {
                // 프리팹 에셋·Prefab Mode 오브젝트에 오실레이터를 붙이거나 Pause를 기록하면 에셋에 그대로 저장돼 빌드까지 따라간다
                TelleRGUI.Section("Runtime");
                EditorGUILayout.HelpBox(
                    "Runtime·Debug Motion 조작은 플레이 중인 씬의 오브젝트에만 쓸 수 있습니다. " +
                    "프리팹 에셋이나 Prefab Mode 오브젝트를 바꾸면 에셋에 그대로 저장되므로, Hierarchy에서 씬 인스턴스를 선택하세요.",
                    MessageType.Info);
                return;
            }

            if (Application.isPlaying)
                DrawRuntimeControls();

            DrawDebugMotion();
        }

        // 모든 대상이 로드된 씬의 인스턴스인지 (프로젝트 창의 프리팹 에셋·Prefab Mode 프리뷰 씬 제외)
        bool AllTargetsInPlayingScene()
        {
            foreach (var t in targets)
            {
                if (t == null || EditorUtility.IsPersistent(t)) return false;
                var go = ((Component)t).gameObject;
                if (!go.scene.IsValid() || EditorSceneManager.IsPreviewScene(go.scene)) return false;
            }
            return true;
        }

        // ═════════════════════════════════════════════
        //  Sections
        // ═════════════════════════════════════════════

        void DrawTargetSummary()
        {
            if (Event.current.type == EventType.Layout)
            {
                int filterCount = 0, skinnedCount = 0, empty = 0;
                foreach (var t in targets)
                {
                    ((TrailEffect)t).GetTargets(scanFilters, scanSkinned);
                    if (scanFilters.Count + scanSkinned.Count == 0) empty++;
                    filterCount += scanFilters.Count;
                    skinnedCount += scanSkinned.Count;
                }
                scanFilters.Clear();
                scanSkinned.Clear();

                if (filterCount != scanFilterCount || skinnedCount != scanSkinnedCount ||
                    empty != scanEmptyTargets || targets.Length != scanTargetCount)
                {
                    scanFilterCount = filterCount;
                    scanSkinnedCount = skinnedCount;
                    scanEmptyTargets = empty;
                    scanTargetCount = targets.Length;
                    BuildTargetSummary();
                }
            }

            if (scanSummary != null)
                EditorGUILayout.HelpBox(scanSummary, scanSummaryType);
        }

        void BuildTargetSummary()
        {
            if (scanTargetCount > 1)
            {
                // 여러 개 선택: 개수 합계는 의미가 없으므로 대상이 없는 오브젝트만 알린다
                scanSummary = scanEmptyTargets > 0
                    ? $"선택한 {scanTargetCount}개 중 {scanEmptyTargets}개는 잔상으로 복제할 MeshFilter/SkinnedMeshRenderer가 없어 Color 모드에서 아무것도 그리지 않습니다."
                    : null;
                scanSummaryType = MessageType.Warning;
                return;
            }

            int total = scanFilterCount + scanSkinnedCount;
            if (total == 0)
            {
                scanSummary = "잔상으로 복제할 MeshFilter/SkinnedMeshRenderer가 없어 Color 모드는 아무것도 그리지 않습니다. 대상 메쉬를 자식으로 두거나 Search Root/Target Mesh Filter를 지정하세요.";
                scanSummaryType = MessageType.Warning;
                return;
            }

            scanSummary = $"대상: 메쉬 {scanFilterCount}개, 스킨드 메쉬 {scanSkinnedCount}개";
            scanSummaryType = MessageType.None;

            if (scanSkinnedCount > 0)
            {
                scanSummary += "\n스킨드 메쉬는 잔상마다 메쉬를 구워(BakeMesh) 그리므로 정적 메쉬보다 CPU·메모리 비용이 큽니다.";
                scanSummaryType = MessageType.Info;
            }

            if (total > TrailEffect.MaxParts)
            {
                scanSummary += $"\n앞의 {TrailEffect.MaxParts}개만 잔상으로 그립니다. Search Root 또는 Target Mesh Filter로 대상을 좁히세요.";
                scanSummaryType = MessageType.Warning;
            }
        }

        void DrawTruncationWarning(bool hasProfile, SerializedObject profileSO)
        {
            float dur = ResolveFloat(hasProfile, profileSO, duration, "Duration", "overrideDuration");
            int sps = ResolveInt(hasProfile, profileSO, snapshotsPerSecond, "SnapshotsPerSecond", "overrideSnapshotsPerSecond");
            int max = ResolveInt(hasProfile, profileSO, maxSnapshots, "MaxSnapshots", "overrideMaxSnapshots");
            int needed = Mathf.CeilToInt(dur * sps);
            if (needed <= max) return;

            bool fromProfile = hasProfile && !IsOverridden("overrideMaxSnapshots");
            EditorGUILayout.HelpBox(
                $"Duration × Snapshots/Sec = {needed}개가 Max Snapshots({max})보다 많아 꼬리 끝이 잘립니다." +
                (fromProfile ? " Profile의 Max Snapshots를 늘리거나 체크박스를 켜서 이 컴포넌트 값을 쓰세요." : string.Empty),
                MessageType.Warning);

            int fixedValue = Mathf.Min(needed, 128);
            if (!fromProfile && fixedValue > max && GUILayout.Button($"Max Snapshots = {fixedValue}"))
                maxSnapshots.intValue = fixedValue;
        }

        void DrawMaterialValidation(bool hasProfile)
        {
            // 선택한 오브젝트의 로컬 머티리얼이 서로 다르면 대상마다 실제 머티리얼이 다르다
            if (trailMaterial.hasMultipleDifferentValues)
            {
                DrawPerTargetMaterialValidation(false);
                return;
            }

            var local = trailMaterial.objectReferenceValue as Material;

            // 선택한 오브젝트의 Profile이 서로 다르고 로컬 머티리얼이 비어 있으면, 대상마다 자기 Profile의 머티리얼을 쓴다.
            if (local == null && profile.hasMultipleDifferentValues)
            {
                DrawPerTargetMaterialValidation(true);
                return;
            }

            Material fromProfile = hasProfile && cachedProfileRef != null ? cachedProfileRef.TrailMaterial : null;
            Material effective = local != null ? local : fromProfile;

            if (effective == null)
            {
                EditorGUILayout.HelpBox("Trail Material이 없으면 잔상이 그려지지 않습니다. 기본 머티리얼을 연결하거나 새로 만드세요.", MessageType.Warning);
                DrawMissingMaterialButtons(false);
                return;
            }

            if (local == null)
            {
                EditorGUILayout.HelpBox($"Profile의 Trail Material '{effective.name}'을 사용합니다.", MessageType.None);
            }
            else if (fromProfile != null && fromProfile != local)
            {
                EditorGUILayout.HelpBox(
                    $"이 컴포넌트의 Trail Material '{local.name}'이 Profile의 Trail Material '{fromProfile.name}'보다 우선합니다. " +
                    "Profile의 머티리얼을 쓰려면 이 항목을 비우세요.",
                    MessageType.Info);
                if (GUILayout.Button(UseProfileMaterialContent))
                {
                    // SerializedProperty 경유 → 선택한 모든 대상에 Undo 가능하게 적용 (OnInspectorGUI 끝에서 Apply).
                    // 이번 이벤트의 레이아웃이 바뀌지 않도록 아래 검증은 기존 값으로 계속 그린다.
                    trailMaterial.objectReferenceValue = null;
                }
            }

            DrawShaderValidation(effective, false);
        }

        // 대상마다 실제로 쓰는 머티리얼(ResolvedMaterial)이 다를 때: 머티리얼이 없는 대상만 경고하고, 버튼도 문제가 있는 대상에만 적용한다
        // (다른 대상의 머티리얼을 덮지 않게). fromProfiles = true면 로컬이 비어 있어 각자 Profile의 머티리얼을 쓰는 경우.
        void DrawPerTargetMaterialValidation(bool fromProfiles)
        {
            int missing = CountTargetsWithoutMaterial();
            if (missing > 0)
            {
                EditorGUILayout.HelpBox(
                    $"선택한 {targets.Length}개 중 {missing}개는 Trail Material이 없어 잔상이 그려지지 않습니다. " +
                    "아래 버튼은 머티리얼이 없는 오브젝트에만 적용됩니다.",
                    MessageType.Warning);
                DrawMissingMaterialButtons(true);
                return;
            }

            if (fromProfiles)
                EditorGUILayout.HelpBox("선택한 오브젝트마다 각자 Profile의 Trail Material을 사용합니다.", MessageType.None);

            // 셰이더·인스턴싱 문제가 있는 첫 머티리얼 하나만 알린다 (고치면 다음 것이 보인다)
            foreach (var t in targets)
            {
                Material m = ((TrailEffect)t).ResolvedMaterial;
                if (m != null && HasMaterialProblem(m))
                {
                    DrawShaderValidation(m, true);
                    return;
                }
            }
        }

        // 컴포넌트·Profile 어디에도 머티리얼이 없는 대상 수
        int CountTargetsWithoutMaterial()
        {
            int missing = 0;
            foreach (var t in targets)
            {
                if (((TrailEffect)t).ResolvedMaterial == null) missing++;
            }
            return missing;
        }

        void DrawMissingMaterialButtons(bool onlyMissing)
        {
            EditorGUILayout.BeginHorizontal();
            DrawReplaceMaterialButtons(onlyMissing, null);
            EditorGUILayout.EndHorizontal();
        }

        // Use Default / Create Material: 컴포넌트의 Trail Material 참조만 바꾸고 기존 머티리얼 에셋은 건드리지 않는다.
        // perTarget = true면 ResolvedMaterial이 replaceOnly인 대상에만 넣는다 (replaceOnly = null이면 머티리얼이 없는 대상).
        void DrawReplaceMaterialButtons(bool perTarget, Material replaceOnly)
        {
            Material defaultMat = LoadDefaultMaterial();
            using (new EditorGUI.DisabledScope(defaultMat == null))
            {
                if (GUILayout.Button(UseDefaultMaterialContent))
                {
                    if (perTarget) AssignMaterialToTargets(defaultMat, replaceOnly);
                    else trailMaterial.objectReferenceValue = defaultMat;
                }
            }
            if (GUILayout.Button(CreateMaterialContent))
                CreateTrailMaterial(perTarget, replaceOnly);
        }

        // ResolvedMaterial이 replaceOnly인 대상에만 로컬 Trail Material을 넣는다. 대상별 SerializedObject로 기록해 Undo·프리팹 오버라이드가 정상 동작한다.
        void AssignMaterialToTargets(Material mat, Material replaceOnly)
        {
            // 에디터의 SerializedObject에 남은 변경을 먼저 반영해, 아래 대상별 기록을 덮어쓰지 않게 한다
            serializedObject.ApplyModifiedProperties();

            foreach (var t in targets)
            {
                var trail = (TrailEffect)t;
                if (trail.ResolvedMaterial != replaceOnly) continue;   // UnityEngine.Object 비교: replaceOnly == null이면 머티리얼이 없는 대상

                var so = new SerializedObject(trail);
                so.FindProperty("trailMaterial").objectReferenceValue = mat;
                so.ApplyModifiedProperties();
            }

            serializedObject.Update();
            GUIUtility.ExitGUI();
        }

        static bool HasMaterialProblem(Material m)
        {
            return m.shader == null || m.shader.name != TrailEffect.ShaderName || !m.enableInstancing;
        }

        void DrawShaderValidation(Material effective, bool perTarget)
        {
            if (!HasMaterialProblem(effective)) return;

            bool wrongShader = effective.shader == null || effective.shader.name != TrailEffect.ShaderName;
            bool readOnly = IsReadOnlyMaterialCached(effective);
            string msg = wrongShader
                ? $"머티리얼 '{effective.name}'이 '{TrailEffect.ShaderName}' 셰이더를 쓰지 않아 잔상이 올바르게 그려지지 않습니다. 기본 Trail 머티리얼을 쓰거나 새로 만드세요."
                : $"머티리얼 '{effective.name}'의 GPU Instancing이 꺼져 있어, 빌드에서 인스턴싱 셰이더 변형이 제거되면 잔상이 사라질 수 있습니다.";
            if (readOnly)
                msg += "\n이 머티리얼은 읽기 전용(내장·모델 포함·수정 불가 패키지)이라 직접 고칠 수 없습니다.";
            EditorGUILayout.HelpBox(msg, MessageType.Warning);

            if (!wrongShader && !readOnly)
            {
                // 인스턴싱 플래그는 보이는 모습을 바꾸지 않으므로 바로 고친다
                if (GUILayout.Button(EnableInstancingContent))
                    EnableInstancing(effective);
                return;
            }

            // 셰이더가 다르면 기존 에셋을 몰래 바꾸지 않고 새 머티리얼로 교체하는 것을 기본으로 한다
            EditorGUILayout.BeginHorizontal();
            DrawReplaceMaterialButtons(perTarget, effective);
            if (wrongShader && !readOnly && GUILayout.Button(ChangeShaderContent))
                ChangeShaderWithConfirm(effective);
            EditorGUILayout.EndHorizontal();
        }

        bool IsReadOnlyMaterialCached(Material mat)
        {
            if (readOnlyCheckedMat != mat)
            {
                readOnlyCheckedMat = mat;
                readOnlyCheckedValue = IsReadOnlyMaterial(mat);
            }
            return readOnlyCheckedValue;
        }

        // 고쳐도 저장되지 않는 머티리얼: 내장 리소스, 모델(FBX 등)에서 임포트된 머티리얼, 수정 불가 패키지(레지스트리·Git 등)의 머티리얼
        static bool IsReadOnlyMaterial(Material mat)
        {
            string path = AssetDatabase.GetAssetPath(mat);
            if (string.IsNullOrEmpty(path)) return false;   // 에셋이 아닌 머티리얼 (씬에 함께 저장됨)

            bool inAssets = path.StartsWith("Assets/", System.StringComparison.Ordinal);
            bool inPackages = path.StartsWith("Packages/", System.StringComparison.Ordinal);
            if (!inAssets && !inPackages) return true;       // 내장 리소스
            if (!AssetDatabase.IsNativeAsset(mat)) return true;

            if (inPackages)
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                if (info != null &&
                    info.source != UnityEditor.PackageManager.PackageSource.Embedded &&
                    info.source != UnityEditor.PackageManager.PackageSource.Local)
                    return true;
            }
            return false;
        }

        void DrawRuntimeControls()
        {
            TelleRGUI.Section("Runtime");

            bool allActive = true;
            bool anyNotInitialized = false;
            foreach (var t in targets)
            {
                var trail = (TrailEffect)t;
                if (!trail.Active) allActive = false;
                if (trail.isActiveAndEnabled && !trail.IsInitialized) anyNotInitialized = true;
            }

            if (anyNotInitialized)
                EditorGUILayout.HelpBox("잔상이 초기화되지 않았습니다. Console의 [TelleR/TrailEffect] 메시지를 확인하세요.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear"))
            {
                foreach (var t in targets)
                    ((TrailEffect)t).Clear();
            }
            if (GUILayout.Button(allActive ? "Pause" : "Resume"))
            {
                bool next = !allActive;
                foreach (var t in targets)
                {
                    var trail = (TrailEffect)t;
                    Undo.RecordObject(trail, next ? "Resume Trail" : "Pause Trail");
                    trail.SetActive(next);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawDebugMotion()
        {
            TelleRGUI.Section("Debug Motion");

            if (!Application.isPlaying)
            {
                // 편집 모드에서 붙은 오실레이터는 씬에 저장돼 빌드에서도 오브젝트를 흔든다
                bool anySaved = false;
                foreach (var t in targets)
                {
                    if (((TrailEffect)t).GetComponent<TrailDebugOscillator>() != null) { anySaved = true; break; }
                }

                if (anySaved)
                {
                    EditorGUILayout.HelpBox("TrailDebugOscillator가 오브젝트에 저장돼 있습니다. 빌드에서도 오브젝트가 흔들리니 테스트가 끝났으면 제거하세요.", MessageType.Warning);
                    if (GUILayout.Button("Remove Oscillator"))
                    {
                        foreach (var t in targets)
                        {
                            var osc = ((TrailEffect)t).GetComponent<TrailDebugOscillator>();
                            if (osc != null) Undo.DestroyObjectImmediate(osc);
                        }
                        GUIUtility.ExitGUI();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Play Mode에서 테스트용 움직임을 붙여 트레일을 확인할 수 있습니다. Play Mode를 끝내면 자동으로 사라집니다.", MessageType.Info);
                }
                return;
            }

            var first = ((TrailEffect)target).GetComponent<TrailDebugOscillator>();
            bool isRunning = first != null && first.enabled;

            if (isRunning)
            {
                EditorGUI.BeginChangeCheck();
                var pattern = (DebugMotionPattern)EditorGUILayout.EnumPopup("Pattern", first.Pattern);
                float speed = EditorGUILayout.Slider("Speed", first.Speed, 0.1f, 20f);
                float distance = EditorGUILayout.Slider("Distance", first.Distance, 0.01f, 10f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var t in targets)
                    {
                        var osc = ((TrailEffect)t).GetComponent<TrailDebugOscillator>();
                        if (osc == null) continue;
                        Undo.RecordObject(osc, "Change Debug Motion");
                        osc.Pattern = pattern;
                        osc.Speed = speed;
                        osc.Distance = distance;
                    }
                }

                if (GUILayout.Button("■ Stop"))
                {
                    foreach (var t in targets)
                    {
                        var osc = ((TrailEffect)t).GetComponent<TrailDebugOscillator>();
                        if (osc == null) continue;
                        osc.enabled = false;   // OnDisable에서 원래 위치로 복귀
                        Undo.DestroyObjectImmediate(osc);
                    }
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < MotionPatterns.Length; i++)
                {
                    DebugMotionPattern pattern = MotionPatterns[i];
                    if (GUILayout.Button(MotionPatternContents[i]))
                    {
                        foreach (var t in targets)
                        {
                            var go = ((TrailEffect)t).gameObject;
                            var osc = go.GetComponent<TrailDebugOscillator>();
                            if (osc == null) osc = Undo.AddComponent<TrailDebugOscillator>(go);
                            osc.Pattern = pattern;
                            osc.Speed = 3f;
                            osc.Distance = 0.5f;
                            osc.enabled = true;
                        }
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ═════════════════════════════════════════════
        //  Field helpers
        // ═════════════════════════════════════════════

        bool IsOverridden(string overrideFlag)
        {
            overrideCache.TryGetValue(overrideFlag, out var ovProp);
            return ovProp != null && ovProp.boolValue;
        }

        T ResolveEnum<T>(bool hasProfile, SerializedObject profileSO,
            SerializedProperty localProp, string profileField, string overrideFlag) where T : System.Enum
        {
            if (hasProfile && profileSO != null && !IsOverridden(overrideFlag))
                return (T)(object)profileSO.FindProperty(profileField).intValue;
            return (T)(object)localProp.intValue;
        }

        float ResolveFloat(bool hasProfile, SerializedObject profileSO,
            SerializedProperty localProp, string profileField, string overrideFlag)
        {
            if (hasProfile && profileSO != null && !IsOverridden(overrideFlag))
                return profileSO.FindProperty(profileField).floatValue;
            return localProp.floatValue;
        }

        int ResolveInt(bool hasProfile, SerializedObject profileSO,
            SerializedProperty localProp, string profileField, string overrideFlag)
        {
            if (hasProfile && profileSO != null && !IsOverridden(overrideFlag))
                return profileSO.FindProperty(profileField).intValue;
            return localProp.intValue;
        }

        void Field(bool hasProfile, SerializedObject profileSO,
            SerializedProperty localProp, string profileField, string overrideFlag, GUIContent content)
        {
            if (!hasProfile || profileSO == null)
            {
                EditorGUILayout.PropertyField(localProp, content, true);
                return;
            }

            overrideCache.TryGetValue(overrideFlag, out var ovProp);
            bool ov = ovProp != null && ovProp.boolValue;

            SerializedProperty displayProp = ov ? localProp : (profileSO.FindProperty(profileField) ?? localProp);

            float propHeight = EditorGUI.GetPropertyHeight(displayProp, content, true);
            Rect row = EditorGUILayout.GetControlRect(false, propHeight);

            Rect checkRect = new Rect(row.x, row.y + 1f, 14f, 14f);

            float indent = 20f;
            Rect fieldRect = new Rect(row.x + indent, row.y, row.width - indent, row.height);

            if (ovProp != null)
            {
                EditorGUI.showMixedValue = ovProp.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                bool newVal = EditorGUI.Toggle(checkRect, GUIContent.none, ovProp.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    ovProp.boolValue = newVal;
                    ov = newVal;
                    displayProp = ov ? localProp : (profileSO.FindProperty(profileField) ?? localProp);
                }
                EditorGUI.showMixedValue = false;
                GUI.Label(checkRect, OverrideToggleTip);
            }

            float savedLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = savedLabelWidth - indent;

            if (ov)
            {
                EditorGUI.PropertyField(fieldRect, localProp, content, true);
            }
            else
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    EditorGUI.PropertyField(fieldRect, displayProp, content, true);
            }

            EditorGUIUtility.labelWidth = savedLabelWidth;
        }

        // ═════════════════════════════════════════════
        //  Asset helpers
        // ═════════════════════════════════════════════

        static Material LoadDefaultMaterial()
        {
            string path = AssetDatabase.GUIDToAssetPath(TrailEffect.DefaultMaterialGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        void CreateProfile(TrailEffect fx)
        {
            string path = EditorUtility.SaveFilePanelInProject("Trail Profile 저장", "TrailProfile", "asset", "Profile을 저장할 위치를 선택하세요.");
            if (string.IsNullOrEmpty(path)) { GUIUtility.ExitGUI(); return; }

            var p = CreateInstance<TrailEffectProfile>();
            p.Mode = fx.Mode;
            p.ColorMode = fx.ColorMode;
            p.TrailColor = fx.TrailColor;
            p.ColorOverLifetime = fx.ColorOverLifetime;
            p.Duration = fx.Duration;
            p.SnapshotsPerSecond = fx.SnapshotsPerSecond;
            p.ScaleStart = fx.ScaleStart;
            p.ScaleEnd = fx.ScaleEnd;
            p.FresnelPower = fx.FresnelPower;
            p.FresnelIntensity = fx.FresnelIntensity;
            p.StampTexture = fx.StampTexture;
            p.StampSizeStart = fx.StampSizeStart;
            p.StampSizeEnd = fx.StampSizeEnd;
            p.StampStyle = fx.StampStyle;
            p.StampCount = fx.StampCount;
            p.StampFollowSpeed = fx.StampFollowSpeed;
            p.StampSpacing = fx.StampSpacing;
            p.PreventOverlap = fx.PreventOverlap;
            p.MaxSnapshots = fx.MaxSnapshots;
            p.MinDistance = fx.MinDistance;
            p.TrailMaterial = fx.TrailMaterial;

            AssetDatabase.CreateAsset(p, path);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + $"Profile을 만들었습니다: {path}", p);

            profile.objectReferenceValue = p;
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }

        void CreateTrailMaterial(bool perTarget, Material replaceOnly)
        {
            var shader = Shader.Find(TrailEffect.ShaderName);
            if (shader == null)
            {
                Debug.LogError(LogPrefix + $"'{TrailEffect.ShaderName}' 셰이더를 찾지 못했습니다. 패키지가 올바르게 설치됐는지 확인하세요.");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject("Trail Material 저장", "TrailFX", "mat", "머티리얼을 저장할 위치를 선택하세요.");
            if (string.IsNullOrEmpty(path)) { GUIUtility.ExitGUI(); return; }

            var mat = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path),
                enableInstancing = true,
                renderQueue = (int)RenderQueue.Transparent
            };

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + $"머티리얼을 만들었습니다: {path}", mat);

            if (perTarget)
            {
                AssignMaterialToTargets(mat, replaceOnly);   // 내부에서 ExitGUI
                return;
            }

            // 선택한 모든 TrailEffect에 연결 (SerializedProperty 경유 → Undo 가능)
            trailMaterial.objectReferenceValue = mat;
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }

        static void EnableInstancing(Material mat)
        {
            Undo.RecordObject(mat, "Enable GPU Instancing");
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            Debug.Log(LogPrefix + $"머티리얼 '{mat.name}'의 GPU Instancing을 켰습니다.", mat);
        }

        // 공유 머티리얼일 수 있으므로 영향 범위를 알리고 확인을 받은 뒤에만 셰이더를 바꾼다
        static void ChangeShaderWithConfirm(Material mat)
        {
            var shader = Shader.Find(TrailEffect.ShaderName);
            if (shader == null)
            {
                Debug.LogError(LogPrefix + $"'{TrailEffect.ShaderName}' 셰이더를 찾지 못했습니다. 패키지가 올바르게 설치됐는지 확인하세요.");
                GUIUtility.ExitGUI();
                return;
            }

            string path = AssetDatabase.GetAssetPath(mat);
            string oldShader = mat.shader != null ? mat.shader.name : "없음";
            bool ok = TelleRGUI.Confirm(
                "셰이더 교체",
                $"머티리얼 '{mat.name}'" + (string.IsNullOrEmpty(path) ? string.Empty : $" ({path})") +
                $"의 셰이더를 '{oldShader}'에서 '{TrailEffect.ShaderName}'(으)로 바꿉니다.\n\n" +
                "이 머티리얼을 쓰는 모든 오브젝트의 모습이 함께 바뀝니다. 잔상 전용 머티리얼이 아니라면 취소하고 " +
                "'Use Default' 또는 'Create Material'로 새 머티리얼을 쓰세요.\n\n" +
                "바꾼 뒤에도 Edit > Undo로 되돌릴 수 있습니다.",
                "교체", "취소");

            if (ok)
            {
                Undo.RecordObject(mat, "Change Trail Shader");
                mat.shader = shader;
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                Debug.Log(LogPrefix + $"머티리얼 '{mat.name}'의 셰이더를 '{oldShader}'에서 '{TrailEffect.ShaderName}'(으)로 바꿨습니다.", mat);
            }

            // 모달 창 뒤에는 레이아웃이 어긋나므로 이번 GUI 패스를 끝낸다
            GUIUtility.ExitGUI();
        }
    }
}
