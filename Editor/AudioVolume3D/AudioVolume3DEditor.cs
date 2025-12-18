using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine.Rendering;

namespace TelleR
{
    [CustomEditor(typeof(AudioVolume3D))]
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
        private SerializedProperty targetTransformProp;
        private SerializedProperty targetTagProp;
        private SerializedProperty gizmoHandleScaleProp;

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

            targetTransformProp = serializedObject.FindProperty("TargetTransform");
            targetTagProp = serializedObject.FindProperty("TargetTag");
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
            EditorGUILayout.PropertyField(targetTransformProp);
            EditorGUILayout.PropertyField(targetTagProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(volumeCenterProp);
            EditorGUILayout.PropertyField(volumeSizeProp);
            EditorGUILayout.PropertyField(fadeDistanceProp);
            EditorGUILayout.PropertyField(useHeightAttenuationProp);
            EditorGUILayout.PropertyField(maxVolumeProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(fadeInSpeedProp);
            EditorGUILayout.PropertyField(fadeOutSpeedProp);
        }

        private void DrawAudioTab()
        {
            EditorGUILayout.PropertyField(clipProp);
            EditorGUILayout.PropertyField(outputGroupProp);
            EditorGUILayout.PropertyField(spatialBlendProp);
            EditorGUILayout.PropertyField(autoSpatialBlendProp);
            EditorGUILayout.PropertyField(minDistanceProp);
            EditorGUILayout.PropertyField(maxDistanceProp);
            EditorGUILayout.PropertyField(loopProp);
            EditorGUILayout.PropertyField(playOnAwakeProp);
        }

        private void DrawOcclusionTab()
        {
            EditorGUILayout.PropertyField(occlusionSmoothSpeedProp);
            EditorGUILayout.PropertyField(manualOcclusionZonesProp, true);
        }

        private void DrawInnerVolumesTab()
        {
            EditorGUILayout.PropertyField(innerVolumesProp, true);
        }

        private void DrawVisualsTab()
        {
            EditorGUILayout.PropertyField(showMainVolumeProp);
            EditorGUILayout.PropertyField(showFadeZoneProp);
            EditorGUILayout.PropertyField(showInnerVolumesProp);
            EditorGUILayout.PropertyField(showOcclusionZonesProp);
            EditorGUILayout.PropertyField(showLabelProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(zoneColorProp);
            EditorGUILayout.PropertyField(fadeZoneColorProp);
            EditorGUILayout.PropertyField(occlusionZoneColorProp);
            EditorGUILayout.Space();
            gizmoHandleScaleProp.floatValue = EditorGUILayout.Slider("Handle Size", gizmoHandleScaleProp.floatValue, 0.01f, 3f);
        }

        private void OnSceneGUI()
        {
            if (script == null)
                return;

            Transform t = script.transform;
            Matrix4x4 matrix = t.localToWorldMatrix;
            float handleScale = Mathf.Max(0.01f, script.GizmoHandleScale);

            CompareFunction prevZ = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;

            using (new Handles.DrawingScope(matrix))
            {
                if (script.ShowMainVolume)
                    DrawMainVolume(matrix, handleScale);

                if (script.ShowFadeZone && script.FadeDistance > 0f)
                    DrawFadeZone();

                if (script.ShowOcclusionZones && script.ManualOcclusionZones != null && script.ManualOcclusionZones.Count > 0)
                    DrawSubVolumeHandles(script.ManualOcclusionZones, script.OcclusionZoneColor, "Occlusion", matrix, handleScale);

                if (script.ShowInnerVolumes && script.InnerVolumes != null && script.InnerVolumes.Count > 0)
                    DrawSubVolumeHandles(script.InnerVolumes, script.ZoneColor, "Inner", matrix, handleScale);
            }

            Handles.zTest = prevZ;

            if (script.ShowLabel)
                DrawLabel();
        }

        private void DrawMainVolume(Matrix4x4 matrix, float handleScale)
        {
            Vector3 center = script.VolumeCenter;
            Vector3 size = script.VolumeSize;

            DrawBoxFillLocal(center, size, script.ZoneColor, 0.03f, 0.45f);

            if (DrawBoxResizeHandlesLocal(ref center, ref size, script.ZoneColor, matrix, handleScale))
            {
                Undo.RecordObject(script, "Edit Main Volume");
                script.VolumeCenter = center;
                script.VolumeSize = size;
            }
        }

        private void DrawFadeZone()
        {
            Vector3 center = script.VolumeCenter;
            Vector3 fadeSize = script.VolumeSize + Vector3.one * script.FadeDistance * 2f;
            if (!script.UseHeightAttenuation)
                fadeSize.y = script.VolumeSize.y;

            DrawBoxFillLocal(center, fadeSize, script.FadeZoneColor, 0.02f, 0.35f);
        }

        private void DrawSubVolumeHandles<T>(System.Collections.Generic.List<T> list, Color baseColor, string name, Matrix4x4 matrix, float handleScale)
        {
            for (int i = 0; i < list.Count; i++)
            {
                dynamic zone = list[i];

                Vector3 center = zone.LocalPosition;
                Vector3 sizeForCheck = zone.Shape == AudioVolume3D.VolumeShape.Sphere
                    ? Vector3.one * zone.Radius * 2f
                    : zone.Size;

                bool insideMain = IsBoxInsideMain(center, sizeForCheck);

                Color innerColor = baseColor;
                Color leakColor = Color.Lerp(baseColor, Color.red, 0.65f);
                Color usedColor = insideMain ? innerColor : leakColor;

                float fillAlpha = insideMain ? 0.11f : 0.18f;
                float outlineAlpha = insideMain ? 0.9f : 1f;

                if (zone.Shape == AudioVolume3D.VolumeShape.Sphere)
                {
                    Color fillColor = usedColor;
                    fillColor.a = fillAlpha;
                    Color outlineColor = usedColor;
                    outlineColor.a = outlineAlpha;

                    Handles.color = fillColor;
                    Handles.SphereHandleCap(0, center, Quaternion.identity, zone.Radius * 2f, EventType.Repaint);

                    Handles.color = outlineColor;
                    Handles.DrawWireDisc(center, Vector3.up, zone.Radius);
                    Handles.DrawWireDisc(center, Vector3.right, zone.Radius);
                    Handles.DrawWireDisc(center, Vector3.forward, zone.Radius);

                    float radius = zone.Radius;
                    if (DrawSphereResizeHandlesLocal(ref radius, center, usedColor, matrix, handleScale))
                    {
                        Undo.RecordObject(script, "Edit " + name);
                        zone.Radius = Mathf.Max(0.01f, radius);
                        list[i] = (T)zone;
                    }

                    EditorGUI.BeginChangeCheck();
                    Vector3 newLocalCenter = Handles.PositionHandle(center, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(script, "Move " + name);
                        zone.LocalPosition = newLocalCenter;
                        list[i] = (T)zone;
                    }
                }
                else
                {
                    DrawBoxFillLocal(center, zone.Size, usedColor, fillAlpha, outlineAlpha);

                    Vector3 size = zone.Size;
                    if (DrawBoxResizeHandlesLocal(ref center, ref size, usedColor, matrix, handleScale))
                    {
                        Undo.RecordObject(script, "Edit " + name);
                        zone.Size = size;
                        zone.LocalPosition = center;
                        list[i] = (T)zone;
                    }

                    EditorGUI.BeginChangeCheck();
                    Vector3 newLocalCenter = Handles.PositionHandle(center, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(script, "Move " + name);
                        zone.LocalPosition = newLocalCenter;
                        list[i] = (T)zone;
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

            Vector3 worldPlus = matrix.MultiplyPoint3x4(localPlus);

            float baseSize = HandleUtility.GetHandleSize(worldPlus) * handleScale * 0.12f;
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

            Vector3 worldMinus = matrix.MultiplyPoint3x4(localMinus);
            baseSize = HandleUtility.GetHandleSize(worldMinus) * handleScale * 0.12f;

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
            Vector3 worldPos = matrix.MultiplyPoint3x4(localHandlePos);

            float baseSize = HandleUtility.GetHandleSize(worldPos) * handleScale * 0.12f;
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

            Vector3 p0 = center + new Vector3(-half.x, -half.y, -half.z);
            Vector3 p1 = center + new Vector3(-half.x, -half.y, half.z);
            Vector3 p2 = center + new Vector3(half.x, -half.y, half.z);
            Vector3 p3 = center + new Vector3(half.x, -half.y, -half.z);
            Vector3 p4 = center + new Vector3(-half.x, half.y, -half.z);
            Vector3 p5 = center + new Vector3(-half.x, half.y, half.z);
            Vector3 p6 = center + new Vector3(half.x, half.y, half.z);
            Vector3 p7 = center + new Vector3(half.x, half.y, -half.z);

            Color fillColor = baseColor;
            fillColor.a = fillAlpha;
            Color outlineColor = baseColor;
            outlineColor.a = outlineAlpha;

            Handles.DrawSolidRectangleWithOutline(new[] { p0, p1, p2, p3 }, fillColor, outlineColor);
            Handles.DrawSolidRectangleWithOutline(new[] { p4, p5, p6, p7 }, fillColor, outlineColor);
            Handles.DrawSolidRectangleWithOutline(new[] { p0, p1, p5, p4 }, fillColor, outlineColor);
            Handles.DrawSolidRectangleWithOutline(new[] { p2, p3, p7, p6 }, fillColor, outlineColor);
            Handles.DrawSolidRectangleWithOutline(new[] { p1, p2, p6, p5 }, fillColor, outlineColor);
            Handles.DrawSolidRectangleWithOutline(new[] { p3, p0, p4, p7 }, fillColor, outlineColor);
        }

        private void DrawLabel()
        {
            Vector3 worldCenter = script.transform.TransformPoint(script.VolumeCenter);
            Handles.BeginGUI();
            Vector3 screenPos = HandleUtility.WorldToGUIPoint(worldCenter);
            if (screenPos.z > 0f)
            {
                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = Color.white;
                style.alignment = TextAnchor.MiddleCenter;
                style.fontSize = 12;
                style.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(screenPos.x - 50f, screenPos.y - 10f, 100f, 20f), script.name, style);
            }

            Handles.EndGUI();
        }
    }
}
