#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TelleR
{
    [CustomEditor(typeof(AnimationInspectorController))]
    public class AnimationInspectorControllerEditor : Editor
    {
        private const string MainTabPrefKey = "TelleR.AnimInspector.MainTab";
        private const string PreviewFoldoutPrefKey = "TelleR.AnimInspector.PreviewFoldout";
        private const string WorkspaceSubTabPrefKey = "TelleR.AnimInspector.WorkspaceSubTab";
        private const string TransitionFoldoutPrefKey = "TelleR.AnimInspector.TransitionFoldout";
        private const string FrameEventsFoldoutPrefKey = "TelleR.AnimInspector.FrameEventsFoldout";

        private static readonly string[] mainTabs = { "Workspace", "Settings" };
        private static readonly string[] workspaceTabs = { "Transitions", "Frame Events" };

        private AnimationInspectorController ctrl;

        private int mainTabIndex;
        private int workspaceTabIndex;

        private bool stylesReady;
        private GUIStyle card;
        private GUIStyle sectionBox;
        private GUIStyle miniLabelStyle;
        private GUIStyle clipInfoStyle;

        private bool previewFoldout = true;
        private bool transitionFoldout = true;
        private bool frameEventsFoldout = true;

        private Vector2 transitionListScroll;
        private int transitionPage;

        private string[] availableStates = Array.Empty<string>();
        private List<ClipCatalog.ClipInfo> clipInfos = new List<ClipCatalog.ClipInfo>();
        private List<ClipCatalog.ClipInfo> visibleClips = new List<ClipCatalog.ClipInfo>();
        private List<ClipCatalog.ClipInfo> hiddenClipInfos = new List<ClipCatalog.ClipInfo>();
        private int currentClipIndex = -1;
        private bool showClipList;
        private bool showHiddenClips;
        private int gridColumns = 3;

        private int eventPage;
        private Vector2 eventsListScroll;

        private readonly Color timerActiveColor = new Color(0.3f, 0.9f, 0.4f, 1f);
        private readonly Color timerStoppedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private readonly Color eventMarkerColor = new Color(1f, 0.6f, 0.2f, 1f);
        private readonly Color selectedEventMarkerColor = new Color(0.2f, 0.8f, 1f, 1f);
        private readonly Color currentFrameColor = new Color(0.3f, 0.9f, 0.4f, 1f);

        // Edit Mode Preview State (AnimationMode 기반)
        private enum EditorPlayState { Stopped, Playing, Paused }
        private EditorPlayState editorPlayState = EditorPlayState.Stopped;
        private int editorCurrentFrame;
        private float editorPlaybackSpeed = 1f;
        private double lastEditorTime;
        private float frameAccumulator;
        private bool isAnimationModeActive;

        private void OnEnable()
        {
            ctrl = (AnimationInspectorController)target;

            mainTabIndex = Mathf.Clamp(EditorPrefs.GetInt(MainTabPrefKey, 0), 0, mainTabs.Length - 1);
            workspaceTabIndex = Mathf.Clamp(EditorPrefs.GetInt(WorkspaceSubTabPrefKey, 0), 0, workspaceTabs.Length - 1);

            previewFoldout = EditorPrefs.GetBool(PreviewFoldoutPrefKey, true);
            transitionFoldout = EditorPrefs.GetBool(TransitionFoldoutPrefKey, true);
            frameEventsFoldout = EditorPrefs.GetBool(FrameEventsFoldoutPrefKey, true);

            RefreshAvailableStates();
            RefreshClipList();

            EditorApplication.update += OnEditorUpdate;
            lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopAnimationModeIfActive();
        }

        private void OnEditorUpdate()
        {
            if (Application.isPlaying) return;
            if (ctrl == null || ctrl.CurrentClip == null) return;
            if (editorPlayState != EditorPlayState.Playing) return;

            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastEditorTime), 0f, 0.1f);
            lastEditorTime = now;

            float fps = Mathf.Max(1f, ctrl.CurrentClip.frameRate);
            float frameDuration = 1f / fps;

            frameAccumulator += dt * editorPlaybackSpeed;

            while (frameAccumulator >= frameDuration)
            {
                frameAccumulator -= frameDuration;
                editorCurrentFrame++;

                int maxFrame = GetMaxFrame();
                if (editorCurrentFrame > maxFrame)
                {
                    var playbackProp = serializedObject.FindProperty("playback");
                    bool loop = playbackProp?.FindPropertyRelative("Loop")?.boolValue ?? true;

                    if (loop)
                        editorCurrentFrame = 0;
                    else
                    {
                        editorCurrentFrame = maxFrame;
                        editorPlayState = EditorPlayState.Stopped;
                        break;
                    }
                }
            }

            SampleCurrentFrame();
            Repaint();
        }

        private void StartAnimationModeIfNeeded()
        {
            if (isAnimationModeActive) return;
            if (ctrl == null || ctrl.AnimatorComponent == null) return;

            AnimationMode.StartAnimationMode();
            isAnimationModeActive = true;
        }

        private void StopAnimationModeIfActive()
        {
            if (!isAnimationModeActive) return;

            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();

            isAnimationModeActive = false;
        }

        private void SampleCurrentFrame()
        {
            if (ctrl == null || ctrl.CurrentClip == null || ctrl.AnimatorComponent == null) return;

            StartAnimationModeIfNeeded();

            if (!AnimationMode.InAnimationMode()) return;

            float fps = Mathf.Max(1f, ctrl.CurrentClip.frameRate);
            float time = editorCurrentFrame / fps;

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(ctrl.AnimatorComponent.gameObject, ctrl.CurrentClip, time);
            AnimationMode.EndSampling();

            SceneView.RepaintAll();
        }

        private int GetMaxFrame()
        {
            if (ctrl == null || ctrl.CurrentClip == null) return 0;
            return Mathf.Max(0, Mathf.RoundToInt(ctrl.CurrentClip.length * ctrl.CurrentClip.frameRate));
        }

        public override void OnInspectorGUI()
        {
            if (ctrl == null)
                ctrl = (AnimationInspectorController)target;

            serializedObject.Update();
            InitStyles();

            GUILayout.Space(2);
            GUILayout.BeginVertical(card);

            DrawAnimatorField();
            DrawClipSelector();

            GUILayout.Space(8);
            int newMainTab = GUILayout.Toolbar(mainTabIndex, mainTabs, GUILayout.Height(24));
            if (newMainTab != mainTabIndex)
            {
                mainTabIndex = newMainTab;
                EditorPrefs.SetInt(MainTabPrefKey, mainTabIndex);
            }

            GUILayout.Space(8);

            if (mainTabIndex == 0)
                DrawTabWorkspace();
            else
                DrawTabSettings();

            GUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }

        private void InitStyles()
        {
            if (stylesReady) return;

            card = new GUIStyle("box") { padding = new RectOffset(14, 14, 14, 14) };
            sectionBox = new GUIStyle("HelpBox") 
            { 
                padding = new RectOffset(12, 12, 12, 14), 
                margin = new RectOffset(0, 0, 8, 8) 
            };

            miniLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(4, 4, 4, 6),
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.65f, 0.65f, 0.65f, 1f)
                        : new Color(0.35f, 0.35f, 0.35f, 1f)
                }
            };

            clipInfoStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 2, 2),
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.9f, 0.75f, 0.4f, 1f)
                        : new Color(0.6f, 0.4f, 0.1f, 1f)
                }
            };

            stylesReady = true;
        }

        private void DrawAnimatorField()
        {
            var animProp = serializedObject.FindProperty("animator");
            var newAnim = (Animator)EditorGUILayout.ObjectField("Animator", animProp.objectReferenceValue, typeof(Animator), true);

            if (newAnim != (Animator)animProp.objectReferenceValue)
            {
                StopAnimationModeIfActive();
                animProp.objectReferenceValue = newAnim;
                serializedObject.ApplyModifiedProperties();

                RefreshAvailableStates();
                RefreshClipList();
            }
        }

        private void RefreshAvailableStates()
        {
            var states = new List<string>();

            if (ctrl != null && ctrl.AnimatorComponent != null && ctrl.AnimatorComponent.runtimeAnimatorController != null)
            {
                var controller = ctrl.AnimatorComponent.runtimeAnimatorController as AnimatorController;
                if (controller != null)
                {
                    foreach (var layer in controller.layers)
                    {
                        foreach (var child in layer.stateMachine.states)
                            states.Add(child.state.name);
                    }
                }
            }

            availableStates = states.ToArray();
        }

        private void RefreshClipList()
        {
            if (ctrl != null && ctrl.AnimatorComponent != null && ctrl.AnimatorComponent.runtimeAnimatorController != null)
            {
                clipInfos = ClipCatalog.CollectWithInfo(ctrl.AnimatorComponent);
                FilterClipsByVisibility();
                UpdateCurrentClipIndex();
            }
            else
            {
                clipInfos = new List<ClipCatalog.ClipInfo>();
                visibleClips = new List<ClipCatalog.ClipInfo>();
                hiddenClipInfos = new List<ClipCatalog.ClipInfo>();
                currentClipIndex = -1;
            }
        }

        private void FilterClipsByVisibility()
        {
            var hiddenProp = serializedObject.FindProperty("hiddenClips");
            var hiddenSet = new HashSet<string>();

            if (hiddenProp != null)
            {
                for (int i = 0; i < hiddenProp.arraySize; i++)
                    hiddenSet.Add(hiddenProp.GetArrayElementAtIndex(i).stringValue);
            }

            visibleClips = new List<ClipCatalog.ClipInfo>();
            hiddenClipInfos = new List<ClipCatalog.ClipInfo>();

            foreach (var info in clipInfos)
            {
                string key = $"{info.LayerName}/{info.StateName}";
                if (hiddenSet.Contains(key))
                    hiddenClipInfos.Add(info);
                else
                    visibleClips.Add(info);
            }
        }

        private void UpdateCurrentClipIndex()
        {
            if (visibleClips == null || ctrl == null || ctrl.CurrentClip == null)
            {
                currentClipIndex = -1;
                return;
            }

            currentClipIndex = visibleClips.FindIndex(c => c.Clip == ctrl.CurrentClip);
        }

        private void DrawClipSelector()
        {
            if (ctrl == null || ctrl.AnimatorComponent == null || ctrl.AnimatorComponent.runtimeAnimatorController == null)
            {
                EditorGUILayout.HelpBox("Animator Controller가 없습니다.", MessageType.Warning);
                return;
            }

            if (clipInfos == null || clipInfos.Count == 0)
            {
                EditorGUILayout.HelpBox("Animator Controller에 클립이 없습니다.", MessageType.Info);
                return;
            }

            int visibleCount = visibleClips?.Count ?? 0;
            int hiddenCount = hiddenClipInfos?.Count ?? 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel($"Clip ({visibleCount}/{clipInfos.Count})");

            using (new EditorGUI.DisabledScope(visibleCount <= 1))
            {
                if (GUILayout.Button("<", EditorStyles.miniButtonLeft, GUILayout.Width(24)))
                    NavigateClip(-1);
            }

            var currentClip = ctrl.CurrentClip;
            string buttonLabel = "Select Clip...";
            if (currentClip != null && currentClipIndex >= 0 && currentClipIndex < visibleCount)
            {
                var info = visibleClips[currentClipIndex];
                int frames = Mathf.RoundToInt(info.Clip.length * info.Clip.frameRate);
                buttonLabel = $"{info.StateName} ({frames}f @ {info.Clip.frameRate:0}fps)";
            }
            else if (currentClip != null)
            {
                buttonLabel = currentClip.name;
            }

            if (GUILayout.Button(buttonLabel, EditorStyles.miniButtonMid))
            {
                var menu = new GenericMenu();
                for (int i = 0; i < visibleCount; i++)
                {
                    var info = visibleClips[i];
                    if (info.Clip == null) continue;

                    int frames = Mathf.RoundToInt(info.Clip.length * info.Clip.frameRate);
                    string menuLabel = $"{info.LayerName}/{info.StateName} ({frames}f)";
                    if (info.IsDefault) menuLabel += " ★";

                    var capturedInfo = info;
                    menu.AddItem(new GUIContent(menuLabel), info.Clip == currentClip, () =>
                    {
                        SelectClipWithState(capturedInfo);
                    });
                }
                menu.ShowAsContext();
            }

            using (new EditorGUI.DisabledScope(visibleCount <= 1))
            {
                if (GUILayout.Button(">", EditorStyles.miniButtonRight, GUILayout.Width(24)))
                    NavigateClip(1);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            showClipList = EditorGUILayout.Foldout(showClipList, $"Clips ({visibleCount})", true);
            GUILayout.FlexibleSpace();
            if (hiddenCount > 0)
                showHiddenClips = EditorGUILayout.Foldout(showHiddenClips, $"Hidden ({hiddenCount})", true);
            EditorGUILayout.EndHorizontal();

            if (showClipList && visibleCount > 0)
                DrawClipGrid(visibleClips, false);

            if (showHiddenClips && hiddenCount > 0)
                DrawClipGrid(hiddenClipInfos, true);
        }

        private void DrawClipGrid(List<ClipCatalog.ClipInfo> clips, bool isHiddenList)
        {
            GUILayout.BeginVertical(sectionBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(isHiddenList ? "Hidden Clips" : "Available Clips", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUILayout.Label("Columns:", EditorStyles.miniLabel);
            int newColumns = EditorGUILayout.IntSlider(gridColumns, 2, 5, GUILayout.Width(120));
            if (newColumns != gridColumns) gridColumns = newColumns;

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            var currentClip = ctrl.CurrentClip;
            int count = clips.Count;
            int rows = Mathf.CeilToInt((float)count / gridColumns);
            float buttonWidth = (EditorGUIUtility.currentViewWidth - 60) / gridColumns;

            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();

                for (int col = 0; col < gridColumns; col++)
                {
                    int idx = row * gridColumns + col;
                    if (idx >= count)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    var info = clips[idx];
                    if (info.Clip == null)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    var prevBg = GUI.backgroundColor;

                    if (isHiddenList)
                        GUI.backgroundColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                    else if (info.Clip == currentClip)
                        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 1f);
                    else if (info.IsDefault)
                        GUI.backgroundColor = new Color(0.5f, 0.5f, 0.8f, 1f);

                    int frames = Mathf.RoundToInt(info.Clip.length * info.Clip.frameRate);

                    GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(buttonWidth - 4), GUILayout.Height(62));

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(info.StateName, EditorStyles.boldLabel, GUILayout.Height(16));
                    GUILayout.FlexibleSpace();

                    if (isHiddenList)
                    {
                        if (GUILayout.Button("↩", GUILayout.Width(22), GUILayout.Height(16)))
                        {
                            RestoreClip(info);
                            GUIUtility.ExitGUI();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(16)))
                        {
                            HideClip(info);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"{frames}f @ {info.Clip.frameRate:0}fps", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(4);

                    if (!isHiddenList)
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("▶ Select", EditorStyles.miniButton, GUILayout.Height(16)))
                        {
                            SelectClipWithState(capturedInfo: info);
                            GUIUtility.ExitGUI();
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    GUILayout.EndVertical();
                    GUI.backgroundColor = prevBg;
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            if (isHiddenList && count > 0)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Restore All", GUILayout.Height(22)))
                    RestoreAllClips();
            }

            GUILayout.EndVertical();
        }

        private void HideClip(ClipCatalog.ClipInfo info)
        {
            var hiddenProp = serializedObject.FindProperty("hiddenClips");
            if (hiddenProp == null) return;

            string key = $"{info.LayerName}/{info.StateName}";

            for (int i = 0; i < hiddenProp.arraySize; i++)
            {
                if (hiddenProp.GetArrayElementAtIndex(i).stringValue == key)
                    return;
            }

            hiddenProp.arraySize++;
            hiddenProp.GetArrayElementAtIndex(hiddenProp.arraySize - 1).stringValue = key;

            serializedObject.ApplyModifiedProperties();
            FilterClipsByVisibility();
            UpdateCurrentClipIndex();
        }

        private void RestoreClip(ClipCatalog.ClipInfo info)
        {
            var hiddenProp = serializedObject.FindProperty("hiddenClips");
            if (hiddenProp == null) return;

            string key = $"{info.LayerName}/{info.StateName}";

            for (int i = 0; i < hiddenProp.arraySize; i++)
            {
                if (hiddenProp.GetArrayElementAtIndex(i).stringValue == key)
                {
                    hiddenProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    break;
                }
            }

            FilterClipsByVisibility();
            UpdateCurrentClipIndex();
        }

        private void RestoreAllClips()
        {
            var hiddenProp = serializedObject.FindProperty("hiddenClips");
            if (hiddenProp == null) return;

            hiddenProp.ClearArray();
            serializedObject.ApplyModifiedProperties();

            FilterClipsByVisibility();
            UpdateCurrentClipIndex();
        }

        private void NavigateClip(int direction)
        {
            if (visibleClips == null || visibleClips.Count == 0) return;

            int newIndex = currentClipIndex + direction;
            if (newIndex < 0) newIndex = visibleClips.Count - 1;
            if (newIndex >= visibleClips.Count) newIndex = 0;

            SelectClipWithState(visibleClips[newIndex]);
        }

        private void SelectClipWithState(ClipCatalog.ClipInfo capturedInfo)
        {
            if (ctrl == null) return;

            StopAnimationModeIfActive();
            editorPlayState = EditorPlayState.Stopped;

            var clipProp = serializedObject.FindProperty("clip");
            if (clipProp != null)
            {
                clipProp.objectReferenceValue = capturedInfo.Clip;
                serializedObject.ApplyModifiedProperties();
            }

            editorCurrentFrame = 0;
            frameAccumulator = 0f;

            FrameStateInAnimator(capturedInfo);

            transitionPage = 0;
            UpdateCurrentClipIndex();

            // 선택 후 바로 0프레임 샘플링
            SampleCurrentFrame();

            // 자동 재생
            EditorPlay();
        }

        private void FrameStateInAnimator(ClipCatalog.ClipInfo info)
        {
            // 아무 동작 안 함 - Animator 창 열지 않음
        }

        private void DrawTabWorkspace()
        {
            DrawPreviewControlsSection();

            GUILayout.Space(6);

            int newWorkspaceTab = GUILayout.Toolbar(workspaceTabIndex, workspaceTabs, GUILayout.Height(22));
            if (newWorkspaceTab != workspaceTabIndex)
            {
                workspaceTabIndex = newWorkspaceTab;
                EditorPrefs.SetInt(WorkspaceSubTabPrefKey, workspaceTabIndex);
            }

            GUILayout.Space(6);

            if (workspaceTabIndex == 0)
                DrawAutoTransitionsPanel();
            else
                DrawFrameEventsPanel();
        }

        private void DrawPreviewControlsSection()
        {
            GUILayout.BeginVertical(sectionBox);

            EditorGUILayout.BeginHorizontal();
            previewFoldout = EditorGUILayout.Foldout(previewFoldout, "Preview", true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (GUI.changed)
                EditorPrefs.SetBool(PreviewFoldoutPrefKey, previewFoldout);

            if (!previewFoldout)
            {
                GUILayout.EndVertical();
                return;
            }

            if (ctrl == null || ctrl.CurrentClip == null)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox("선택된 클립이 없습니다.", MessageType.Info);
                GUILayout.EndVertical();
                return;
            }

            if (Application.isPlaying)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox("Play Mode에서는 런타임 재생이 동작합니다.", MessageType.Info);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(6);

            DrawCurrentClipInfo();

            GUILayout.Space(10);

            // Speed 슬라이더
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Speed", GUILayout.Width(50));
            editorPlaybackSpeed = EditorGUILayout.Slider(editorPlaybackSpeed, 0f, 4f, GUILayout.Height(20));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Play / Pause / Stop 버튼
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            string playLabel = editorPlayState == EditorPlayState.Playing ? "⏸  Pause" : "▶  Play";
            if (GUILayout.Button(playLabel, GUILayout.Width(110), GUILayout.Height(30)))
            {
                if (editorPlayState == EditorPlayState.Playing)
                    EditorPause();
                else
                    EditorPlay();
            }

            GUILayout.Space(8);

            if (GUILayout.Button("■  Stop", GUILayout.Width(90), GUILayout.Height(30)))
            {
                EditorStop();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            DrawFrameControlsWithEventMarkers();

            GUILayout.Space(4);
            GUILayout.EndVertical();
        }

        private void EditorPlay()
        {
            if (ctrl == null || ctrl.CurrentClip == null) return;

            editorPlayState = EditorPlayState.Playing;
            lastEditorTime = EditorApplication.timeSinceStartup;
            frameAccumulator = 0f;

            StartAnimationModeIfNeeded();
            SampleCurrentFrame();
        }

        private void EditorPause()
        {
            editorPlayState = EditorPlayState.Paused;
        }

        private void EditorStop()
        {
            editorPlayState = EditorPlayState.Stopped;
            editorCurrentFrame = 0;
            frameAccumulator = 0f;

            SampleCurrentFrame();
        }

        private void EditorJumpToFrame(int frame)
        {
            int maxFrame = GetMaxFrame();
            editorCurrentFrame = Mathf.Clamp(frame, 0, maxFrame);
            frameAccumulator = 0f;

            SampleCurrentFrame();
            Repaint();
        }

        private void DrawCurrentClipInfo()
        {
            if (ctrl == null || ctrl.CurrentClip == null) return;

            var clip = ctrl.CurrentClip;
            int maxFrame = GetMaxFrame();
            float fps = Mathf.Max(1f, clip.frameRate);
            float currentTime = editorCurrentFrame / fps;
            float totalTime = clip.length;

            var frameInfoStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            frameInfoStyle.normal.textColor = currentFrameColor;

            string frameInfo = $"Frame {editorCurrentFrame} / {maxFrame}   |   {currentTime:0.000}s / {totalTime:0.000}s   |   {fps:0.#} fps";
            GUILayout.Label(frameInfo, frameInfoStyle);
        }

        private void DrawFrameControlsWithEventMarkers()
        {
            int maxFrame = GetMaxFrame();
            int current = Mathf.Clamp(editorCurrentFrame, 0, maxFrame);
            int target = current;

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("-10", GUILayout.Width(40), GUILayout.Height(24)))
                target = Mathf.Clamp(current - 10, 0, maxFrame);

            if (GUILayout.Button("-1", GUILayout.Width(34), GUILayout.Height(24)))
                target = Mathf.Clamp(current - 1, 0, maxFrame);

            Rect sliderRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.horizontalSlider, GUILayout.Height(24), GUILayout.ExpandWidth(true));

            int sliderVal = Mathf.RoundToInt(GUI.HorizontalSlider(sliderRect, current, 0, maxFrame));
            if (sliderVal != current)
                target = sliderVal;

            if (GUILayout.Button("+1", GUILayout.Width(34), GUILayout.Height(24)))
                target = Mathf.Clamp(current + 1, 0, maxFrame);

            if (GUILayout.Button("+10", GUILayout.Width(40), GUILayout.Height(24)))
                target = Mathf.Clamp(current + 10, 0, maxFrame);

            EditorGUILayout.EndHorizontal();

            // Frame Events 마커 그리기
            var frameEvents = ctrl.FrameEvents;
            if (frameEvents != null && frameEvents.Count > 0 && maxFrame > 0)
            {
                DrawEventMarkersOnTimeline(sliderRect, maxFrame, current, frameEvents);
            }
            else
            {
                GUILayout.Space(8);
            }

            if (target != current)
            {
                EditorJumpToFrame(target);
            }
        }

        private void DrawEventMarkersOnTimeline(Rect sliderRect, int maxFrame, int currentFrame, FrameEventSystem frameEvents)
        {
            if (frameEvents == null || frameEvents.Count == 0) return;

            float sliderPadding = 7f;
            float usableWidth = sliderRect.width - sliderPadding * 2;

            Rect markerArea = new Rect(sliderRect.x, sliderRect.yMax + 2, sliderRect.width, 18);

            var groupedEvents = new Dictionary<int, List<int>>();
            for (int i = 0; i < frameEvents.Count; i++)
            {
                var ev = frameEvents.GetEvent(i);
                if (ev == null) continue;

                int frame = Mathf.Clamp(ev.Frame, 0, maxFrame);
                if (!groupedEvents.ContainsKey(frame))
                    groupedEvents[frame] = new List<int>();
                groupedEvents[frame].Add(i);
            }

            foreach (var kvp in groupedEvents)
            {
                int frame = kvp.Key;
                var eventIndices = kvp.Value;

                float normalizedPos = maxFrame > 0 ? (float)frame / maxFrame : 0f;
                float xPos = sliderRect.x + sliderPadding + normalizedPos * usableWidth;

                bool isSelected = eventIndices.Contains(eventPage);
                bool isCurrentFrame = frame == currentFrame;

                Color markerColor = isSelected ? selectedEventMarkerColor : eventMarkerColor;
                if (isCurrentFrame && !isSelected)
                    markerColor = currentFrameColor;

                Rect triangleRect = new Rect(xPos - 5, markerArea.y, 10, 10);

                var prevColor = GUI.color;
                GUI.color = markerColor;

                DrawTriangleMarker(triangleRect);

                GUI.color = prevColor;

                Rect clickRect = new Rect(xPos - 8, markerArea.y - 2, 16, 14);
                if (Event.current.type == EventType.MouseDown && clickRect.Contains(Event.current.mousePosition))
                {
                    if (eventIndices.Count == 1)
                    {
                        eventPage = eventIndices[0];
                        EditorJumpToFrame(frame);
                    }
                    else
                    {
                        var menu = new GenericMenu();
                        foreach (int idx in eventIndices)
                        {
                            var ev = frameEvents.GetEvent(idx);
                            string label = string.IsNullOrEmpty(ev.Label) ? $"Event #{idx + 1}" : ev.Label;
                            int capturedIdx = idx;
                            int capturedFrame = frame;
                            menu.AddItem(new GUIContent($"#{idx + 1}: {label}"), idx == eventPage, () =>
                            {
                                eventPage = capturedIdx;
                                EditorJumpToFrame(capturedFrame);
                            });
                        }
                        menu.ShowAsContext();
                    }
                    Event.current.Use();
                    Repaint();
                }

                string countLabel = eventIndices.Count > 1 ? $"{eventIndices.Count}" : "";
                if (!string.IsNullOrEmpty(countLabel))
                {
                    var countStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 8,
                        normal = { textColor = Color.white }
                    };
                    Rect countRect = new Rect(xPos - 6, triangleRect.yMax - 2, 12, 10);
                    GUI.Label(countRect, countLabel, countStyle);
                }
            }

            GUILayout.Space(20);
        }

        private void DrawTriangleMarker(Rect rect)
        {
            Vector3[] points = new Vector3[3];
            points[0] = new Vector3(rect.x + rect.width / 2, rect.y, 0);
            points[1] = new Vector3(rect.x, rect.yMax, 0);
            points[2] = new Vector3(rect.xMax, rect.yMax, 0);

            Handles.BeginGUI();
            Handles.color = GUI.color;
            Handles.DrawAAConvexPolygon(points);
            Handles.EndGUI();
        }

        private void DrawAutoTransitionsPanel()
        {
            var transitionsProp = serializedObject.FindProperty("autoTransitions");

            GUILayout.BeginVertical(sectionBox);

            EditorGUILayout.BeginHorizontal();
            int size = transitionsProp != null ? transitionsProp.arraySize : 0;
            transitionFoldout = EditorGUILayout.Foldout(transitionFoldout, $"Auto Transitions ({size})", true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (GUI.changed)
                EditorPrefs.SetBool(TransitionFoldoutPrefKey, transitionFoldout);

            if (!transitionFoldout)
            {
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(6);

            if (Application.isPlaying)
                DrawRuntimeTimerStatus();

            if (availableStates == null || availableStates.Length == 0)
            {
                EditorGUILayout.HelpBox("사용 가능한 State가 없습니다.", MessageType.Warning);
                GUILayout.EndVertical();
                return;
            }

            if (transitionsProp == null)
            {
                EditorGUILayout.HelpBox("autoTransitions 프로퍼티를 찾을 수 없습니다.", MessageType.Error);
                GUILayout.EndVertical();
                return;
            }

            if (GUILayout.Button("+ Add Transition", GUILayout.Height(24)))
            {
                transitionsProp.arraySize++;
                var elem = transitionsProp.GetArrayElementAtIndex(transitionsProp.arraySize - 1);

                elem.FindPropertyRelative("Tag").stringValue = "";
                elem.FindPropertyRelative("Delay").floatValue = 0f;
                elem.FindPropertyRelative("BlendDuration").floatValue = 0.25f;
                elem.FindPropertyRelative("TargetState").stringValue = availableStates[0];
                elem.FindPropertyRelative("Layer").intValue = -1;
                elem.FindPropertyRelative("Speed").floatValue = 1f;

                serializedObject.ApplyModifiedProperties();
                transitionPage = Mathf.Clamp(transitionsProp.arraySize - 1, 0, transitionsProp.arraySize - 1);
            }

            GUILayout.Space(4);

            int count = transitionsProp.arraySize;
            if (count == 0)
            {
                DrawMiniLabel("PlayTransition() 호출 시 등록된 전환이 Delay 후 자동 실행됩니다.");
                GUILayout.EndVertical();
                return;
            }

            DrawTransitionEditor(transitionsProp);
            GUILayout.Space(6);
            DrawTransitionList(transitionsProp);

            GUILayout.EndVertical();
        }

        private void DrawRuntimeTimerStatus()
        {
            bool isRunning = ctrl.IsTransitionRunning;
            float timerValue = ctrl.TransitionTimer;
            string activeTag = ctrl.TransitionTag;

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();

            var timerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = isRunning ? timerActiveColor : timerStoppedColor }
            };
            GUILayout.Label($"⏱ {timerValue:F2}s", timerStyle, GUILayout.Width(90));

            var statusStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = isRunning ? timerActiveColor : timerStoppedColor }
            };
            GUILayout.Label(isRunning ? "● Running" : "○ Stopped", statusStyle, GUILayout.Width(75));

            if (isRunning && !string.IsNullOrEmpty(activeTag))
            {
                var tagStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.8f, 0.3f, 1f) }
                };
                GUILayout.Label($"Tag: {activeTag}", tagStyle);
            }

            GUILayout.FlexibleSpace();

            GUI.enabled = isRunning;
            if (GUILayout.Button("Pause", GUILayout.Width(55), GUILayout.Height(20)))
                ctrl.PauseTransition();
            GUI.enabled = true;

            if (GUILayout.Button("Stop", GUILayout.Width(55), GUILayout.Height(20)))
                ctrl.StopTransition();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void DrawTransitionEditor(SerializedProperty transitionsProp)
        {
            int count = transitionsProp.arraySize;
            transitionPage = Mathf.Clamp(transitionPage, 0, Mathf.Max(0, count - 1));

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(transitionPage <= 0))
                if (GUILayout.Button("<", GUILayout.Width(24))) transitionPage--;

            var pageStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label($"{transitionPage + 1} / {count}", pageStyle, GUILayout.Width(60));

            using (new EditorGUI.DisabledScope(transitionPage >= count - 1))
                if (GUILayout.Button(">", GUILayout.Width(24))) transitionPage++;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Remove", GUILayout.Width(70)))
            {
                transitionsProp.DeleteArrayElementAtIndex(transitionPage);
                serializedObject.ApplyModifiedProperties();
                transitionPage = Mathf.Clamp(transitionPage, 0, Mathf.Max(0, transitionsProp.arraySize - 1));
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (transitionsProp.arraySize == 0) return;
            transitionPage = Mathf.Clamp(transitionPage, 0, transitionsProp.arraySize - 1);

            var t = transitionsProp.GetArrayElementAtIndex(transitionPage);

            var stateProp = t.FindPropertyRelative("TargetState");
            var tagProp = t.FindPropertyRelative("Tag");
            var delayProp = t.FindPropertyRelative("Delay");
            var blendProp = t.FindPropertyRelative("BlendDuration");
            var speedProp = t.FindPropertyRelative("Speed");
            var layerProp = t.FindPropertyRelative("Layer");

            int currentIndex = Array.IndexOf(availableStates, stateProp.stringValue);
            if (currentIndex < 0) currentIndex = 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Target State");
            int newIndex = EditorGUILayout.Popup(currentIndex, availableStates);
            if (newIndex >= 0 && newIndex < availableStates.Length)
                stateProp.stringValue = availableStates[newIndex];
            EditorGUILayout.EndHorizontal();

            tagProp.stringValue = EditorGUILayout.TextField("Tag (Filter)", tagProp.stringValue);

            EditorGUILayout.BeginHorizontal();
            delayProp.floatValue = EditorGUILayout.FloatField("Delay", delayProp.floatValue);
            EditorGUILayout.LabelField("sec", GUILayout.Width(28));
            EditorGUILayout.EndHorizontal();

            speedProp.floatValue = EditorGUILayout.Slider("Speed", speedProp.floatValue, 0f, 4f);
            blendProp.floatValue = EditorGUILayout.Slider("Blend", blendProp.floatValue, 0f, 2f);
            layerProp.intValue = EditorGUILayout.IntField("Layer (-1 = any)", layerProp.intValue);

            GUILayout.Space(4);

            string callExample = string.IsNullOrEmpty(tagProp.stringValue)
                ? "PlayTransition()"
                : $"PlayTransition(\"{tagProp.stringValue}\")";

            DrawMiniLabel($"{callExample} 호출 후 {delayProp.floatValue:F1}초가 지나면 '{stateProp.stringValue}'로 전환됩니다.");
        }

        private void DrawTransitionList(SerializedProperty transitionsProp)
        {
            if (transitionsProp == null)
                return;

            GUILayout.Label("All Transitions", EditorStyles.boldLabel);

            transitionListScroll = EditorGUILayout.BeginScrollView(transitionListScroll, GUILayout.Height(100));

            int count = transitionsProp.arraySize;
            for (int i = 0; i < count; i++)
            {
                if (i >= transitionsProp.arraySize) break;

                var trans = transitionsProp.GetArrayElementAtIndex(i);
                if (trans == null) continue;

                var tagProp = trans.FindPropertyRelative("Tag");
                var delayProp = trans.FindPropertyRelative("Delay");
                var stateProp = trans.FindPropertyRelative("TargetState");
                var speedProp = trans.FindPropertyRelative("Speed");

                if (tagProp == null || delayProp == null || stateProp == null || speedProp == null) continue;

                string tag = tagProp.stringValue;
                float delay = delayProp.floatValue;
                string state = stateProp.stringValue;
                float speed = speedProp.floatValue;
                if (speed <= 0f) speed = 1f;

                EditorGUILayout.BeginHorizontal();

                string tagLabel = string.IsNullOrEmpty(tag) ? "*" : tag;
                string label = $"[{tagLabel}] {delay:0.0}s → {state} (x{speed:0.#})";

                var prevBg = GUI.backgroundColor;

                if (Application.isPlaying && ctrl.IsTransitionRunning)
                {
                    string activeTag = ctrl.TransitionTag;
                    bool isActive = string.IsNullOrEmpty(activeTag) || tag == activeTag;
                    if (isActive)
                        GUI.backgroundColor = timerActiveColor;
                }
                else if (i == transitionPage)
                {
                    GUI.backgroundColor = new Color(0.4f, 0.7f, 1f, 1f);
                }

                if (GUILayout.Button(label, EditorStyles.miniButtonLeft))
                {
                    if (Application.isPlaying)
                        ctrl.PlayTransition(tag);
                    else
                        transitionPage = i;
                }

                GUI.backgroundColor = prevBg;

                if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                {
                    transitionsProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    transitionPage = Mathf.Clamp(transitionPage, 0, Mathf.Max(0, transitionsProp.arraySize - 1));
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawFrameEventsPanel()
        {
            var frameEventsProp = serializedObject.FindProperty("frameEvents");
            var eventsProp = frameEventsProp?.FindPropertyRelative("events");

            GUILayout.BeginVertical(sectionBox);

            EditorGUILayout.BeginHorizontal();
            int size = eventsProp != null ? eventsProp.arraySize : 0;
            frameEventsFoldout = EditorGUILayout.Foldout(frameEventsFoldout, $"Frame Events ({size})", true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (GUI.changed)
                EditorPrefs.SetBool(FrameEventsFoldoutPrefKey, frameEventsFoldout);

            if (!frameEventsFoldout)
            {
                GUILayout.EndVertical();
                return;
            }

            if (ctrl == null || ctrl.CurrentClip == null)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox("선택된 클립이 없습니다.", MessageType.Info);
                GUILayout.EndVertical();
                return;
            }

            if (eventsProp == null)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox("frameEvents 프로퍼티를 찾을 수 없습니다.", MessageType.Error);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(8);

            // 현재 클립 이름 표시 - 더 큰 영역
            var clipBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 0, 0)
            };
            EditorGUILayout.BeginHorizontal(clipBoxStyle, GUILayout.Height(28));
            GUILayout.Label("🎬", GUILayout.Width(24), GUILayout.Height(20));
            GUILayout.Label(ctrl.CurrentClip.name, clipInfoStyle, GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            DrawEventQuickActions(eventsProp);

            GUILayout.Space(8);

            if (eventsProp.arraySize == 0)
            {
                DrawMiniLabel("Frame Events가 없습니다. UnityEvent 기반으로 동작합니다.");
                GUILayout.Space(4);
                GUILayout.EndVertical();
                return;
            }

            eventPage = Mathf.Clamp(eventPage, 0, eventsProp.arraySize - 1);

            DrawFrameEventEditor(eventsProp, eventPage);

            GUILayout.Space(10);

            DrawFrameEventsList(eventsProp);

            GUILayout.Space(4);
            GUILayout.EndVertical();
        }

        private void DrawEventQuickActions(SerializedProperty eventsProp)
        {
            int currentFrame = editorCurrentFrame;
            int maxFrame = GetMaxFrame();

            bool hasEventAtCurrentFrame = false;
            int eventAtCurrentFrameIndex = -1;

            for (int i = 0; i < eventsProp.arraySize; i++)
            {
                var ev = eventsProp.GetArrayElementAtIndex(i);
                int frame = ev.FindPropertyRelative("Frame").intValue;
                if (frame == currentFrame)
                {
                    hasEventAtCurrentFrame = true;
                    eventAtCurrentFrameIndex = i;
                    break;
                }
            }

            EditorGUILayout.BeginHorizontal();

            var prevBg = GUI.backgroundColor;
            if (!hasEventAtCurrentFrame)
                GUI.backgroundColor = new Color(0.4f, 0.9f, 0.5f, 1f);

            if (GUILayout.Button("+ Add Event", GUILayout.Height(28)))
            {
                AddFrameEvent(eventsProp, currentFrame);
            }

            GUI.backgroundColor = prevBg;

            if (hasEventAtCurrentFrame)
            {
                GUI.backgroundColor = selectedEventMarkerColor;
                if (GUILayout.Button($"Select Event #{eventAtCurrentFrameIndex + 1}", GUILayout.Width(140), GUILayout.Height(28)))
                {
                    eventPage = eventAtCurrentFrameIndex;
                }
                GUI.backgroundColor = prevBg;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Sort", GUILayout.Width(60), GUILayout.Height(28)))
            {
                SortFrameEvents(eventsProp);
            }

            EditorGUILayout.EndHorizontal();

            if (hasEventAtCurrentFrame)
            {
                GUILayout.Space(4);
                var infoStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = selectedEventMarkerColor },
                    fontStyle = FontStyle.Bold,
                    fontSize = 11
                };
                GUILayout.Label($"  ▲ 현재 프레임({currentFrame})에 이벤트가 있습니다", infoStyle);
            }
        }

        private void AddFrameEvent(SerializedProperty eventsProp, int frame)
        {
            eventsProp.arraySize++;
            var newEvent = eventsProp.GetArrayElementAtIndex(eventsProp.arraySize - 1);
            newEvent.FindPropertyRelative("Frame").intValue = frame;
            newEvent.FindPropertyRelative("Label").stringValue = "";

            serializedObject.ApplyModifiedProperties();
            eventPage = eventsProp.arraySize - 1;
        }

        private void SortFrameEvents(SerializedProperty eventsProp)
        {
            // SerializedProperty 정렬은 복잡하므로 간단히 처리
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFrameEventEditor(SerializedProperty eventsProp, int index)
        {
            int maxFrame = GetMaxFrame();
            float fps = Mathf.Max(1f, ctrl.CurrentClip.frameRate);

            // 이벤트 에디터 박스
            var editorBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 12),
                margin = new RectOffset(0, 0, 4, 4)
            };
            EditorGUILayout.BeginVertical(editorBoxStyle);

            // 페이지 네비게이션
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(index <= 0))
            {
                if (GUILayout.Button("◀", GUILayout.Width(32), GUILayout.Height(24)))
                {
                    eventPage = Mathf.Max(0, eventPage - 1);
                    JumpToEventFrame(eventsProp, eventPage);
                    GUIUtility.ExitGUI();
                }
            }

            var pageStyle = new GUIStyle(EditorStyles.boldLabel) 
            { 
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };
            GUILayout.Label($"Event {index + 1} / {eventsProp.arraySize}", pageStyle, GUILayout.Height(24));

            using (new EditorGUI.DisabledScope(index >= eventsProp.arraySize - 1))
            {
                if (GUILayout.Button("▶", GUILayout.Width(32), GUILayout.Height(24)))
                {
                    eventPage = Mathf.Min(eventsProp.arraySize - 1, eventPage + 1);
                    JumpToEventFrame(eventsProp, eventPage);
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.FlexibleSpace();

            var deleteBtnColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f, 1f);
            if (GUILayout.Button("Delete", GUILayout.Width(70), GUILayout.Height(24)))
            {
                eventsProp.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                eventPage = Mathf.Clamp(eventPage, 0, Mathf.Max(0, eventsProp.arraySize - 1));
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = deleteBtnColor;

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            var ev = eventsProp.GetArrayElementAtIndex(index);
            var frameProp = ev.FindPropertyRelative("Frame");
            var labelProp = ev.FindPropertyRelative("Label");
            var onTriggeredProp = ev.FindPropertyRelative("OnTriggered");

            int currentFrame = frameProp.intValue;

            // Frame 슬라이더
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Frame", GUILayout.Width(50));

            int newFrame = EditorGUILayout.IntSlider(currentFrame, 0, maxFrame, GUILayout.Height(20));

            var jumpBtnColor = GUI.backgroundColor;
            bool isAtEventFrame = editorCurrentFrame == currentFrame;
            if (!isAtEventFrame)
                GUI.backgroundColor = new Color(0.5f, 0.8f, 1f, 1f);

            if (GUILayout.Button(isAtEventFrame ? "●" : "→", GUILayout.Width(30), GUILayout.Height(20)))
            {
                EditorJumpToFrame(currentFrame);
            }
            GUI.backgroundColor = jumpBtnColor;

            EditorGUILayout.EndHorizontal();

            if (newFrame != currentFrame)
            {
                frameProp.intValue = newFrame;
                serializedObject.ApplyModifiedProperties();
                EditorJumpToFrame(newFrame);
            }

            // 프레임 정보 표시
            var frameInfoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 10
            };
            if (isAtEventFrame)
            {
                frameInfoStyle.normal.textColor = currentFrameColor;
                frameInfoStyle.fontStyle = FontStyle.Bold;
            }

            float time = currentFrame / fps;
            string frameInfo = $"{currentFrame}f / {maxFrame}f  @{fps:0.#}fps  →  {time:0.000}s";
            if (isAtEventFrame)
                frameInfo += "  (현재)";

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(frameInfo, frameInfoStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Label 필드 - 크게
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Label", GUILayout.Width(50));
            labelProp.stringValue = EditorGUILayout.TextField(labelProp.stringValue, GUILayout.Height(22));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // UnityEvent - 별도 박스로 감싸기
            var eventBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 8, 8),
                margin = new RectOffset(0, 0, 0, 0)
            };
            EditorGUILayout.BeginVertical(eventBoxStyle);
            
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            GUILayout.Label("On Triggered ()", headerStyle);
            GUILayout.Space(4);
            
            EditorGUILayout.PropertyField(onTriggeredProp, GUIContent.none);
            
            EditorGUILayout.EndVertical();

            GUILayout.Space(6);

            DrawMiniLabel("UnityEvent 기반: 재생 중 해당 Frame에 도달하면 호출됩니다.");

            EditorGUILayout.EndVertical();
        }

        private void JumpToEventFrame(SerializedProperty eventsProp, int eventIndex)
        {
            if (eventIndex < 0 || eventIndex >= eventsProp.arraySize) return;

            var ev = eventsProp.GetArrayElementAtIndex(eventIndex);
            int frame = ev.FindPropertyRelative("Frame").intValue;
            EditorJumpToFrame(frame);
        }

        private void DrawFrameEventsList(SerializedProperty eventsProp)
        {
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            GUILayout.Label("All Events (click to select & jump)", headerStyle);
            GUILayout.Space(4);

            // 리스트 박스
            var listBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 6, 6)
            };
            EditorGUILayout.BeginVertical(listBoxStyle);

            eventsListScroll = EditorGUILayout.BeginScrollView(eventsListScroll, GUILayout.Height(140));

            int maxFrame = GetMaxFrame();
            int currentPlayFrame = editorCurrentFrame;

            for (int i = 0; i < eventsProp.arraySize; i++)
            {
                var ev = eventsProp.GetArrayElementAtIndex(i);
                int frame = ev.FindPropertyRelative("Frame").intValue;
                string label = ev.FindPropertyRelative("Label").stringValue;

                if (string.IsNullOrEmpty(label))
                    label = $"Event #{i + 1}";

                bool isSelected = i == eventPage;
                bool isAtCurrentFrame = frame == currentPlayFrame;

                EditorGUILayout.BeginHorizontal();

                var prevBg = GUI.backgroundColor;

                if (isSelected)
                    GUI.backgroundColor = selectedEventMarkerColor;
                else if (isAtCurrentFrame)
                    GUI.backgroundColor = currentFrameColor;

                string frameIndicator = isAtCurrentFrame ? "●" : " ";
                string buttonLabel = $"{frameIndicator} [{frame,3}f] {label}";

                var buttonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                    fontSize = 11,
                    padding = new RectOffset(8, 8, 4, 4)
                };

                if (GUILayout.Button(buttonLabel, buttonStyle, GUILayout.Height(24)))
                {
                    eventPage = i;
                    EditorJumpToFrame(frame);
                    GUI.backgroundColor = prevBg;
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = prevBg;

                if (GUILayout.Button("×", GUILayout.Width(26), GUILayout.Height(24)))
                {
                    eventsProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    eventPage = Mathf.Clamp(eventPage, 0, Mathf.Max(0, eventsProp.arraySize - 1));
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawTabSettings()
        {
            var playbackProp = serializedObject.FindProperty("playback");
            if (playbackProp == null)
            {
                EditorGUILayout.HelpBox("playback 프로퍼티를 찾을 수 없습니다.", MessageType.Error);
                return;
            }

            GUILayout.BeginVertical(sectionBox);
            GUILayout.Label("Playback", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var speedProp = playbackProp.FindPropertyRelative("PlaybackSpeed");
            speedProp.floatValue = EditorGUILayout.Slider("Playback Speed", speedProp.floatValue, 0f, 4f);
            DrawMiniLabel("재생 속도 배율입니다.");

            GUILayout.Space(4);
            EditorGUILayout.PropertyField(playbackProp.FindPropertyRelative("Loop"));
            DrawMiniLabel("활성화하면 EndFrame에서 StartFrame으로 돌아가 반복 재생됩니다.");

            GUILayout.Space(4);
            EditorGUILayout.PropertyField(playbackProp.FindPropertyRelative("AutoPlay"));
            DrawMiniLabel("Start() 시점에 자동으로 재생을 시작합니다.");

            GUILayout.Space(4);
            EditorGUILayout.PropertyField(playbackProp.FindPropertyRelative("ReversePlayback"));
            DrawMiniLabel("역방향으로 재생합니다.");

            GUILayout.EndVertical();

            GUILayout.Space(4);

            GUILayout.BeginVertical(sectionBox);
            GUILayout.Label("Frame Range", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var startFrame = playbackProp.FindPropertyRelative("StartFrame");
            var endFrame = playbackProp.FindPropertyRelative("EndFrame");

            startFrame.intValue = EditorGUILayout.IntField("Start Frame", startFrame.intValue);
            DrawMiniLabel("재생이 시작되는 프레임입니다.");

            GUILayout.Space(4);
            endFrame.intValue = EditorGUILayout.IntField("End Frame", endFrame.intValue);
            DrawMiniLabel("-1로 설정하면 클립의 마지막 프레임을 사용합니다.");

            GUILayout.EndVertical();

            GUILayout.Space(4);

            GUILayout.BeginVertical(sectionBox);
            GUILayout.Label("Initial State", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var useInit = playbackProp.FindPropertyRelative("UseInitialFrameOnStart");
            var initFrame = playbackProp.FindPropertyRelative("InitialFrameOnStart");

            EditorGUILayout.PropertyField(useInit, new GUIContent("Use Initial Frame"));
            DrawMiniLabel("Start() 시점에 지정된 프레임에서 시작합니다.");

            GUILayout.Space(4);
            EditorGUILayout.PropertyField(initFrame, new GUIContent("Initial Frame"));
            DrawMiniLabel("-1로 설정하면 StartFrame 또는 EndFrame(역재생 시)에서 시작합니다.");

            GUILayout.EndVertical();
        }

        private void DrawMiniLabel(string text)
        {
            GUILayout.Label($"  └ {text}", miniLabelStyle);
        }
    }
}
#endif