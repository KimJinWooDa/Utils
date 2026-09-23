#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        // 숨긴 클립은 보기 설정이므로 컴포넌트가 아니라 에디터 환경설정에 오브젝트별(GlobalObjectId)로 저장한다
        private const string HiddenClipsPrefPrefix = "TelleR.AnimInspector.HiddenClips.";
        // GlobalObjectId가 없는(저장 전 씬·런타임 생성) 오브젝트는 이번 세션 동안만 인스턴스별로 보관한다
        private const string HiddenClipsSessionPrefix = "TelleR.AnimInspector.HiddenClipsSession.";
        private const string SessionStoredMark = "#";

        private static readonly string[] mainTabs = { "Workspace", "Settings" };
        private static readonly string[] workspaceTabs = { "Transitions", "Frame Events" };

        private static readonly GUIContent PlayContent = new GUIContent("▶  Play", "재생 (Space)");
        private static readonly GUIContent PauseContent = new GUIContent("||  Pause", "일시정지 (Space)");
        private static readonly GUIContent StopContent = new GUIContent("■  Stop", "정지하고 Start Frame으로 돌아갑니다.");
        private static readonly GUIContent PrevFrameContent = new GUIContent("-1", "이전 프레임 (←)");
        private static readonly GUIContent NextFrameContent = new GUIContent("+1", "다음 프레임 (→)");
        private static readonly GUIContent Prev10Content = new GUIContent("-10", "10프레임 뒤로");
        private static readonly GUIContent Next10Content = new GUIContent("+10", "10프레임 앞으로");
        private static readonly GUIContent RefreshContent = new GUIContent("Refresh", "Animator Controller의 State·클립 목록을 다시 읽습니다.");
        private static readonly GUIContent HideClipContent = new GUIContent("×", "목록에서 숨깁니다(이 PC의 에디터 설정에만 저장).");
        private static readonly GUIContent ShowClipContent = new GUIContent("+", "다시 목록에 표시합니다.");
        private static readonly GUIContent AllEventsContent = new GUIContent("All Events", "클릭하면 선택하고 해당 프레임으로 이동합니다. 흐리게 표시된 이벤트는 다른 클립 전용입니다.");
        private static readonly GUIContent ScopeClipContent = new GUIContent("Clip", "이 이벤트가 발사될 클립. 비워 두면(None) 모든 클립에서 같은 Frame에 발사됩니다.");
        private static readonly GUIContent ThisClipContent = new GUIContent("This Clip", "현재 클립에서만 발사되도록 지정합니다.");
        private static readonly GUIContent AllClipsContent = new GUIContent("All Clips", "모든 클립에서 발사되도록 지정합니다.");
        private static readonly GUIContent UseInitialFrameContent = new GUIContent("Use Initial Frame");
        private static readonly GUIContent AddEventContent = new GUIContent("+ Add Event", "현재 프레임에 이 클립 전용 이벤트를 추가합니다.");
        private static readonly GUIContent SortContent = new GUIContent("Sort", "Frame 순으로 정렬합니다.");
        private static readonly GUIContent DeleteEventContent = new GUIContent("×", "이 이벤트를 삭제합니다(Undo 가능).");
        private static readonly GUIContent JumpToEventContent = new GUIContent("→", "이 이벤트의 프레임으로 이동합니다.");
        private static readonly GUIContent AtEventContent = new GUIContent("●", "현재 이 이벤트의 프레임에 있습니다.");
        private static readonly GUIContent TransitionSpeedContent = new GUIContent("Speed", "0이면 전환 후에도 현재 재생 속도를 유지합니다.");
        private static readonly GUIContent AllTransitionsEditContent = new GUIContent("All Transitions", "클릭하면 편집할 전환을 선택합니다.");
        private static readonly GUIContent AllTransitionsPlayContent = new GUIContent("All Transitions", "클릭하면 해당 Tag로 PlayTransition()을 실행합니다.");

        private static readonly Color HiddenClipTint = new Color(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color DefaultClipTint = new Color(0.72f, 0.72f, 0.95f, 1f);

        private AnimationInspectorController ctrl;

        private int mainTabIndex;
        private int workspaceTabIndex;

        // ─── 캐시된 스타일 (스킨이 바뀔 때만 다시 만든다) ───
        private bool? stylesBuiltForPro;
        private GUIStyle card;
        private GUIStyle sectionBox;
        private GUIStyle miniLabelStyle;
        private GUIStyle clipInfoStyle;
        private GUIStyle frameInfoStyle;
        private GUIStyle markerCountStyle;
        private GUIStyle timerStyle;
        private GUIStyle statusStyle;
        private GUIStyle tagStyle;
        private GUIStyle pageStyle;
        private GUIStyle eventPageStyle;
        private GUIStyle clipBoxStyle;
        private GUIStyle eventInfoStyle;
        private GUIStyle editorBoxStyle;
        private GUIStyle eventFrameInfoStyle;
        private GUIStyle eventFrameInfoCurrentStyle;
        private GUIStyle eventBoxStyle;
        private GUIStyle smallHeaderStyle;
        private GUIStyle listBoxStyle;
        private GUIStyle eventRowStyle;
        private GUIStyle eventRowSelectedStyle;

        // 타임라인 마커용 재사용 버퍼
        private readonly Vector3[] trianglePoints = new Vector3[3];

        private bool previewFoldout = true;
        private bool transitionFoldout = true;
        private bool frameEventsFoldout = true;

        private Vector2 transitionListScroll;
        private int transitionPage;

        private string[] availableStates = Array.Empty<string>();
        private List<ClipCatalog.ClipInfo> clipInfos = new List<ClipCatalog.ClipInfo>();
        private List<ClipCatalog.ClipInfo> visibleClips = new List<ClipCatalog.ClipInfo>();
        private List<ClipCatalog.ClipInfo> hiddenClipInfos = new List<ClipCatalog.ClipInfo>();
        private readonly HashSet<string> hiddenKeys = new HashSet<string>();
        private RuntimeAnimatorController lastController;
        private int currentClipIndex = -1;
        private bool showClipList;
        private bool showHiddenClips;
        private int gridColumns = 3;

        private int eventPage;
        private Vector2 eventsListScroll;

        // Layout/Repaint 사이에 값이 바뀌어 레이아웃이 어긋나지 않도록 Layout 이벤트에서 한 번만 읽는다
        private int guiFrame;
        private bool guiPlaying;

        // Edit Mode Preview State (AnimationMode 기반)
        private enum EditorPlayState { Stopped, Playing, Paused }
        private EditorPlayState editorPlayState = EditorPlayState.Stopped;
        private int editorCurrentFrame;
        private float editorPlaybackSpeed = 1f;
        private double lastEditorTime;
        private float frameAccumulator;

        // 전용 드라이버로 AnimationMode를 잠가 Animation 창·Timeline의 미리보기/녹화를 끄지 않는다
        private AnimationModeDriver previewDriver;
        private bool animationModeBlocked;
        private int lastSampledFrame = -1;
        private AnimationClip lastSampledClip;

        private bool IsPreviewActive => previewDriver != null && AnimationMode.InAnimationMode(previewDriver);

        private void OnEnable()
        {
            ctrl = (AnimationInspectorController)target;

            mainTabIndex = Mathf.Clamp(EditorPrefs.GetInt(MainTabPrefKey, 0), 0, mainTabs.Length - 1);
            workspaceTabIndex = Mathf.Clamp(EditorPrefs.GetInt(WorkspaceSubTabPrefKey, 0), 0, workspaceTabs.Length - 1);

            previewFoldout = EditorPrefs.GetBool(PreviewFoldoutPrefKey, true);
            transitionFoldout = EditorPrefs.GetBool(TransitionFoldoutPrefKey, true);
            frameEventsFoldout = EditorPrefs.GetBool(FrameEventsFoldoutPrefKey, true);

            LoadHiddenClips();
            RefreshLists();

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            StopAnimationModeIfActive();

            if (previewDriver != null)
            {
                DestroyImmediate(previewDriver);
                previewDriver = null;
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Play Mode 진입 전에 에디터 프리뷰 포즈를 원래대로 되돌린다(AnimationMode가 런타임 재생을 방해하지 않도록)
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                editorPlayState = EditorPlayState.Stopped;
                StopAnimationModeIfActive();
            }
        }

        public override bool RequiresConstantRepaint()
        {
            return IsLiveRuntime &&
                   (ctrl.CurrentPlayState == AnimationPlaybackCore.PlayState.Playing || ctrl.IsTransitionRunning);
        }

        /// <summary>
        /// Play Mode에서 씬에 올라간 인스턴스인지 — 클립 선택을 런타임 API(ChangeClip)로 보낼 대상. 프로젝트 창의 프리팹 에셋이나
        /// Prefab Mode(미리보기 씬)의 오브젝트에 런타임 API를 쓰면 Undo·저장 없이 값이 바뀌고 정리되지 않는 PlayableGraph가 생긴다.
        /// </summary>
        private bool IsSceneInstanceInPlayMode => Application.isPlaying && ctrl != null
                                                 && !EditorUtility.IsPersistent(ctrl)
                                                 && !EditorSceneManager.IsPreviewScene(ctrl.gameObject.scene);

        /// <summary>
        /// 재생·프레임 조작과 런타임 상태 표시를 할 대상인지. 비활성 컴포넌트(또는 GameObject)는 그래프가 없고 Update도 돌지 않으므로 제외한다
        /// — 이때 클립 선택은 런타임이 기록만 해 두었다가 다시 켜질 때 적용한다.
        /// </summary>
        private bool IsLiveRuntime => IsSceneInstanceInPlayMode && ctrl.isActiveAndEnabled;

        /// <summary>Play Mode지만 재생 조작 대상이 아닌 경우(비활성 컴포넌트·프리팹 에셋·Prefab Mode). 재생 조작 없이 값 편집만 허용한다.</summary>
        private bool IsPlayModeNonLive => Application.isPlaying && !IsLiveRuntime;

        // ─────────────────────────── Edit Mode preview ───────────────────────────

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastEditorTime), 0f, 0.1f);
            lastEditorTime = now;

            if (Application.isPlaying) return;
            if (ctrl == null || ctrl.CurrentClip == null) return;
            if (editorPlayState != EditorPlayState.Playing) return;

            float fps = Mathf.Max(1f, ctrl.CurrentClip.frameRate);
            float frameDuration = 1f / fps;

            GetPreviewRange(out int start, out int end);
            bool reverse = ctrl.ReversePlayback;
            bool loop = ctrl.Loop;

            frameAccumulator += dt * editorPlaybackSpeed;

            int frame = editorCurrentFrame;
            bool finished = false;

            while (frameAccumulator >= frameDuration)
            {
                frameAccumulator -= frameDuration;
                frame += reverse ? -1 : 1;

                if (!reverse && frame > end)
                {
                    if (loop) frame = start;
                    else { frame = end; finished = true; break; }
                }
                else if (reverse && frame < start)
                {
                    if (loop) frame = end;
                    else { frame = start; finished = true; break; }
                }
            }

            if (finished)
            {
                editorPlayState = EditorPlayState.Stopped;
                frameAccumulator = 0f;
            }

            // 프레임이 바뀐 경우에만 샘플링·다시 그리기
            if (frame != editorCurrentFrame || finished)
            {
                editorCurrentFrame = frame;
                SampleCurrentFrame();
                Repaint();
            }
        }

        /// <summary>Settings의 Start/End Frame 구간. 구간이 비정상(Start &gt;= End)이면 클립 전체를 쓴다.</summary>
        private void GetPreviewRange(out int start, out int end)
        {
            start = ctrl.EffectiveStartFrame;
            end = ctrl.EffectiveEndFrame;
            if (start >= end)
            {
                start = 0;
                end = GetMaxFrame();
            }
        }

        private AnimationModeDriver PreviewDriver
        {
            get
            {
                if (previewDriver == null)
                {
                    previewDriver = CreateInstance<AnimationModeDriver>();
                    previewDriver.hideFlags = HideFlags.HideAndDontSave;
                    previewDriver.name = "TelleR Animation Inspector Preview";
                }
                return previewDriver;
            }
        }

        private bool StartAnimationModeIfNeeded()
        {
            if (Application.isPlaying) return false;
            if (ctrl == null || ctrl.AnimatorComponent == null) return false;

            if (IsPreviewActive)
            {
                animationModeBlocked = false;
                return true;
            }

            // Animation 창·Timeline 등 다른 도구가 AnimationMode를 쓰는 중이면 뺏지 않는다
            if (AnimationMode.InAnimationMode())
            {
                animationModeBlocked = true;
                return false;
            }

            animationModeBlocked = false;
            AnimationMode.StartAnimationMode(PreviewDriver);
            lastSampledFrame = -1;
            return AnimationMode.InAnimationMode(previewDriver);
        }

        private void StopAnimationModeIfActive()
        {
            // 우리가 시작한 AnimationMode만 끈다
            if (IsPreviewActive)
                AnimationMode.StopAnimationMode(previewDriver);

            lastSampledFrame = -1;
            lastSampledClip = null;
        }

        private void SampleCurrentFrame(bool force = false)
        {
            if (Application.isPlaying) return;
            if (ctrl == null || ctrl.CurrentClip == null || ctrl.AnimatorComponent == null) return;

            var clip = ctrl.CurrentClip;
            if (!force && IsPreviewActive && lastSampledFrame == editorCurrentFrame && lastSampledClip == clip) return;

            if (!StartAnimationModeIfNeeded()) return;

            float fps = Mathf.Max(1f, clip.frameRate);
            float time = Mathf.Min(editorCurrentFrame / fps, clip.length);

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(ctrl.AnimatorComponent.gameObject, clip, time);
            AnimationMode.EndSampling();

            lastSampledFrame = editorCurrentFrame;
            lastSampledClip = clip;
            SceneView.RepaintAll();
        }

        private int GetMaxFrame()
        {
            if (ctrl == null || ctrl.CurrentClip == null) return 0;
            return Mathf.Max(0, Mathf.RoundToInt(ctrl.CurrentClip.length * ctrl.CurrentClip.frameRate));
        }

        // ─────────────────────────── Preview/runtime 공통 조작 ───────────────────────────

        private int LiveFrame => IsLiveRuntime ? ctrl.CurrentFrame : editorCurrentFrame;

        private bool LivePlaying => IsLiveRuntime
            ? ctrl.CurrentPlayState == AnimationPlaybackCore.PlayState.Playing
            : editorPlayState == EditorPlayState.Playing;

        /// <summary>Play Mode(씬 인스턴스)에서는 런타임 재생 위치를, 그 밖에는 프리뷰 위치를 옮긴다.</summary>
        private void JumpTo(int frame)
        {
            if (ctrl == null) return;

            if (IsLiveRuntime)
            {
                ctrl.JumpToFrame(frame);
                Repaint();
            }
            else
            {
                EditorJumpToFrame(frame);
            }
        }

        private void StepFrame(int delta)
        {
            JumpTo(Mathf.Clamp(LiveFrame + delta, 0, GetMaxFrame()));
        }

        private void TogglePlayPause()
        {
            if (ctrl == null || ctrl.CurrentClip == null) return;

            if (IsLiveRuntime)
            {
                ctrl.TogglePlayPause();
                return;
            }
            // Play Mode의 프리팹 에셋·Prefab Mode 대상은 재생 조작이 없다(AnimationMode 프리뷰도 Play Mode에서는 쓰지 않음)
            if (Application.isPlaying) return;

            if (editorPlayState == EditorPlayState.Playing) EditorPause();
            else EditorPlay();
        }

        private void StopPlayback()
        {
            if (IsLiveRuntime)
                ctrl.Stop();
            else if (!Application.isPlaying)
                EditorStop();
        }

        private void HandleShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || ctrl == null || ctrl.CurrentClip == null || IsPlayModeNonLive) return;
            // 입력 필드·슬라이더 등 다른 컨트롤이 키보드를 잡고 있으면 가로채지 않는다
            if (GUIUtility.keyboardControl != 0 || EditorGUIUtility.editingTextField) return;

            switch (e.keyCode)
            {
                case KeyCode.Space:
                    TogglePlayPause();
                    e.Use();
                    break;
                case KeyCode.LeftArrow:
                    StepFrame(-1);
                    e.Use();
                    break;
                case KeyCode.RightArrow:
                    StepFrame(1);
                    e.Use();
                    break;
            }
        }

        // ─────────────────────────── Inspector ───────────────────────────

        public override void OnInspectorGUI()
        {
            if (ctrl == null)
                ctrl = (AnimationInspectorController)target;

            serializedObject.Update();
            InitStyles();

            // Animator Controller가 교체되면 State·클립 목록을 다시 읽는다
            var rac = ctrl.AnimatorComponent != null ? ctrl.AnimatorComponent.runtimeAnimatorController : null;
            if (rac != lastController)
                RefreshLists();

            if (Event.current.type == EventType.Layout)
            {
                guiFrame = LiveFrame;
                guiPlaying = LivePlaying;
                // 자동 전환·스크립트의 ChangeClip·Undo로 클립이 바뀌면 캐시한 선택 위치가 낡는다 — 라벨과 '<'/'>'가 실제 클립을 따르게 맞춘다
                SyncCurrentClipIndex();
            }

            HandleShortcuts();

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
            if (stylesBuiltForPro == TelleRGUI.IsPro && card != null) return;
            stylesBuiltForPro = TelleRGUI.IsPro;

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
                padding = new RectOffset(4, 4, 4, 6)
            };
            miniLabelStyle.normal.textColor = TelleRGUI.HintText;

            clipInfoStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 2, 2)
            };
            clipInfoStyle.normal.textColor = TelleRGUI.Warning;

            frameInfoStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
            frameInfoStyle.normal.textColor = TelleRGUI.Success;

            markerCountStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 8 };
            markerCountStyle.normal.textColor = TelleRGUI.StrongText;

            timerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            statusStyle = new GUIStyle(EditorStyles.label);
            tagStyle = new GUIStyle(EditorStyles.miniLabel);
            tagStyle.normal.textColor = TelleRGUI.Warning;

            pageStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            eventPageStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };

            clipBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 0, 0)
            };

            eventInfoStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, fontSize = 11 };
            eventInfoStyle.normal.textColor = TelleRGUI.Accent;

            editorBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 12),
                margin = new RectOffset(0, 0, 4, 4)
            };

            eventFrameInfoStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 10 };
            eventFrameInfoCurrentStyle = new GUIStyle(eventFrameInfoStyle) { fontStyle = FontStyle.Bold };
            eventFrameInfoCurrentStyle.normal.textColor = TelleRGUI.Success;

            eventBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 8, 8),
                margin = new RectOffset(0, 0, 0, 0)
            };

            smallHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };

            listBoxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(6, 6, 6, 6) };

            eventRowStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                padding = new RectOffset(8, 8, 4, 4)
            };
            eventRowSelectedStyle = new GUIStyle(eventRowStyle) { fontStyle = FontStyle.Bold };
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

                RefreshLists();
            }
        }

        private void RefreshLists()
        {
            lastController = ctrl != null && ctrl.AnimatorComponent != null ? ctrl.AnimatorComponent.runtimeAnimatorController : null;
            RefreshAvailableStates();
            RefreshClipList();
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
                        if (layer.stateMachine == null) continue; // Synced Layer는 stateMachine이 null
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

        // ─── 숨긴 클립 (EditorPrefs / SessionState) ───

        /// <summary>
        /// 영구 저장 키(EditorPrefs). 저장된 적 없는 씬의 오브젝트, 런타임에 생성된 오브젝트처럼 GlobalObjectId가 비어 있으면
        /// 모든 오브젝트가 같은 키를 나눠 쓰게 되므로 null을 돌려준다 — 그동안은 이번 에디터 세션에만 인스턴스별로 보관한다.
        /// </summary>
        private string HiddenClipsPrefKey
        {
            get
            {
                if (ctrl == null) return null;
                var id = GlobalObjectId.GetGlobalObjectIdSlow(ctrl);
                if (id.identifierType == 0 || id.targetObjectId == 0 || id.assetGUID.Empty()) return null;
                return HiddenClipsPrefPrefix + id;
            }
        }

        // GetInstanceID는 Unity 6000.6에서 컴파일 오류(obsolete-as-error)라 6000.3+에서는 GetEntityId를 쓴다
#if UNITY_6000_3_OR_NEWER
        private string HiddenClipsSessionKey => ctrl == null ? null : HiddenClipsSessionPrefix + ctrl.GetEntityId();
#else
        private string HiddenClipsSessionKey => ctrl == null ? null : HiddenClipsSessionPrefix + ctrl.GetInstanceID();
#endif

        private void LoadHiddenClips()
        {
            hiddenKeys.Clear();
            if (ctrl == null) return;

            string prefKey = HiddenClipsPrefKey;
            if (prefKey != null && EditorPrefs.HasKey(prefKey))
            {
                AddHiddenKeys(EditorPrefs.GetString(prefKey, ""));
                return;
            }

            // 저장되지 않은 씬에서 숨겼던 목록(세션 보관) — 그 사이 씬이 저장되어 영구 키가 생겼으면 그쪽으로 옮긴다
            string session = SessionState.GetString(HiddenClipsSessionKey, "");
            if (session.Length > 0)
            {
                AddHiddenKeys(session.Substring(SessionStoredMark.Length));
                if (prefKey != null) SaveHiddenClips();
                return;
            }

            // 1.1 이하에서 컴포넌트에 저장된 값을 한 번만 읽어 옮긴다(컴포넌트는 수정하지 않음)
            var hiddenProp = serializedObject.FindProperty("hiddenClips");
            if (hiddenProp == null || hiddenProp.arraySize == 0) return;

            for (int i = 0; i < hiddenProp.arraySize; i++)
            {
                string k = hiddenProp.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(k)) hiddenKeys.Add(k);
            }
            SaveHiddenClips();
        }

        private void AddHiddenKeys(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var k in raw.Split('\n'))
                if (!string.IsNullOrEmpty(k)) hiddenKeys.Add(k);
        }

        private void SaveHiddenClips()
        {
            if (ctrl == null) return;

            string data = string.Join("\n", hiddenKeys);
            string prefKey = HiddenClipsPrefKey;
            if (prefKey != null)
            {
                EditorPrefs.SetString(prefKey, data);
                SessionState.EraseString(HiddenClipsSessionKey);
            }
            else
            {
                // 빈 목록도 '저장됨'으로 구분하도록 표식을 앞에 붙인다(표식이 없으면 레거시 값을 다시 옮겨 온다)
                SessionState.SetString(HiddenClipsSessionKey, SessionStoredMark + data);
            }
        }

        private void OnSceneSaved(Scene scene)
        {
            // 처음 저장된 씬은 이때 GlobalObjectId가 생긴다 — 세션에만 있던 숨김 목록을 영구 키로 옮긴다
            if (ctrl == null || ctrl.gameObject.scene != scene) return;
            if (SessionState.GetString(HiddenClipsSessionKey, "").Length == 0) return;
            SaveHiddenClips();
        }

        private static string ClipKey(ClipCatalog.ClipInfo info) => $"{info.LayerName}/{info.StateName}";

        private void FilterClipsByVisibility()
        {
            visibleClips = new List<ClipCatalog.ClipInfo>();
            hiddenClipInfos = new List<ClipCatalog.ClipInfo>();

            foreach (var info in clipInfos)
            {
                if (hiddenKeys.Contains(ClipKey(info)))
                    hiddenClipInfos.Add(info);
                else
                    visibleClips.Add(info);
            }
        }

        private void UpdateCurrentClipIndex()
        {
            currentClipIndex = -1;
            if (visibleClips == null || ctrl == null) return;

            var current = ctrl.CurrentClip;
            if (current == null) return;

            int count = visibleClips.Count;
            for (int i = 0; i < count; i++)
            {
                if (visibleClips[i].Clip == current)
                {
                    currentClipIndex = i;
                    return;
                }
            }
        }

        /// <summary>캐시한 선택 위치가 현재 클립을 가리키지 않을 때만 다시 찾는다(참조 비교, 할당 없음).</summary>
        private void SyncCurrentClipIndex()
        {
            if (ctrl != null && visibleClips != null
                && currentClipIndex >= 0 && currentClipIndex < visibleClips.Count
                && visibleClips[currentClipIndex].Clip == ctrl.CurrentClip)
                return;

            UpdateCurrentClipIndex();
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
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("Animator Controller에 클립이 없습니다.", MessageType.Info);
                if (GUILayout.Button(RefreshContent, GUILayout.Width(64), GUILayout.Height(38)))
                    RefreshLists();
                EditorGUILayout.EndHorizontal();
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

            if (GUILayout.Button(RefreshContent, EditorStyles.miniButton, GUILayout.Width(58)))
                RefreshLists();

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
                        GUI.backgroundColor = HiddenClipTint;
                    else if (info.Clip == currentClip)
                        GUI.backgroundColor = TelleRGUI.AccentButton;
                    else if (info.IsDefault)
                        GUI.backgroundColor = DefaultClipTint;

                    int frames = Mathf.RoundToInt(info.Clip.length * info.Clip.frameRate);

                    GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(buttonWidth - 4), GUILayout.Height(62));

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(info.StateName, EditorStyles.boldLabel, GUILayout.Height(16));
                    GUILayout.FlexibleSpace();

                    if (isHiddenList)
                    {
                        if (GUILayout.Button(ShowClipContent, GUILayout.Width(22), GUILayout.Height(16)))
                        {
                            RestoreClip(info);
                            GUIUtility.ExitGUI();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(HideClipContent, GUILayout.Width(22), GUILayout.Height(16)))
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
                            GUI.backgroundColor = prevBg;
                            SelectClipWithState(info);
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
            if (!hiddenKeys.Add(ClipKey(info))) return;
            SaveHiddenClips();
            FilterClipsByVisibility();
            UpdateCurrentClipIndex();
        }

        private void RestoreClip(ClipCatalog.ClipInfo info)
        {
            if (hiddenKeys.Remove(ClipKey(info)))
                SaveHiddenClips();

            FilterClipsByVisibility();
            UpdateCurrentClipIndex();
        }

        private void RestoreAllClips()
        {
            hiddenKeys.Clear();
            SaveHiddenClips();

            FilterClipsByVisibility();
            UpdateCurrentClipIndex();
        }

        private void NavigateClip(int direction)
        {
            if (visibleClips == null || visibleClips.Count == 0) return;

            // 런타임이 방금 클립을 바꿨을 수 있으므로 실제 클립 위치에서 한 칸 옮긴다
            SyncCurrentClipIndex();
            int newIndex = currentClipIndex + direction;
            if (newIndex < 0) newIndex = visibleClips.Count - 1;
            if (newIndex >= visibleClips.Count) newIndex = 0;

            SelectClipWithState(visibleClips[newIndex]);
        }

        private void SelectClipWithState(ClipCatalog.ClipInfo capturedInfo)
        {
            if (ctrl == null || capturedInfo?.Clip == null) return;

            if (IsSceneInstanceInPlayMode)
            {
                // Play Mode(씬 인스턴스): 런타임 그래프까지 교체해야 라벨과 실제 재생 클립이 일치한다(AnimationMode는 쓰지 않음).
                // 비활성 컴포넌트는 런타임이 clip만 바꿔 두고 다시 켜질 때 새 클립으로 이어 간다
                ctrl.ChangeClip(capturedInfo.Clip);
                serializedObject.Update();
            }
            else
            {
                StopAnimationModeIfActive();
                editorPlayState = EditorPlayState.Stopped;

                var clipProp = serializedObject.FindProperty("clip");
                if (clipProp != null)
                {
                    clipProp.objectReferenceValue = capturedInfo.Clip;
                    serializedObject.ApplyModifiedProperties();
                }

                GetPreviewRange(out int start, out int end);
                editorCurrentFrame = ctrl.ReversePlayback ? end : start;
                frameAccumulator = 0f;

                // 선택 후 바로 시작 프레임을 샘플링 — 재생은 Play 버튼으로 (목록을 훑기만 해도
                // 씬 오브젝트가 움직이기 시작하는 부수효과 제거)
                SampleCurrentFrame(true);
            }

            transitionPage = 0;
            UpdateCurrentClipIndex();
            Repaint();
        }

        private void DrawTabWorkspace()
        {
            DrawPreviewControlsSection();

            if (!Application.isPlaying)
            {
                if (animationModeBlocked && !IsPreviewActive)
                    EditorGUILayout.HelpBox("Animation 창이나 Timeline이 미리보기/녹화 중이라 프리뷰를 표시하지 않습니다. 해당 창의 미리보기를 끈 뒤 다시 시도하세요.", MessageType.Warning);
                else if (IsPreviewActive)
                    EditorGUILayout.HelpBox("프리뷰 포즈가 씬에 표시 중입니다 — 선택을 해제하면 원래 포즈로 복구됩니다.", MessageType.None);
            }

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

        private bool DrawFoldoutHeader(bool value, string label, string prefKey)
        {
            EditorGUILayout.BeginHorizontal();
            bool newValue = EditorGUILayout.Foldout(value, label, true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (newValue != value)
                EditorPrefs.SetBool(prefKey, newValue);
            return newValue;
        }

        private void DrawPreviewControlsSection()
        {
            GUILayout.BeginVertical(sectionBox);

            bool live = IsLiveRuntime;
            bool playModeNonLive = IsPlayModeNonLive;
            previewFoldout = DrawFoldoutHeader(previewFoldout, live ? "Playback (Runtime)" : "Preview", PreviewFoldoutPrefKey);

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

            GUILayout.Space(6);

            if (playModeNonLive)
            {
                if (IsSceneInstanceInPlayMode)
                    EditorGUILayout.HelpBox("컴포넌트나 GameObject가 비활성 상태라 재생·프레임 이동을 할 수 없습니다. " +
                                            "여기서 고른 클립은 다시 활성화될 때 적용됩니다.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("Play Mode 중에는 씬에 있는 인스턴스만 재생·프레임 이동을 할 수 있습니다. " +
                                            "프리팹 에셋이나 Prefab Mode의 오브젝트는 클립 선택 등 값 편집만 가능합니다.", MessageType.Info);
                GUILayout.Space(4);
            }

            DrawCurrentClipInfo();

            GUILayout.Space(10);

            // Speed 슬라이더: Play Mode(씬 인스턴스)는 컴포넌트의 재생 속도(즉시 반영), 그 밖에는 프리뷰 전용 속도
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Speed", GUILayout.Width(50));
            if (live)
            {
                var speedProp = serializedObject.FindProperty("playback.PlaybackSpeed");
                if (speedProp != null)
                    speedProp.floatValue = EditorGUILayout.Slider(speedProp.floatValue, 0f, 4f, GUILayout.Height(20));
            }
            else
            {
                editorPlaybackSpeed = EditorGUILayout.Slider(editorPlaybackSpeed, 0f, 4f, GUILayout.Height(20));
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            using (new EditorGUI.DisabledScope(playModeNonLive))
            {
                // Play / Pause / Stop 버튼
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(guiPlaying ? PauseContent : PlayContent, GUILayout.Width(110), GUILayout.Height(30)))
                    TogglePlayPause();

                GUILayout.Space(8);

                if (GUILayout.Button(StopContent, GUILayout.Width(90), GUILayout.Height(30)))
                    StopPlayback();

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(12);

                DrawFrameControlsWithEventMarkers();
            }

            GUILayout.Space(4);
            GUILayout.EndVertical();
        }

        private void EditorPlay()
        {
            if (ctrl == null || ctrl.CurrentClip == null) return;

            GetPreviewRange(out int start, out int end);
            bool reverse = ctrl.ReversePlayback;

            // 구간 밖이거나, 비루프 완주로 끝(역재생이면 시작)에 멈춘 상태면 구간 시작으로 되감기
            if (editorCurrentFrame < start || editorCurrentFrame > end)
                editorCurrentFrame = reverse ? end : start;
            else if (editorPlayState == EditorPlayState.Stopped)
            {
                if (!reverse && editorCurrentFrame >= end) editorCurrentFrame = start;
                else if (reverse && editorCurrentFrame <= start) editorCurrentFrame = end;
            }

            editorPlayState = EditorPlayState.Playing;
            lastEditorTime = EditorApplication.timeSinceStartup;
            frameAccumulator = 0f;

            SampleCurrentFrame(true);
        }

        private void EditorPause()
        {
            editorPlayState = EditorPlayState.Paused;
        }

        private void EditorStop()
        {
            editorPlayState = EditorPlayState.Stopped;
            GetPreviewRange(out int start, out _);
            editorCurrentFrame = start;
            frameAccumulator = 0f;

            SampleCurrentFrame();
            Repaint();
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
            float currentTime = guiFrame / fps;
            float totalTime = clip.length;

            string frameInfo = $"Frame {guiFrame} / {maxFrame}   |   {currentTime:0.000}s / {totalTime:0.000}s   |   {fps:0.#} fps";
            if (IsLiveRuntime)
                frameInfo += $"   |   {ctrl.CurrentPlayState}";
            GUILayout.Label(frameInfo, frameInfoStyle);
        }

        private void DrawFrameControlsWithEventMarkers()
        {
            int maxFrame = GetMaxFrame();
            int current = Mathf.Clamp(guiFrame, 0, maxFrame);
            int target = current;

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(Prev10Content, GUILayout.Width(40), GUILayout.Height(24)))
                target = Mathf.Clamp(current - 10, 0, maxFrame);

            if (GUILayout.Button(PrevFrameContent, GUILayout.Width(34), GUILayout.Height(24)))
                target = Mathf.Clamp(current - 1, 0, maxFrame);

            Rect sliderRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.horizontalSlider, GUILayout.Height(24), GUILayout.ExpandWidth(true));

            int sliderVal = Mathf.RoundToInt(GUI.HorizontalSlider(sliderRect, current, 0, maxFrame));
            if (sliderVal != current)
                target = sliderVal;

            if (GUILayout.Button(NextFrameContent, GUILayout.Width(34), GUILayout.Height(24)))
                target = Mathf.Clamp(current + 1, 0, maxFrame);

            if (GUILayout.Button(Next10Content, GUILayout.Width(40), GUILayout.Height(24)))
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
                JumpTo(target);
            }
        }

        private void DrawEventMarkersOnTimeline(Rect sliderRect, int maxFrame, int currentFrame, FrameEventSystem frameEvents)
        {
            // 레이아웃 공간은 항상 같은 크기로 확보한다(이벤트 유무로 Layout/Repaint가 어긋나지 않도록)
            GUILayout.Space(20);
            if (frameEvents == null || frameEvents.Count == 0) return;

            Event evt = Event.current;
            if (evt.type != EventType.Repaint && evt.type != EventType.MouseDown) return;

            var currentClip = ctrl.CurrentClip;
            float sliderPadding = 7f;
            float usableWidth = sliderRect.width - sliderPadding * 2;
            float markerY = sliderRect.yMax + 2;

            int count = frameEvents.Count;
            for (int i = 0; i < count; i++)
            {
                var ev = frameEvents.GetEvent(i);
                if (ev == null || !ev.AppliesTo(currentClip)) continue;

                int frame = Mathf.Clamp(ev.Frame, 0, maxFrame);

                // 같은 프레임의 첫 이벤트에서만 그룹을 그린다(추가 할당 없이 그룹 크기·선택 여부 계산)
                bool firstOfFrame = true;
                for (int j = 0; j < i; j++)
                {
                    var prev = frameEvents.GetEvent(j);
                    if (prev != null && prev.AppliesTo(currentClip) && Mathf.Clamp(prev.Frame, 0, maxFrame) == frame)
                    {
                        firstOfFrame = false;
                        break;
                    }
                }
                if (!firstOfFrame) continue;

                int groupSize = 0;
                bool isSelected = false;
                for (int j = i; j < count; j++)
                {
                    var other = frameEvents.GetEvent(j);
                    if (other == null || !other.AppliesTo(currentClip) || Mathf.Clamp(other.Frame, 0, maxFrame) != frame) continue;
                    groupSize++;
                    if (j == eventPage) isSelected = true;
                }

                float normalizedPos = maxFrame > 0 ? (float)frame / maxFrame : 0f;
                float xPos = sliderRect.x + sliderPadding + normalizedPos * usableWidth;
                Rect triangleRect = new Rect(xPos - 5, markerY, 10, 10);

                if (evt.type == EventType.Repaint)
                {
                    Color markerColor = isSelected ? TelleRGUI.Accent : TelleRGUI.Warning;
                    if (frame == currentFrame && !isSelected)
                        markerColor = TelleRGUI.Success;

                    DrawTriangleMarker(triangleRect, markerColor);

                    if (groupSize > 1)
                    {
                        Rect countRect = new Rect(xPos - 6, triangleRect.yMax - 2, 12, 10);
                        GUI.Label(countRect, groupSize.ToString(), markerCountStyle);
                    }
                    continue;
                }

                Rect clickRect = new Rect(xPos - 8, markerY - 2, 16, 14);
                if (!clickRect.Contains(evt.mousePosition)) continue;

                if (groupSize == 1)
                {
                    eventPage = i;
                    JumpTo(frame);
                }
                else
                {
                    var menu = new GenericMenu();
                    for (int j = i; j < count; j++)
                    {
                        var other = frameEvents.GetEvent(j);
                        if (other == null || !other.AppliesTo(currentClip) || Mathf.Clamp(other.Frame, 0, maxFrame) != frame) continue;

                        string label = string.IsNullOrEmpty(other.Label) ? $"Event #{j + 1}" : other.Label;
                        int capturedIdx = j;
                        int capturedFrame = frame;
                        menu.AddItem(new GUIContent($"#{j + 1}: {label}"), j == eventPage, () =>
                        {
                            eventPage = capturedIdx;
                            JumpTo(capturedFrame);
                        });
                    }
                    menu.ShowAsContext();
                }
                evt.Use();
                Repaint();
                break;
            }
        }

        private void DrawTriangleMarker(Rect rect, Color color)
        {
            trianglePoints[0] = new Vector3(rect.x + rect.width / 2, rect.y, 0);
            trianglePoints[1] = new Vector3(rect.x, rect.yMax, 0);
            trianglePoints[2] = new Vector3(rect.xMax, rect.yMax, 0);

            Handles.BeginGUI();
            var prev = Handles.color;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(trianglePoints);
            Handles.color = prev;
            Handles.EndGUI();
        }

        // ─────────────────────────── Auto Transitions ───────────────────────────

        private void DrawAutoTransitionsPanel()
        {
            var transitionsProp = serializedObject.FindProperty("autoTransitions");

            GUILayout.BeginVertical(sectionBox);

            int size = transitionsProp != null ? transitionsProp.arraySize : 0;
            transitionFoldout = DrawFoldoutHeader(transitionFoldout, $"Auto Transitions ({size})", TransitionFoldoutPrefKey);

            if (!transitionFoldout)
            {
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(6);

            if (IsLiveRuntime)
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
            bool isPaused = ctrl.IsTransitionPaused;
            float timerValue = ctrl.TransitionTimer;
            string activeTag = ctrl.TransitionTag;

            Color stateColor = isRunning ? TelleRGUI.Success : isPaused ? TelleRGUI.Warning : TelleRGUI.HintText;
            timerStyle.normal.textColor = stateColor;
            statusStyle.normal.textColor = stateColor;

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();

            GUILayout.Label($"{timerValue:F2}s", timerStyle, GUILayout.Width(70));
            GUILayout.Label(isRunning ? "● Running" : isPaused ? "● Paused" : "○ Stopped", statusStyle, GUILayout.Width(75));

            if ((isRunning || isPaused) && !string.IsNullOrEmpty(activeTag))
                GUILayout.Label($"Tag: {activeTag}", tagStyle);

            GUILayout.FlexibleSpace();

            // 같은 자리에 Pause/Resume을 번갈아 그린다(레이아웃 구조 고정)
            using (new EditorGUI.DisabledScope(!isRunning && !isPaused))
            {
                if (GUILayout.Button(isPaused ? "Resume" : "Pause", GUILayout.Width(62), GUILayout.Height(20)))
                {
                    if (isPaused) ctrl.ResumeTransition();
                    else ctrl.PauseTransition();
                }
            }

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

            GUILayout.Label($"{transitionPage + 1} / {count}", pageStyle, GUILayout.Width(60));

            using (new EditorGUI.DisabledScope(transitionPage >= count - 1))
                if (GUILayout.Button(">", GUILayout.Width(24))) transitionPage++;

            GUILayout.FlexibleSpace();

            bool remove = GUILayout.Button("Remove", GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();

            if (remove)
            {
                transitionsProp.DeleteArrayElementAtIndex(transitionPage);
                serializedObject.ApplyModifiedProperties();
                transitionPage = Mathf.Clamp(transitionPage, 0, Mathf.Max(0, transitionsProp.arraySize - 1));
                GUIUtility.ExitGUI();
            }

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
            // 저장된 State가 목록에 없어도(rename 등) 값을 덮어쓰지 않고 (missing) 항목으로 표시만 한다
            bool missingState = currentIndex < 0 && !string.IsNullOrEmpty(stateProp.stringValue);
            string[] stateOptions = availableStates;
            if (missingState)
            {
                stateOptions = new string[availableStates.Length + 1];
                stateOptions[0] = $"(missing) {stateProp.stringValue}";
                Array.Copy(availableStates, 0, stateOptions, 1, availableStates.Length);
                currentIndex = 0;
            }
            else if (currentIndex < 0) currentIndex = 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Target State");
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(currentIndex, stateOptions);
            if (EditorGUI.EndChangeCheck())
            {
                if (missingState)
                {
                    if (newIndex > 0) stateProp.stringValue = stateOptions[newIndex];
                }
                else if (newIndex >= 0 && newIndex < stateOptions.Length)
                    stateProp.stringValue = stateOptions[newIndex];
            }
            EditorGUILayout.EndHorizontal();

            tagProp.stringValue = EditorGUILayout.TextField("Tag (Filter)", tagProp.stringValue);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            float delay = EditorGUILayout.FloatField("Delay", delayProp.floatValue);
            if (EditorGUI.EndChangeCheck()) delayProp.floatValue = Mathf.Max(0f, delay);
            EditorGUILayout.LabelField("sec", GUILayout.Width(28));
            EditorGUILayout.EndHorizontal();

            speedProp.floatValue = EditorGUILayout.Slider(TransitionSpeedContent, speedProp.floatValue, 0f, 4f);
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

            bool live = IsLiveRuntime;
            GUILayout.Label(live ? AllTransitionsPlayContent : AllTransitionsEditContent, EditorStyles.boldLabel);

            transitionListScroll = EditorGUILayout.BeginScrollView(transitionListScroll, GUILayout.Height(100));

            int deleteIndex = -1;
            int count = transitionsProp.arraySize;
            for (int i = 0; i < count; i++)
            {
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
                // 런타임에서 Speed <= 0은 "현재 재생 속도 유지"다
                string speedLabel = speed > 0f ? $"x{speed:0.#}" : "x(현재)";

                EditorGUILayout.BeginHorizontal();

                string tagLabel = string.IsNullOrEmpty(tag) ? "*" : tag;
                string label = $"[{tagLabel}] {delay:0.0}s → {state} ({speedLabel})";

                var prevBg = GUI.backgroundColor;

                if (live && ctrl.IsTransitionRunning)
                {
                    string activeTag = ctrl.TransitionTag;
                    bool isActive = string.IsNullOrEmpty(activeTag) || tag == activeTag;
                    if (isActive)
                        GUI.backgroundColor = TelleRGUI.SuccessButton;
                }
                else if (i == transitionPage)
                {
                    GUI.backgroundColor = TelleRGUI.AccentButton;
                }

                if (GUILayout.Button(label, EditorStyles.miniButtonLeft))
                {
                    if (live)
                        ctrl.PlayTransition(tag);
                    else
                        transitionPage = i;
                }

                GUI.backgroundColor = prevBg;

                if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    deleteIndex = i;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // 루프 밖에서 삭제해 Begin/End 짝이 어긋나지 않게 한다
            if (deleteIndex >= 0)
            {
                transitionsProp.DeleteArrayElementAtIndex(deleteIndex);
                serializedObject.ApplyModifiedProperties();
                transitionPage = Mathf.Clamp(transitionPage, 0, Mathf.Max(0, transitionsProp.arraySize - 1));
                GUIUtility.ExitGUI();
            }
        }

        // ─────────────────────────── Frame Events ───────────────────────────

        private static bool EventAppliesTo(SerializedProperty ev, AnimationClip clip)
        {
            var clipProp = ev.FindPropertyRelative("Clip");
            if (clipProp == null) return true;
            var scoped = clipProp.objectReferenceValue as AnimationClip;
            // Missing(삭제된 클립)은 런타임과 같게 어떤 클립에서도 발사되지 않는 것으로 본다
            return scoped == null ? !IsMissingClip(ev, clipProp) : scoped == clip;
        }

        /// <summary>
        /// 지정했던 클립 에셋이 삭제된 참조인지. 런타임 FrameEvent.IsClipMissing과 같은 규칙:
        /// 저장된 범위(scope)가 있으면 그것으로, 1.2.0 이하 데이터면 남아 있는 참조 ID로 판정한다.
        /// </summary>
        private static bool IsMissingClip(SerializedProperty ev, SerializedProperty clipProp)
        {
            if (clipProp == null || clipProp.objectReferenceValue != null) return false;
            var scopeProp = ev.FindPropertyRelative("scope");
            int scope = scopeProp != null ? scopeProp.intValue : (int)FrameEvent.ClipScope.Unspecified;
            if (scope == (int)FrameEvent.ClipScope.SingleClip) return true;
            if (scope == (int)FrameEvent.ClipScope.AllClips) return false;
            return HasReferenceId(clipProp);
        }

        /// <summary>발사 범위를 참조와 함께 기록한다(null이면 모든 클립). 빌드에서도 Missing과 '모든 클립'이 구분되도록.</summary>
        private static void SetEventClip(SerializedProperty ev, SerializedProperty clipProp, AnimationClip clip)
        {
            clipProp.objectReferenceValue = clip;
            SetEventScope(ev, clip != null);
        }

        private static void SetEventScope(SerializedProperty ev, bool singleClip)
        {
            var scopeProp = ev.FindPropertyRelative("scope");
            if (scopeProp != null)
                scopeProp.intValue = (int)(singleClip ? FrameEvent.ClipScope.SingleClip : FrameEvent.ClipScope.AllClips);
        }

        // objectReferenceInstanceIDValue는 Unity 6000.6에서 컴파일 오류(obsolete-as-error)이고 대체 API(objectReferenceEntityIdValue)는
        // 6000.3에 없다. 중간 버전을 #if로 가를 수 없어 존재하는 속성을 리플렉션으로 읽는다.
        private static System.Reflection.PropertyInfo referenceIdProperty;
        private static object referenceIdNone;

        private static bool HasReferenceId(SerializedProperty prop)
        {
            if (referenceIdProperty == null)
            {
                referenceIdProperty = typeof(SerializedProperty).GetProperty("objectReferenceEntityIdValue")
                                      ?? typeof(SerializedProperty).GetProperty("objectReferenceInstanceIDValue");
                if (referenceIdProperty == null) return false;
                referenceIdNone = System.Activator.CreateInstance(referenceIdProperty.PropertyType);
            }
            object id = referenceIdProperty.GetValue(prop);
            return id != null && !id.Equals(referenceIdNone);
        }

        private void DrawFrameEventsPanel()
        {
            var frameEventsProp = serializedObject.FindProperty("frameEvents");
            var eventsProp = frameEventsProp?.FindPropertyRelative("events");

            GUILayout.BeginVertical(sectionBox);

            int size = eventsProp != null ? eventsProp.arraySize : 0;
            frameEventsFoldout = DrawFoldoutHeader(frameEventsFoldout, $"Frame Events ({size})", FrameEventsFoldoutPrefKey);

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

            // 현재 클립과, 이 클립에서 발사되는 이벤트 수
            int applicable = 0;
            for (int i = 0; i < eventsProp.arraySize; i++)
                if (EventAppliesTo(eventsProp.GetArrayElementAtIndex(i), ctrl.CurrentClip)) applicable++;

            EditorGUILayout.BeginHorizontal(clipBoxStyle, GUILayout.Height(28));
            GUILayout.Label(ctrl.CurrentClip.name, clipInfoStyle, GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{applicable} / {eventsProp.arraySize} events", EditorStyles.miniLabel, GUILayout.Height(20));
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
            int currentFrame = guiFrame;
            var currentClip = ctrl.CurrentClip;

            bool hasEventAtCurrentFrame = false;
            int eventAtCurrentFrameIndex = -1;

            for (int i = 0; i < eventsProp.arraySize; i++)
            {
                var ev = eventsProp.GetArrayElementAtIndex(i);
                int frame = ev.FindPropertyRelative("Frame").intValue;
                if (frame == currentFrame && EventAppliesTo(ev, currentClip))
                {
                    hasEventAtCurrentFrame = true;
                    eventAtCurrentFrameIndex = i;
                    break;
                }
            }

            EditorGUILayout.BeginHorizontal();

            var prevBg = GUI.backgroundColor;
            if (!hasEventAtCurrentFrame)
                GUI.backgroundColor = TelleRGUI.SuccessButton;

            if (GUILayout.Button(AddEventContent, GUILayout.Height(28)))
            {
                AddFrameEvent(eventsProp, currentFrame);
            }

            GUI.backgroundColor = prevBg;

            if (hasEventAtCurrentFrame)
            {
                GUI.backgroundColor = TelleRGUI.AccentButton;
                if (GUILayout.Button($"Select Event #{eventAtCurrentFrameIndex + 1}", GUILayout.Width(140), GUILayout.Height(28)))
                {
                    eventPage = eventAtCurrentFrameIndex;
                }
                GUI.backgroundColor = prevBg;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(SortContent, GUILayout.Width(60), GUILayout.Height(28)))
            {
                SortFrameEvents(eventsProp);
            }

            EditorGUILayout.EndHorizontal();

            if (hasEventAtCurrentFrame)
            {
                GUILayout.Space(4);
                GUILayout.Label($"  ▲ 현재 프레임({currentFrame})에 이벤트가 있습니다", eventInfoStyle);
            }
        }

        private void AddFrameEvent(SerializedProperty eventsProp, int frame)
        {
            eventsProp.arraySize++;
            var newEvent = eventsProp.GetArrayElementAtIndex(eventsProp.arraySize - 1);
            newEvent.FindPropertyRelative("Frame").intValue = frame;
            newEvent.FindPropertyRelative("Label").stringValue = "";

            // 목록이 현재 클립 기준으로 표시되므로 새 이벤트는 현재 클립 전용으로 만든다(Clip을 비우면 모든 클립)
            var clipProp = newEvent.FindPropertyRelative("Clip");
            if (clipProp != null) SetEventClip(newEvent, clipProp, ctrl.CurrentClip);

            // arraySize++는 마지막 요소를 복제하므로, 복제된 UnityEvent 리스너를 비워야 함
            var clonedCalls = newEvent.FindPropertyRelative("OnTriggered.m_PersistentCalls.m_Calls");
            if (clonedCalls != null) clonedCalls.ClearArray();

            serializedObject.ApplyModifiedProperties();
            eventPage = eventsProp.arraySize - 1;
        }

        private void SortFrameEvents(SerializedProperty eventsProp)
        {
            // 남은 구간의 최소 Frame 요소를 MoveArrayElement로 앞으로 끌어오는 선택 정렬
            // (UnityEvent 리스너를 포함한 요소 전체가 함께 이동)
            int n = eventsProp.arraySize;
            for (int i = 0; i < n - 1; i++)
            {
                int minIdx = i;
                int minFrame = eventsProp.GetArrayElementAtIndex(i).FindPropertyRelative("Frame").intValue;
                for (int j = i + 1; j < n; j++)
                {
                    int f = eventsProp.GetArrayElementAtIndex(j).FindPropertyRelative("Frame").intValue;
                    if (f < minFrame)
                    {
                        minFrame = f;
                        minIdx = j;
                    }
                }
                if (minIdx != i) eventsProp.MoveArrayElement(minIdx, i);
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFrameEventEditor(SerializedProperty eventsProp, int index)
        {
            int maxFrame = GetMaxFrame();
            float fps = Mathf.Max(1f, ctrl.CurrentClip.frameRate);

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

            GUILayout.Label($"Event {index + 1} / {eventsProp.arraySize}", eventPageStyle, GUILayout.Height(24));

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
            GUI.backgroundColor = TelleRGUI.DangerButton;
            if (GUILayout.Button("Delete", GUILayout.Width(70), GUILayout.Height(24)))
            {
                GUI.backgroundColor = deleteBtnColor;
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
            var clipProp = ev.FindPropertyRelative("Clip");
            var onTriggeredProp = ev.FindPropertyRelative("OnTriggered");

            int currentFrame = frameProp.intValue;

            // Frame 슬라이더
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Frame", GUILayout.Width(50));

            int newFrame = EditorGUILayout.IntSlider(currentFrame, 0, maxFrame, GUILayout.Height(20));

            var jumpBtnColor = GUI.backgroundColor;
            bool isAtEventFrame = guiFrame == currentFrame;
            if (!isAtEventFrame)
                GUI.backgroundColor = TelleRGUI.AccentButton;

            if (GUILayout.Button(isAtEventFrame ? AtEventContent : JumpToEventContent, GUILayout.Width(30), GUILayout.Height(20)))
            {
                JumpTo(currentFrame);
            }
            GUI.backgroundColor = jumpBtnColor;

            EditorGUILayout.EndHorizontal();

            if (newFrame != currentFrame)
            {
                frameProp.intValue = newFrame;
                serializedObject.ApplyModifiedProperties();
                JumpTo(newFrame);
            }

            // 프레임 정보 표시
            float time = currentFrame / fps;
            string frameInfo = $"{currentFrame}f / {maxFrame}f  @{fps:0.#}fps  →  {time:0.000}s";
            if (isAtEventFrame)
                frameInfo += "  (현재)";

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(frameInfo, isAtEventFrame ? eventFrameInfoCurrentStyle : eventFrameInfoStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Label 필드 - 크게
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Label", GUILayout.Width(50));
            labelProp.stringValue = EditorGUILayout.TextField(labelProp.stringValue, GUILayout.Height(22));
            EditorGUILayout.EndHorizontal();

            // 발사 범위(클립) — None이면 모든 클립
            if (clipProp != null)
            {
                GUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(ScopeClipContent, GUILayout.Width(50));
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(clipProp, GUIContent.none);
                if (EditorGUI.EndChangeCheck())
                    SetEventScope(ev, clipProp.objectReferenceValue != null);
                using (new EditorGUI.DisabledScope(clipProp.objectReferenceValue == ctrl.CurrentClip))
                {
                    if (GUILayout.Button(ThisClipContent, EditorStyles.miniButtonLeft, GUILayout.Width(64)))
                        SetEventClip(ev, clipProp, ctrl.CurrentClip);
                }
                bool missingClip = IsMissingClip(ev, clipProp);
                using (new EditorGUI.DisabledScope(clipProp.objectReferenceValue == null && !missingClip))
                {
                    if (GUILayout.Button(AllClipsContent, EditorStyles.miniButtonRight, GUILayout.Width(64)))
                        SetEventClip(ev, clipProp, null);
                }
                EditorGUILayout.EndHorizontal();

                var scoped = clipProp.objectReferenceValue as AnimationClip;
                if (missingClip)
                    EditorGUILayout.HelpBox("지정했던 클립이 삭제되어(Missing) 이 이벤트는 어떤 클립에서도 발사되지 않습니다. This Clip 또는 All Clips로 다시 지정하세요.", MessageType.Warning);
                else if (scoped != null && scoped != ctrl.CurrentClip)
                    EditorGUILayout.HelpBox($"이 이벤트는 '{scoped.name}' 클립 전용이라 현재 클립에서는 발사되지 않습니다.", MessageType.Info);
            }

            GUILayout.Space(10);

            // UnityEvent - 별도 박스로 감싸기
            EditorGUILayout.BeginVertical(eventBoxStyle);

            GUILayout.Label("On Triggered ()", smallHeaderStyle);
            GUILayout.Space(4);

            EditorGUILayout.PropertyField(onTriggeredProp, GUIContent.none);

            EditorGUILayout.EndVertical();

            GUILayout.Space(6);

            DrawMiniLabel("UnityEvent 기반: 재생 중 해당 Frame을 지나가면 한 번 호출됩니다(Start~End Frame 구간 안에서만).");

            EditorGUILayout.EndVertical();
        }

        private void JumpToEventFrame(SerializedProperty eventsProp, int eventIndex)
        {
            if (eventIndex < 0 || eventIndex >= eventsProp.arraySize) return;

            var ev = eventsProp.GetArrayElementAtIndex(eventIndex);
            int frame = ev.FindPropertyRelative("Frame").intValue;
            JumpTo(frame);
        }

        private void DrawFrameEventsList(SerializedProperty eventsProp)
        {
            GUILayout.Label(AllEventsContent, smallHeaderStyle);
            GUILayout.Space(4);

            EditorGUILayout.BeginVertical(listBoxStyle);

            eventsListScroll = EditorGUILayout.BeginScrollView(eventsListScroll, GUILayout.Height(140));

            int currentPlayFrame = guiFrame;
            var currentClip = ctrl.CurrentClip;

            for (int i = 0; i < eventsProp.arraySize; i++)
            {
                var ev = eventsProp.GetArrayElementAtIndex(i);
                int frame = ev.FindPropertyRelative("Frame").intValue;
                string label = ev.FindPropertyRelative("Label").stringValue;
                var clipProp = ev.FindPropertyRelative("Clip");
                var scoped = clipProp != null ? clipProp.objectReferenceValue as AnimationClip : null;
                bool missingClip = IsMissingClip(ev, clipProp);
                bool applies = scoped == null ? !missingClip : scoped == currentClip;

                if (string.IsNullOrEmpty(label))
                    label = $"Event #{i + 1}";

                bool isSelected = i == eventPage;
                bool isAtCurrentFrame = applies && frame == currentPlayFrame;

                EditorGUILayout.BeginHorizontal();

                var prevBg = GUI.backgroundColor;
                var prevColor = GUI.color;

                if (isSelected)
                    GUI.backgroundColor = TelleRGUI.AccentButton;
                else if (isAtCurrentFrame)
                    GUI.backgroundColor = TelleRGUI.SuccessButton;

                // 다른 클립 전용 이벤트는 흐리게 표시한다
                if (!applies)
                    GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * 0.55f);

                string frameIndicator = isAtCurrentFrame ? "●" : " ";
                string scopeLabel = missingClip ? "  (Missing Clip)"
                    : scoped == null ? "  (All Clips)"
                    : scoped != currentClip ? $"  ({scoped.name})" : "";
                string buttonLabel = $"{frameIndicator} [{frame,3}f] {label}{scopeLabel}";

                if (GUILayout.Button(buttonLabel, isSelected ? eventRowSelectedStyle : eventRowStyle, GUILayout.Height(24)))
                {
                    GUI.backgroundColor = prevBg;
                    GUI.color = prevColor;
                    eventPage = i;
                    JumpTo(frame);
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = prevBg;
                GUI.color = prevColor;

                if (GUILayout.Button(DeleteEventContent, GUILayout.Width(26), GUILayout.Height(24)))
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

        // ─────────────────────────── Settings ───────────────────────────

        private void DrawTabSettings()
        {
            var playbackProp = serializedObject.FindProperty("playback");
            if (playbackProp == null)
            {
                EditorGUILayout.HelpBox("playback 프로퍼티를 찾을 수 없습니다.", MessageType.Error);
                return;
            }

            bool hasClip = ctrl != null && ctrl.CurrentClip != null;
            int maxFrame = GetMaxFrame();

            GUILayout.BeginVertical(sectionBox);
            GUILayout.Label("Playback", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var speedProp = playbackProp.FindPropertyRelative("PlaybackSpeed");
            speedProp.floatValue = EditorGUILayout.Slider("Playback Speed", speedProp.floatValue, 0f, 4f);
            DrawMiniLabel("재생 속도 배율입니다. 0이면 재생 상태를 유지한 채 제자리에 멈춥니다.");

            GUILayout.Space(4);
            EditorGUILayout.PropertyField(playbackProp.FindPropertyRelative("Loop"));
            DrawMiniLabel("활성화하면 EndFrame에서 StartFrame으로 돌아가 반복 재생됩니다. 끄면 EndFrame에서 멈추고 OnAnimationComplete가 호출됩니다.");

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

            // 직접 편집할 때만 범위로 자른다(인스펙터를 열기만 해도 값이 바뀌어 씬이 더러워지지 않도록)
            EditorGUI.BeginChangeCheck();
            int newStart = EditorGUILayout.IntField("Start Frame", startFrame.intValue);
            if (EditorGUI.EndChangeCheck())
                startFrame.intValue = hasClip ? Mathf.Clamp(newStart, 0, maxFrame) : Mathf.Max(0, newStart);
            DrawMiniLabel("재생이 시작되는 프레임입니다.");

            GUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            int newEnd = EditorGUILayout.IntField("End Frame", endFrame.intValue);
            if (EditorGUI.EndChangeCheck())
                endFrame.intValue = hasClip ? Mathf.Clamp(newEnd, -1, maxFrame) : Mathf.Max(-1, newEnd);
            DrawMiniLabel("-1로 설정하면 클립의 마지막 프레임을 사용합니다.");

            if (hasClip)
            {
                int resolvedStart = Mathf.Clamp(startFrame.intValue, 0, maxFrame);
                int resolvedEnd = endFrame.intValue < 0 ? maxFrame : Mathf.Min(endFrame.intValue, maxFrame);
                if (resolvedStart >= resolvedEnd)
                    EditorGUILayout.HelpBox($"Start Frame({resolvedStart})이 End Frame({resolvedEnd})보다 작아야 합니다. 지금은 루프가 동작하지 않고, 비루프 재생은 시작하자마자 완료됩니다.", MessageType.Warning);
                else if (startFrame.intValue > maxFrame || endFrame.intValue > maxFrame)
                    EditorGUILayout.HelpBox($"현재 클립은 {maxFrame}프레임까지라 범위를 넘는 값은 클립 끝으로 처리됩니다.", MessageType.Info);
            }

            GUILayout.EndVertical();

            GUILayout.Space(4);

            GUILayout.BeginVertical(sectionBox);
            GUILayout.Label("Initial State", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var useInit = playbackProp.FindPropertyRelative("UseInitialFrameOnStart");
            var initFrame = playbackProp.FindPropertyRelative("InitialFrameOnStart");

            EditorGUILayout.PropertyField(useInit, UseInitialFrameContent);
            DrawMiniLabel("Start() 시점에 지정된 프레임에서 시작합니다.");

            GUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            int newInit = EditorGUILayout.IntField("Initial Frame", initFrame.intValue);
            if (EditorGUI.EndChangeCheck())
                initFrame.intValue = hasClip ? Mathf.Clamp(newInit, -1, maxFrame) : Mathf.Max(-1, newInit);
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
