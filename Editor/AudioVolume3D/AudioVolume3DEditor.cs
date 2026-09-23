using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace TelleR
{
    [CustomEditor(typeof(AudioVolume3D))]
    [CanEditMultipleObjects]
    public class AudioVolume3DEditor : Editor
    {
        private AudioVolume3D script;

        private int currentTab;
        private readonly string[] tabNames = { "Settings", "Audio", "Occlusion", "Inner", "Visuals" };

        private SerializedProperty zoneColorProp;
        private SerializedProperty fadeZoneColorProp;
        private SerializedProperty occlusionZoneColorProp;
        private SerializedProperty showLabelProp;
        private SerializedProperty showMainVolumeProp;
        private SerializedProperty showFadeZoneProp;
        private SerializedProperty showInnerVolumesProp;
        private SerializedProperty showOcclusionZonesProp;
        private SerializedProperty volumeCenterProp;
        private SerializedProperty volumeSizeProp;
        private SerializedProperty fadeDistanceProp;
        private SerializedProperty useHeightAttenuationProp;
        private SerializedProperty maxVolumeProp;
        private SerializedProperty fadeInSpeedProp;
        private SerializedProperty fadeOutSpeedProp;
        private SerializedProperty useUnscaledTimeProp;
        private SerializedProperty manualOcclusionZonesProp;
        private SerializedProperty occlusionSmoothSpeedProp;
        private SerializedProperty innerVolumesProp;
        private SerializedProperty clipProp;
        private SerializedProperty outputGroupProp;
        private SerializedProperty spatialBlendProp;
        private SerializedProperty autoSpatialBlendProp;
        private SerializedProperty minDistanceProp;
        private SerializedProperty maxDistanceProp;
        private SerializedProperty loopProp;
        private SerializedProperty playOnAwakeProp;
        private SerializedProperty autoPlayOnEnterProp;
        private SerializedProperty targetTransformProp;
        private SerializedProperty targetTagProp;
        private SerializedProperty useListenerAsTargetProp;
        private SerializedProperty gizmoHandleScaleProp;

        private static readonly GUIContent HandleSizeContent = new GUIContent("Handle Size", "씬 뷰 크기 조절 핸들의 크기 배율");

        // 씬 뷰에서 이동 핸들(PositionHandle)을 보여줄 서브 볼륨 — 중심 점을 클릭해 선택
        private const int KindNone = -1;
        private const int KindOcclusion = 0;
        private const int KindInner = 1;
        private static Object selectedOwner;
        private static int selectedKind = KindNone;
        private static int selectedIndex = -1;

        // 가려진(벽 뒤) 부분을 그리는 패스의 알파 배율
        private const float OccludedAlphaScale = 0.3f;
        private float alphaScale = 1f;

        // OnSceneGUI마다 할당하지 않도록 재사용하는 버퍼
        private static readonly Vector3[] Corners = new Vector3[8];
        private static readonly Vector3[] Face = new Vector3[4];
        private static readonly int[] FaceIndices =
        {
            0, 1, 2, 3,
            4, 5, 6, 7,
            0, 1, 5, 4,
            2, 3, 7, 6,
            1, 2, 6, 5,
            3, 0, 4, 7
        };

        private static GUIStyle labelStyle;
        private static GUIStyle labelShadowStyle;

        private void OnEnable()
        {
            script = (AudioVolume3D)target;

            zoneColorProp = serializedObject.FindProperty("ZoneColor");
            fadeZoneColorProp = serializedObject.FindProperty("FadeZoneColor");
            occlusionZoneColorProp = serializedObject.FindProperty("OcclusionZoneColor");

            showLabelProp = serializedObject.FindProperty("ShowLabel");
            showMainVolumeProp = serializedObject.FindProperty("ShowMainVolume");
            showFadeZoneProp = serializedObject.FindProperty("ShowFadeZone");
            showInnerVolumesProp = serializedObject.FindProperty("ShowInnerVolumes");
            showOcclusionZonesProp = serializedObject.FindProperty("ShowOcclusionZones");

            volumeCenterProp = serializedObject.FindProperty("VolumeCenter");
            volumeSizeProp = serializedObject.FindProperty("VolumeSize");
            fadeDistanceProp = serializedObject.FindProperty("FadeDistance");
            useHeightAttenuationProp = serializedObject.FindProperty("UseHeightAttenuation");
            maxVolumeProp = serializedObject.FindProperty("MaxVolume");

            fadeInSpeedProp = serializedObject.FindProperty("FadeInSpeed");
            fadeOutSpeedProp = serializedObject.FindProperty("FadeOutSpeed");
            useUnscaledTimeProp = serializedObject.FindProperty("UseUnscaledTime");

            manualOcclusionZonesProp = serializedObject.FindProperty("ManualOcclusionZones");
            occlusionSmoothSpeedProp = serializedObject.FindProperty("OcclusionSmoothSpeed");
            innerVolumesProp = serializedObject.FindProperty("InnerVolumes");

            clipProp = serializedObject.FindProperty("Clip");
            outputGroupProp = serializedObject.FindProperty("OutputGroup");
            spatialBlendProp = serializedObject.FindProperty("SpatialBlend");
            autoSpatialBlendProp = serializedObject.FindProperty("AutoSpatialBlend");
            minDistanceProp = serializedObject.FindProperty("MinDistance");
            maxDistanceProp = serializedObject.FindProperty("MaxDistance");
            loopProp = serializedObject.FindProperty("Loop");
            playOnAwakeProp = serializedObject.FindProperty("PlayOnAwake");
            autoPlayOnEnterProp = serializedObject.FindProperty("AutoPlayOnEnter");

            targetTransformProp = serializedObject.FindProperty("TargetTransform");
            targetTagProp = serializedObject.FindProperty("TargetTag");
            useListenerAsTargetProp = serializedObject.FindProperty("UseListenerAsTarget");
            gizmoHandleScaleProp = serializedObject.FindProperty("GizmoHandleScale");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            currentTab = GUILayout.Toolbar(currentTab, tabNames);
            EditorGUILayout.Space(10);

            switch (currentTab)
            {
                case 0:
                    DrawGeneralTab();
                    break;
                case 1:
                    DrawAudioTab();
                    break;
                case 2:
                    DrawOcclusionTab();
                    break;
                case 3:
                    DrawInnerVolumesTab();
                    break;
                case 4:
                    DrawVisualsTab();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGeneralTab()
        {
            EditorGUILayout.HelpBox("대상이 메인 볼륨 안에 있으면 최대 음량, 경계에서 Fade Distance만큼 멀어지면 무음입니다. 크기·거리는 로컬 단위(스케일 적용)입니다.", MessageType.Info);
            // 커스텀 에디터는 [Header]를 그리지 않으므로 같은 묶음을 Section으로 표시
            TelleRGUI.Section("Tracking");
            EditorGUILayout.PropertyField(targetTransformProp);
            EditorGUILayout.PropertyField(targetTagProp);
            EditorGUILayout.PropertyField(useListenerAsTargetProp);
            TelleRGUI.Section("Main Volume");
            EditorGUILayout.PropertyField(volumeCenterProp);
            EditorGUILayout.PropertyField(volumeSizeProp);
            EditorGUILayout.PropertyField(fadeDistanceProp);
            EditorGUILayout.PropertyField(useHeightAttenuationProp);
            EditorGUILayout.PropertyField(maxVolumeProp);
            TelleRGUI.Section("Fade Smoothing");
            EditorGUILayout.PropertyField(fadeInSpeedProp);
            EditorGUILayout.PropertyField(fadeOutSpeedProp);
            EditorGUILayout.PropertyField(useUnscaledTimeProp);
        }

        private void DrawAudioTab()
        {
            bool autoBlendIgnored = autoSpatialBlendProp.boolValue && innerVolumesProp.arraySize > 0;
            if (autoBlendIgnored)
                EditorGUILayout.HelpBox("Inner Volume이 있어 Auto Spatial Blend가 적용되지 않습니다 (발음 위치를 살리기 위해 Spatial Blend 값을 그대로 씁니다).", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Auto Spatial Blend는 Inner Volume이 없을 때만 동작합니다. 실행 중 인스펙터·스크립트 변경은 즉시 반영됩니다.", MessageType.Info);

            TelleRGUI.Section("Source");
            EditorGUILayout.PropertyField(clipProp);
            EditorGUILayout.PropertyField(outputGroupProp);
            EditorGUILayout.PropertyField(spatialBlendProp);
            EditorGUILayout.PropertyField(autoSpatialBlendProp);
            EditorGUILayout.PropertyField(minDistanceProp);
            EditorGUILayout.PropertyField(maxDistanceProp);
            TelleRGUI.Section("Playback");
            EditorGUILayout.PropertyField(loopProp);
            EditorGUILayout.PropertyField(playOnAwakeProp);
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(playOnAwakeProp.boolValue && !playOnAwakeProp.hasMultipleDifferentValues))
                EditorGUILayout.PropertyField(autoPlayOnEnterProp);
        }

        private void DrawOcclusionTab()
        {
            EditorGUILayout.HelpBox("레이캐스트가 아닌 수동 영역입니다. 대상이 영역 안에 있으면 음량을 줄이고 고음을 깎습니다. 씬 뷰에서 영역 중심 점을 클릭하면 이동 핸들이 나타납니다.", MessageType.Info);
            EditorGUILayout.PropertyField(occlusionSmoothSpeedProp);
            EditorGUILayout.PropertyField(manualOcclusionZonesProp, true);
            DrawSelectionRow(KindOcclusion);
        }

        private void DrawInnerVolumesTab()
        {
            EditorGUILayout.HelpBox("소리가 실제로 나는 지점입니다. 대상이 가까워지면 발음 위치가 이 영역 쪽으로 끌려갑니다. 씬 뷰에서 영역 중심 점을 클릭하면 이동 핸들이 나타납니다.", MessageType.Info);
            EditorGUILayout.PropertyField(innerVolumesProp, true);
            DrawSelectionRow(KindInner);
        }

        private void DrawVisualsTab()
        {
            EditorGUILayout.HelpBox("씬 뷰 표시 설정입니다. 벽 등에 가려진 부분은 반투명하게 표시됩니다.", MessageType.Info);
            TelleRGUI.Section("Visibility");
            EditorGUILayout.PropertyField(showMainVolumeProp);
            EditorGUILayout.PropertyField(showFadeZoneProp);
            EditorGUILayout.PropertyField(showInnerVolumesProp);
            EditorGUILayout.PropertyField(showOcclusionZonesProp);
            EditorGUILayout.PropertyField(showLabelProp);
            TelleRGUI.Section("Colors");
            EditorGUILayout.PropertyField(zoneColorProp);
            EditorGUILayout.PropertyField(fadeZoneColorProp);
            EditorGUILayout.PropertyField(occlusionZoneColorProp);
            TelleRGUI.Section("Handles");
            EditorGUILayout.PropertyField(gizmoHandleScaleProp, HandleSizeContent);
        }

        /// <summary>씬 뷰에서 선택된 서브 볼륨 이름과 선택 해제 버튼 (단일 선택일 때만).</summary>
        private void DrawSelectionRow(int kind)
        {
            if (targets.Length != 1 || selectedOwner != target || selectedKind != kind) return;

            string zoneName = GetSelectedName(kind);
            if (zoneName == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Selected", zoneName);
                if (GUILayout.Button("Deselect", EditorStyles.miniButton, GUILayout.Width(70f)))
                {
                    ClearSelection();
                    SceneView.RepaintAll();
                }
            }
        }

        private string GetSelectedName(int kind)
        {
            AudioVolume3D s = (AudioVolume3D)target;
            if (kind == KindOcclusion && s.ManualOcclusionZones != null && selectedIndex >= 0 && selectedIndex < s.ManualOcclusionZones.Count)
            {
                var z = s.ManualOcclusionZones[selectedIndex];
                return z != null ? $"#{selectedIndex} {z.Name}" : null;
            }
            if (kind == KindInner && s.InnerVolumes != null && selectedIndex >= 0 && selectedIndex < s.InnerVolumes.Count)
            {
                var v = s.InnerVolumes[selectedIndex];
                return v != null ? $"#{selectedIndex} {v.Name}" : null;
            }
            return null;
        }

        private static void ClearSelection()
        {
            selectedOwner = null;
            selectedKind = KindNone;
            selectedIndex = -1;
        }

        private void OnSceneGUI()
        {
            // 다중 편집 시 OnSceneGUI는 대상마다 호출되며 그때마다 target이 바뀐다
            script = target as AudioVolume3D;
            if (script == null)
                return;

            Transform t = script.transform;
            Matrix4x4 matrix = t.localToWorldMatrix;
            float handleScale = Mathf.Max(0.01f, script.GizmoHandleScale);
            bool repaint = Event.current.type == EventType.Repaint;

            CompareFunction prevZ = Handles.zTest;
            Color prevColor = Handles.color;

            using (new Handles.DrawingScope(matrix))
            {
                if (repaint)
                {
                    // 1패스: 가려진 부분을 반투명하게, 2패스: 보이는 부분을 원래 알파로
                    Handles.zTest = CompareFunction.Greater;
                    alphaScale = OccludedAlphaScale;
                    DrawVisuals();

                    Handles.zTest = CompareFunction.LessEqual;
                    alphaScale = 1f;
                    DrawVisuals();
                }

                // 조작 핸들은 가려져도 잡을 수 있도록 항상 표시
                Handles.zTest = CompareFunction.Always;

                if (script.ShowMainVolume)
                    DrawMainVolumeControls(matrix, handleScale);

                if (script.ShowOcclusionZones && script.ManualOcclusionZones != null && script.ManualOcclusionZones.Count > 0)
                    DrawSubVolumeControls(script.ManualOcclusionZones, KindOcclusion, script.OcclusionZoneColor, "Occlusion Zone", matrix, handleScale);

                if (script.ShowInnerVolumes && script.InnerVolumes != null && script.InnerVolumes.Count > 0)
                    DrawSubVolumeControls(script.InnerVolumes, KindInner, script.ZoneColor, "Inner Volume", matrix, handleScale);
            }

            Handles.zTest = prevZ;
            Handles.color = prevColor;

            if (script.ShowLabel && repaint)
                DrawLabel();
        }

        // ─── Visuals (Repaint 전용) ───

        private void DrawVisuals()
        {
            if (script.ShowMainVolume)
                DrawBoxFillLocal(script.VolumeCenter, script.VolumeSize, script.ZoneColor, 0.03f, 0.45f);

            if (script.ShowFadeZone && script.FadeDistance > 0f)
                DrawFadeZone();

            if (script.ShowOcclusionZones && script.ManualOcclusionZones != null)
                DrawSubVolumeVisuals(script.ManualOcclusionZones, script.OcclusionZoneColor);

            if (script.ShowInnerVolumes && script.InnerVolumes != null)
                DrawSubVolumeVisuals(script.InnerVolumes, script.ZoneColor);
        }

        private void DrawFadeZone()
        {
            Vector3 center = script.VolumeCenter;
            Vector3 fadeSize = script.VolumeSize + Vector3.one * script.FadeDistance * 2f;
            if (!script.UseHeightAttenuation)
                fadeSize.y = script.VolumeSize.y;

            DrawBoxFillLocal(center, fadeSize, script.FadeZoneColor, 0.02f, 0.35f);
        }

        private void DrawSubVolumeVisuals<T>(List<T> list, Color baseColor) where T : class, AudioVolume3D.ISubVolume
        {
            for (int i = 0; i < list.Count; i++)
            {
                T zone = list[i];
                if (zone == null) continue;

                Vector3 center = zone.LocalPosition;
                bool isSphere = zone.Shape == AudioVolume3D.VolumeShape.Sphere;
                Vector3 sizeForCheck = isSphere ? Vector3.one * zone.Radius * 2f : zone.Size;
                bool insideMain = IsBoxInsideMain(center, sizeForCheck);

                Color usedColor = insideMain ? baseColor : Color.Lerp(baseColor, Color.red, 0.65f);
                float fillAlpha = insideMain ? 0.11f : 0.18f;
                float outlineAlpha = insideMain ? 0.9f : 1f;

                if (isSphere)
                {
                    Color fillColor = usedColor;
                    fillColor.a = fillAlpha * alphaScale;
                    Color outlineColor = usedColor;
                    outlineColor.a = outlineAlpha * alphaScale;

                    Handles.color = fillColor;
                    Handles.SphereHandleCap(0, center, Quaternion.identity, zone.Radius * 2f, EventType.Repaint);

                    Handles.color = outlineColor;
                    Handles.DrawWireDisc(center, Vector3.up, zone.Radius);
                    Handles.DrawWireDisc(center, Vector3.right, zone.Radius);
                    Handles.DrawWireDisc(center, Vector3.forward, zone.Radius);
                }
                else
                {
                    DrawBoxFillLocal(center, zone.Size, usedColor, fillAlpha, outlineAlpha);
                }
            }
        }

        // ─── Controls (모든 이벤트) ───

        private void DrawMainVolumeControls(Matrix4x4 matrix, float handleScale)
        {
            Vector3 center = script.VolumeCenter;
            Vector3 size = script.VolumeSize;

            if (DrawBoxResizeHandlesLocal(ref center, ref size, script.ZoneColor, matrix, handleScale))
            {
                Undo.RecordObject(script, "Edit Main Volume");
                script.VolumeCenter = center;
                script.VolumeSize = size;
            }
        }

        private void DrawSubVolumeControls<T>(List<T> list, int kind, Color baseColor, string name, Matrix4x4 matrix, float handleScale) where T : class, AudioVolume3D.ISubVolume
        {
            for (int i = 0; i < list.Count; i++)
            {
                T zone = list[i];
                if (zone == null) continue;

                Vector3 center = zone.LocalPosition;
                bool isSphere = zone.Shape == AudioVolume3D.VolumeShape.Sphere;
                Vector3 sizeForCheck = isSphere ? Vector3.one * zone.Radius * 2f : zone.Size;
                Color usedColor = IsBoxInsideMain(center, sizeForCheck) ? baseColor : Color.Lerp(baseColor, Color.red, 0.65f);

                if (isSphere)
                {
                    float radius = zone.Radius;
                    if (DrawSphereResizeHandlesLocal(ref radius, center, usedColor, matrix, handleScale))
                    {
                        Undo.RecordObject(script, "Edit " + name);
                        zone.Radius = Mathf.Max(0.01f, radius);
                    }
                }
                else
                {
                    Vector3 size = zone.Size;
                    if (DrawBoxResizeHandlesLocal(ref center, ref size, usedColor, matrix, handleScale))
                    {
                        Undo.RecordObject(script, "Edit " + name);
                        zone.Size = size;
                        zone.LocalPosition = center;
                    }
                }

                // 이동 핸들과 중심 점은 월드 공간(단위 행렬)에서 그린다 — 스케일된 행렬 안에서는
                // 화면 크기가 스케일만큼 부풀고, DotHandleCap은 그리기와 클릭 판정 크기가 서로 달라진다
                Vector3 worldCenter = matrix.MultiplyPoint3x4(center);
                bool selected = selectedOwner == script && selectedKind == kind && selectedIndex == i;
                using (new Handles.DrawingScope(Matrix4x4.identity))
                {
                    if (selected)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 newWorldCenter = Handles.PositionHandle(worldCenter, script.transform.rotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(script, "Move " + name);
                            zone.LocalPosition = script.transform.InverseTransformPoint(newWorldCenter);
                        }
                    }
                    else
                    {
                        // 중심 점 클릭 → 이 서브 볼륨에 이동 핸들 표시
                        float dotSize = HandleUtility.GetHandleSize(worldCenter) * handleScale * 0.06f;
                        Color dotColor = usedColor;
                        dotColor.a = 1f;
                        Handles.color = dotColor;
                        // 클릭 판정 사각형을 카메라 정면으로 — identity면 위에서 볼 때 선으로 찌그러짐
                        Camera cam = Camera.current;
                        Quaternion facing = cam ? cam.transform.rotation : Quaternion.identity;
                        if (Handles.Button(worldCenter, facing, dotSize, dotSize * 1.6f, Handles.DotHandleCap))
                        {
                            selectedOwner = script;
                            selectedKind = kind;
                            selectedIndex = i;
                            Repaint();
                        }
                    }
                }
            }
        }

        private bool DrawBoxResizeHandlesLocal(ref Vector3 center, ref Vector3 size, Color color, Matrix4x4 matrix, float handleScale)
        {
            bool changed = false;

            changed |= DrawBoxAxisHandles(ref center, ref size, 0, Vector3.right, color, matrix, handleScale);
            changed |= DrawBoxAxisHandles(ref center, ref size, 1, Vector3.up, color, matrix, handleScale);
            changed |= DrawBoxAxisHandles(ref center, ref size, 2, Vector3.forward, color, matrix, handleScale);

            return changed;
        }

        private bool DrawBoxAxisHandles(ref Vector3 center, ref Vector3 size, int axisIndex, Vector3 axis, Color color, Matrix4x4 matrix, float handleScale)
        {
            bool changed = false;

            float extent = size[axisIndex] * 0.5f;
            Vector3 localPlus = center + axis * extent;
            Vector3 localMinus = center - axis * extent;

            float baseSize = LocalHandleSize(localPlus, axis, matrix) * handleScale * 0.12f;
            Color handleColor = color;
            handleColor.a = 1f;

            Handles.color = handleColor;

            EditorGUI.BeginChangeCheck();
            Vector3 newLocalPlus = Handles.Slider(localPlus, axis, baseSize, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float delta = Vector3.Dot(newLocalPlus - localPlus, axis);
                if (Mathf.Abs(delta) > 0.0001f)
                {
                    float newSize = Mathf.Max(0.01f, size[axisIndex] + delta);
                    Vector3 deltaCenter = axis * (delta * 0.5f);
                    center += deltaCenter;
                    size[axisIndex] = newSize;
                    changed = true;
                }
            }

            baseSize = LocalHandleSize(localMinus, axis, matrix) * handleScale * 0.12f;

            EditorGUI.BeginChangeCheck();
            Vector3 newLocalMinus = Handles.Slider(localMinus, -axis, baseSize, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float delta = Vector3.Dot(newLocalMinus - localMinus, -axis);
                if (Mathf.Abs(delta) > 0.0001f)
                {
                    float newSize = Mathf.Max(0.01f, size[axisIndex] + delta);
                    Vector3 deltaCenter = -axis * (delta * 0.5f);
                    center += deltaCenter;
                    size[axisIndex] = newSize;
                    changed = true;
                }
            }

            return changed;
        }

        private bool DrawSphereResizeHandlesLocal(ref float radius, Vector3 center, Color color, Matrix4x4 matrix, float handleScale)
        {
            bool changed = false;

            Vector3 axis = Vector3.right;
            Vector3 localHandlePos = center + axis * radius;

            float baseSize = LocalHandleSize(localHandlePos, axis, matrix) * handleScale * 0.12f;
            Color handleColor = color;
            handleColor.a = 1f;

            Handles.color = handleColor;

            EditorGUI.BeginChangeCheck();
            Vector3 newLocalPos = Handles.Slider(localHandlePos, axis, baseSize, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float delta = Vector3.Dot(newLocalPos - localHandlePos, axis);
                float newRadius = Mathf.Max(0.01f, radius + delta);
                if (!Mathf.Approximately(newRadius, radius))
                {
                    radius = newRadius;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// Handles.matrix(= matrix, 로컬→월드) 안에서 그릴 캡의 크기를 로컬 단위로 구한다.
        /// GetHandleSize는 Handles.matrix를 직접 적용하므로 로컬 좌표를 넘기고(월드 좌표를 넘기면 이중 변환),
        /// 캡은 행렬 스케일만큼 커져 그려지므로 해당 축의 스케일로 나눈다.
        /// </summary>
        private static float LocalHandleSize(Vector3 localPos, Vector3 localAxis, Matrix4x4 matrix)
        {
            float axisScale = matrix.MultiplyVector(localAxis).magnitude;
            return HandleUtility.GetHandleSize(localPos) / Mathf.Max(0.0001f, axisScale);
        }

        private bool IsBoxInsideMain(Vector3 subCenter, Vector3 subSize)
        {
            Vector3 mainHalf = script.VolumeSize * 0.5f;
            Vector3 subHalf = subSize * 0.5f;

            Vector3 mainMin = script.VolumeCenter - mainHalf;
            Vector3 mainMax = script.VolumeCenter + mainHalf;
            Vector3 subMin = subCenter - subHalf;
            Vector3 subMax = subCenter + subHalf;

            if (subMin.x < mainMin.x || subMax.x > mainMax.x)
                return false;
            if (subMin.y < mainMin.y || subMax.y > mainMax.y)
                return false;
            if (subMin.z < mainMin.z || subMax.z > mainMax.z)
                return false;

            return true;
        }

        private void DrawBoxFillLocal(Vector3 center, Vector3 size, Color baseColor, float fillAlpha, float outlineAlpha)
        {
            Vector3 half = size * 0.5f;

            Corners[0] = center + new Vector3(-half.x, -half.y, -half.z);
            Corners[1] = center + new Vector3(-half.x, -half.y, half.z);
            Corners[2] = center + new Vector3(half.x, -half.y, half.z);
            Corners[3] = center + new Vector3(half.x, -half.y, -half.z);
            Corners[4] = center + new Vector3(-half.x, half.y, -half.z);
            Corners[5] = center + new Vector3(-half.x, half.y, half.z);
            Corners[6] = center + new Vector3(half.x, half.y, half.z);
            Corners[7] = center + new Vector3(half.x, half.y, -half.z);

            Color fillColor = baseColor;
            fillColor.a = fillAlpha * alphaScale;
            Color outlineColor = baseColor;
            outlineColor.a = outlineAlpha * alphaScale;

            for (int f = 0; f < FaceIndices.Length; f += 4)
            {
                Face[0] = Corners[FaceIndices[f]];
                Face[1] = Corners[FaceIndices[f + 1]];
                Face[2] = Corners[FaceIndices[f + 2]];
                Face[3] = Corners[FaceIndices[f + 3]];
                Handles.DrawSolidRectangleWithOutline(Face, fillColor, outlineColor);
            }
        }

        private void DrawLabel()
        {
            Vector3 worldCenter = script.transform.TransformPoint(script.VolumeCenter);
            // WorldToGUIPoint는 Vector2를 반환해 z가 항상 0 — 기존 z>0 판정으로는 라벨이 절대 그려지지 않았음
            Vector3 screenPos = HandleUtility.WorldToGUIPointWithDepth(worldCenter);
            if (screenPos.z <= 0f) return;

            EnsureLabelStyles();
            Handles.BeginGUI();
            // 밝은 배경에서도 읽히도록 그림자를 먼저 그린다
            Rect rect = new Rect(screenPos.x - 100f, screenPos.y - 10f, 200f, 20f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), script.name, labelShadowStyle);
            GUI.Label(rect, script.name, labelStyle);
            Handles.EndGUI();
        }

        private static void EnsureLabelStyles()
        {
            if (labelStyle != null) return;

            labelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Overflow
            };
            labelStyle.normal.textColor = Color.white;

            labelShadowStyle = new GUIStyle(labelStyle);
            labelShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        }
    }
}
