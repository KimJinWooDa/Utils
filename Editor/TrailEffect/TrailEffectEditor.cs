using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace TelleR
{
    [CustomEditor(typeof(TrailEffect))]
    [CanEditMultipleObjects]
    public class TrailEffectEditor : Editor
    {
        SerializedProperty profile, mode, colorMode;
        SerializedProperty trailColor, colorOverLifetime, duration, snapshotsPerSecond;
        SerializedProperty scaleStart, scaleEnd;
        SerializedProperty fresnelPower, fresnelIntensity;
        SerializedProperty stampTexture, stampSizeStart, stampSizeEnd, stampStyle, stampCount, stampFollowSpeed, stampSpacing;
        SerializedProperty preventOverlap;
        SerializedProperty maxSnapshots, minDistance;
        SerializedProperty trailMaterial;

        Dictionary<string, SerializedProperty> overrideCache;
        SerializedObject cachedProfileSO;
        TrailEffectProfile cachedProfileRef;
        static GUIStyle cachedHintStyle;

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
            trailMaterial = serializedObject.FindProperty("trailMaterial");

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

            Section("Profile");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(profile, GUIContent.none);
            if (GUILayout.Button("Create", GUILayout.Width(56)))
                CreateProfile(fx);
            EditorGUILayout.EndHorizontal();
            Hint("여러 드론에 같은 트레일을 적용할 때 Profile 하나로 공유 가능");

            bool hasProfile = profile.objectReferenceValue != null;
            SerializedObject profileSO = GetProfileSO();

            if (hasProfile)
                EditorGUILayout.HelpBox("\u2611 Check = use local value  |  Uncheck = use Profile value", MessageType.Info);

            Section("Mode");
            Field(hasProfile, profileSO, mode, "Mode", "overrideMode", "Mode");
            Hint("Color=드론 메쉬 잔상, TextureStamp=텍스쳐 빌보드 (Follow/Trail 스타일)");

            TrailMode currentMode = ResolveEnum<TrailMode>(hasProfile, profileSO, mode, "Mode", "overrideMode");
            bool isColor = currentMode == TrailMode.Color;
            bool isStamp = currentMode == TrailMode.TextureStamp;

            StampStyle currentStyle = StampStyle.Follow;
            if (isStamp)
                currentStyle = ResolveEnum<StampStyle>(hasProfile, profileSO, stampStyle, "StampStyle", "overrideStampStyle");

            bool isStampFollow = isStamp && currentStyle == StampStyle.Follow;
            bool isStampTrail = isStamp && currentStyle == StampStyle.Trail;
            bool usesSnapshots = isColor || isStampTrail;

            Section("Appearance");
            Field(hasProfile, profileSO, colorMode, "ColorMode", "overrideColorMode", "Color Mode");
            Hint("SolidColor=단색, Gradient=시간에 따라 색/투명도 변화");

            TrailColorMode currentColorMode = ResolveEnum<TrailColorMode>(hasProfile, profileSO, colorMode, "ColorMode", "overrideColorMode");

            if (currentColorMode == TrailColorMode.SolidColor)
            {
                Field(hasProfile, profileSO, trailColor, "TrailColor", "overrideTrailColor", "Trail Color");
                Hint("기본색. 예: 빨강=(1,0,0,0.6), alpha로 기본 투명도 조절");
            }
            else
            {
                Field(hasProfile, profileSO, colorOverLifetime, "ColorOverLifetime", "overrideColorOverLifetime", "Color Over Lifetime");
                Hint("시간에 따라 색/투명도 변화. 예: 시안->파랑 페이드아웃");
            }

            if (isColor)
            {
                Field(hasProfile, profileSO, scaleStart, "ScaleStart", "overrideScaleStart", "Scale Start");
                Hint("잔상 시작 배율. 예: 1.0=원본, 1.2=살짝 크게 시작, 0.8=작게 시작");

                Field(hasProfile, profileSO, scaleEnd, "ScaleEnd", "overrideScaleEnd", "Scale End");
                Hint("잔상 끝 배율. 예: 0.5=줄어들며 소멸, 1.5=커지며 소멸");
            }

            if (isColor)
            {
                Section("Fresnel");
                Field(hasProfile, profileSO, fresnelIntensity, "FresnelIntensity", "overrideFresnelIntensity", "Intensity");
                Hint("가장자리 빛나는 정도. 예: 0=끔, 0.5=은은한 림라이트, 1.5=강한 네온 느낌");

                float fVal = ResolveFloat(hasProfile, profileSO, fresnelIntensity, "FresnelIntensity", "overrideFresnelIntensity");
                if (fVal > 0.001f)
                {
                    Field(hasProfile, profileSO, fresnelPower, "FresnelPower", "overrideFresnelPower", "Power");
                    Hint("발광 선명도. 예: 1=넓고 뭉툭, 3=적당, 5=얇고 날카로운 외곽선");
                }
            }

            if (isStamp)
            {
                Section("Texture Stamp");
                Field(hasProfile, profileSO, stampTexture, "StampTexture", "overrideStampTexture", "Texture");
                Hint("항상 카메라를 향하는 빌보드 텍스쳐. 원형/별/하트 등 알파 텍스쳐 사용");

                Field(hasProfile, profileSO, stampSizeStart, "StampSizeStart", "overrideStampSizeStart", "Size Start");
                Hint("빌보드 시작 크기. Follow=첫 번째 스탬프, Trail=생성 직후");

                Field(hasProfile, profileSO, stampSizeEnd, "StampSizeEnd", "overrideStampSizeEnd", "Size End");
                Hint("빌보드 끝 크기. Follow=마지막 스탬프, Trail=소멸 직전");

                Field(hasProfile, profileSO, stampStyle, "StampStyle", "overrideStampStyle", "Style");
                Hint("Follow=드론 뒤를 졸졸 따라다님, Trail=지나간 자리에 흔적을 남기고 서서히 사라짐");

                if (isStampFollow)
                {
                    Field(hasProfile, profileSO, stampCount, "StampCount", "overrideStampCount", "Count");
                    Hint("따라다니는 스탬프 개수. 예: 3=가벼운 효과, 5=기본, 8=긴 꼬리");

                    Field(hasProfile, profileSO, stampFollowSpeed, "StampFollowSpeed", "overrideStampFollowSpeed", "Follow Speed");
                    Hint("드론을 따라가는 속도. 예: 3=느긋하게 따라옴, 8=기본, 20=빠르게 추적");

                    Field(hasProfile, profileSO, stampSpacing, "StampSpacing", "overrideStampSpacing", "Spacing");
                    Hint("스탬프 간 최소 간격. 예: 0.3=촘촘, 0.5=기본, 1.5=넓은 간격");
                }
            }

            if (usesSnapshots)
            {
                Section("Performance");
                Field(hasProfile, profileSO, duration, "Duration", "overrideDuration", "Duration");
                Hint("잔상 수명. 예: 0.3=짧고 빠른 잔상, 1.0=긴 꼬리, 2.0=느린 페이드");

                Field(hasProfile, profileSO, snapshotsPerSecond, "SnapshotsPerSecond", "overrideSnapshotsPerSecond", "Snapshots/Sec");
                Hint("초당 잔상 수. 예: 10=가벼운 효과, 30=부드러운 트레일, 60=매우 촘촘 (성능 주의)");

                Field(hasProfile, profileSO, preventOverlap, "PreventOverlap", "overridePreventOverlap", "Prevent Overlap");
                Hint("켜면 잔상끼리 겹쳐도 뭉쳐 보이지 않음. 끄면 겹치는 부분이 더 불투명해짐");

                Field(hasProfile, profileSO, maxSnapshots, "MaxSnapshots", "overrideMaxSnapshots", "Max Snapshots");
                Hint("최대 잔상 개수. 예: 16=가벼운 효과, 32=기본, 64=긴 꼬리 (GPU 버퍼 크기)");

                Field(hasProfile, profileSO, minDistance, "MinDistance", "overrideMinDistance", "Min Distance");
                Hint("잔상 간 최소 간격. 예: 0.01=거의 연속, 0.1=적당한 간격, 0.5=듬성듬성");
            }

            Section("Material");
            EditorGUILayout.PropertyField(trailMaterial, new GUIContent("Trail Material"));
            Hint("TelleR/Trail 셰이더 Material 에셋. GPU Instancing 필수. 없으면 Create Material 클릭");
            if (trailMaterial.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Trail Material is required. Click 'Create Material' to auto-generate one.", MessageType.Warning);
                if (GUILayout.Button("Create Material"))
                    CreateTrailMaterial(fx);
            }

            if (Application.isPlaying)
            {
                Section("Runtime");
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear"))
                    fx.Clear();
                if (GUILayout.Button(fx.Active ? "Pause" : "Resume"))
                    fx.SetActive(!fx.Active);
                EditorGUILayout.EndHorizontal();
            }

            Section("Debug Motion");
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Play Mode에서 실행하면 실시간 트레일을 확인할 수 있습니다.", MessageType.Info);

            var oscillator = fx.GetComponent<TrailDebugOscillator>();
            bool isRunning = oscillator != null && oscillator.enabled;

            if (isRunning)
            {
                oscillator.Pattern = (DebugMotionPattern)EditorGUILayout.EnumPopup("Pattern", oscillator.Pattern);
                oscillator.Speed = EditorGUILayout.Slider("Speed", oscillator.Speed, 0.1f, 20f);
                oscillator.Distance = EditorGUILayout.Slider("Distance", oscillator.Distance, 0.01f, 10f);

                if (GUILayout.Button("\u25a0 Stop"))
                {
                    oscillator.enabled = false;
                    DestroyImmediate(oscillator);
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                foreach (DebugMotionPattern pattern in System.Enum.GetValues(typeof(DebugMotionPattern)))
                {
                    if (GUILayout.Button("\u25b6 " + pattern))
                    {
                        var osc = fx.gameObject.AddComponent<TrailDebugOscillator>();
                        osc.Pattern = pattern;
                        osc.Speed = 3f;
                        osc.Distance = 0.5f;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            serializedObject.ApplyModifiedProperties();
        }

        T ResolveEnum<T>(bool hasProfile, SerializedObject profileSO,
            SerializedProperty localProp, string profileField, string overrideFlag) where T : System.Enum
        {
            if (hasProfile && profileSO != null)
            {
                overrideCache.TryGetValue(overrideFlag, out var ovProp);
                if (ovProp != null && !ovProp.boolValue)
                    return (T)(object)profileSO.FindProperty(profileField).intValue;
            }
            return (T)(object)localProp.intValue;
        }

        float ResolveFloat(bool hasProfile, SerializedObject profileSO,
            SerializedProperty localProp, string profileField, string overrideFlag)
        {
            if (hasProfile && profileSO != null)
            {
                overrideCache.TryGetValue(overrideFlag, out var ovProp);
                if (ovProp != null && !ovProp.boolValue)
                    return profileSO.FindProperty(profileField).floatValue;
            }
            return localProp.floatValue;
        }

        void Section(string title)
        {
            EditorGUILayout.Space(6);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1));
            EditorGUI.DrawRect(rect, new Color(0.35f, 0.35f, 0.35f));
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        static void Hint(string text)
        {
            if (cachedHintStyle == null)
            {
                cachedHintStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
                    fontSize = 11,
                    padding = new RectOffset(18, 0, 0, 4)
                };
            }
            EditorGUILayout.LabelField("\u3134 " + text, cachedHintStyle);
        }

        void Field(bool hasProfile, SerializedObject profileSO,
            SerializedProperty localProp, string profileField, string overrideFlag, string label)
        {
            var content = new GUIContent(label);

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
                EditorGUI.BeginChangeCheck();
                bool newVal = EditorGUI.Toggle(checkRect, GUIContent.none, ovProp.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    ovProp.boolValue = newVal;
                    ov = newVal;
                    displayProp = ov ? localProp : (profileSO.FindProperty(profileField) ?? localProp);
                }
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

        void CreateProfile(TrailEffect fx)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Trail Profile", "TrailProfile", "asset", "Save location");
            if (string.IsNullOrEmpty(path)) return;

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

            AssetDatabase.CreateAsset(p, path);
            AssetDatabase.SaveAssets();
            fx.Profile = p;
            EditorUtility.SetDirty(fx);
        }

        void CreateTrailMaterial(TrailEffect fx)
        {
            var shader = Shader.Find("TelleR/Trail");
            if (shader == null)
            {
                Debug.LogError("[TrailEffectEditor] Shader 'TelleR/Trail' not found.");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject("Save Trail Material", "TrailFX", "mat", "Save location");
            if (string.IsNullOrEmpty(path)) return;

            var mat = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path),
                enableInstancing = true,
                renderQueue = (int)RenderQueue.Transparent
            };

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();

            serializedObject.FindProperty("trailMaterial").objectReferenceValue = mat;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(fx);

            Debug.Log($"[TrailEffectEditor] Created Material at: {path}");
        }
    }
}