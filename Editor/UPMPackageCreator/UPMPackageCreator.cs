using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TelleR
{
    public class UPMPackageCreator : EditorWindow
    {
        private const string PREF_LAST_PATH = "UPMCreator_LastPath";

        private enum PendingKind
        {
            Script,
            Image
        }

        private enum PendingPlacement
        {
            Auto,
            Editor,
            Runtime
        }

        [Serializable]
        private class PendingItem
        {
            public string SourceFullPath;
            public PendingKind Kind;
            public PendingPlacement Placement;
            public bool UseResources;
            public bool DetectedEditor;
        }

        private TextField packageNameField;
        private TextField displayNameField;
        private TextField versionField;
        private TextField descriptionField;
        private TextField authorField;
        private TextField unityVersionField;
        private TextField newFeatureNameField;

        private VisualElement step1Container;
        private VisualElement step2Container;
        private VisualElement step3Container;
        private VisualElement featureListContainer;
        private VisualElement dropZone;
        private VisualElement pendingListContainer;
        private Button confirmButton;

        private Label step1Status;
        private Label step2Status;
        private Label devModeStatus;
        private Button devModeButton;

        private VisualElement organizerContainer;
        private PopupField<string> organizerFeaturePopup;
        private ScrollView organizerScroll;

        private string currentPackagePath;
        private readonly List<PendingItem> pendingItems = new List<PendingItem>();

        [MenuItem("Tools/TelleR/Tool/UPM Package Creator")]
        public static void ShowWindow()
        {
            var window = GetWindow<UPMPackageCreator>();
            window.titleContent = new GUIContent("UPM Package Creator");
            window.minSize = new Vector2(520, 780);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();

            var pageScroll = new ScrollView();
            pageScroll.style.flexGrow = 1;
            pageScroll.style.paddingTop = 10;
            pageScroll.style.paddingLeft = 15;
            pageScroll.style.paddingRight = 15;
            pageScroll.style.paddingBottom = 10;
            root.Add(pageScroll);

            var title = new Label("UPM Package Creator");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 15;
            title.style.alignSelf = Align.Center;
            pageScroll.Add(title);

            CreateStep1(pageScroll);
            CreateStep2(pageScroll);
            CreateStep3(pageScroll);

            UpdateStepStates();
            TryLoadLastPackage();
        }

        

        private void CreateStep1(VisualElement root)
        {
            step1Container = CreateStepContainer(root, "STEP 1", "패키지 생성 또는 불러오기");

            packageNameField = CreateTextField(step1Container, "Package Name", "com.teller.util");
            displayNameField = CreateTextField(step1Container, "Display Name", "TelleR Utilities");
            descriptionField = CreateTextField(step1Container, "Description", "Unity utilities by TelleR");
            authorField = CreateTextField(step1Container, "Author", "TelleR");
            unityVersionField = CreateTextField(step1Container, "Min Unity Version", "2021.3");

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 10;

            var createBtn = new Button(CreateNewPackage);
            createBtn.text = "새 패키지 생성";
            createBtn.style.flexGrow = 1;
            createBtn.style.height = 32;
            createBtn.style.marginRight = 5;
            buttonRow.Add(createBtn);

            var loadBtn = new Button(LoadExistingPackage);
            loadBtn.text = "기존 패키지 불러오기";
            loadBtn.style.flexGrow = 1;
            loadBtn.style.height = 32;
            buttonRow.Add(loadBtn);

            step1Container.Add(buttonRow);

            step1Status = new Label("");
            step1Status.style.marginTop = 8;
            step1Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            step1Container.Add(step1Status);
        }

        private VisualElement step2TabsRow;
        private VisualElement step2TabAdd;
        private VisualElement step2TabList;
        private VisualElement step2TabOrganize;
        private Button step2TabAddBtn;
        private Button step2TabListBtn;
        private Button step2TabOrganizeBtn;

        private void CreateStep2(VisualElement root)
        {
            step2Container = CreateStepContainer(root, "STEP 2", "기능 관리");

            step2TabsRow = new VisualElement();
            step2TabsRow.style.flexDirection = FlexDirection.Row;
            step2TabsRow.style.alignItems = Align.Center;
            step2TabsRow.style.marginBottom = 10;
            step2Container.Add(step2TabsRow);

            step2TabAddBtn = CreateTabButton("추가", () => SelectStep2Tab(Step2Tab.Add));
            step2TabListBtn = CreateTabButton("기능 목록", () => SelectStep2Tab(Step2Tab.List));
            step2TabOrganizeBtn = CreateTabButton("수정", () => SelectStep2Tab(Step2Tab.Organize));

            step2TabsRow.Add(step2TabAddBtn);
            step2TabsRow.Add(step2TabListBtn);
            step2TabsRow.Add(step2TabOrganizeBtn);

            step2TabAdd = new VisualElement();
            step2TabList = new VisualElement();
            step2TabOrganize = new VisualElement();

            step2Container.Add(step2TabAdd);
            step2Container.Add(step2TabList);
            step2Container.Add(step2TabOrganize);

            CreateStep2AddUI(step2TabAdd);
            CreateStep2ListUI(step2TabList);
            CreateOrganizerUI(step2TabOrganize);

            SelectStep2Tab(Step2Tab.Add);
        }

        private enum Step2Tab
        {
            Add,
            List,
            Organize
        }

        private Button CreateTabButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.height = 26;
            btn.style.minHeight = 26;
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            btn.style.marginRight = 6;
            btn.style.borderTopLeftRadius = 6;
            btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6;
            btn.style.borderBottomRightRadius = 6;
            btn.style.backgroundColor = new Color(0.20f, 0.20f, 0.20f);
            return btn;
        }

        private void SelectStep2Tab(Step2Tab tab)
        {
            step2TabAdd.style.display = tab == Step2Tab.Add ? DisplayStyle.Flex : DisplayStyle.None;
            step2TabList.style.display = tab == Step2Tab.List ? DisplayStyle.Flex : DisplayStyle.None;
            step2TabOrganize.style.display = tab == Step2Tab.Organize ? DisplayStyle.Flex : DisplayStyle.None;

            SetTabSelected(step2TabAddBtn, tab == Step2Tab.Add);
            SetTabSelected(step2TabListBtn, tab == Step2Tab.List);
            SetTabSelected(step2TabOrganizeBtn, tab == Step2Tab.Organize);

            if (tab == Step2Tab.List)
                RefreshFeatureList();

            if (tab == Step2Tab.Organize)
            {
                RefreshOrganizerFeatureOptions();
                RefreshOrganizer();
            }
        }

        private void SetTabSelected(Button button, bool selected)
        {
            button.style.backgroundColor = selected ? new Color(0.24f, 0.40f, 0.70f) : new Color(0.20f, 0.20f, 0.20f);
            button.style.color = Color.white;
        }

        private void CreateStep2AddUI(VisualElement parent)
        {
            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.alignItems = Align.Center;
            addRow.style.marginBottom = 10;

            var featureLabel = new Label("기능 이름");
            featureLabel.style.width = 70;
            parent.Add(addRow);
            addRow.Add(featureLabel);

            newFeatureNameField = new TextField();
            newFeatureNameField.value = "";
            newFeatureNameField.style.flexGrow = 1;
            newFeatureNameField.style.height = 24;
            newFeatureNameField.style.minHeight = 24;
            addRow.Add(newFeatureNameField);

            dropZone = new VisualElement();
            dropZone.style.height = 120;
            dropZone.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            dropZone.style.borderTopLeftRadius = 10;
            dropZone.style.borderTopRightRadius = 10;
            dropZone.style.borderBottomLeftRadius = 10;
            dropZone.style.borderBottomRightRadius = 10;
            dropZone.style.borderTopWidth = 2;
            dropZone.style.borderBottomWidth = 2;
            dropZone.style.borderLeftWidth = 2;
            dropZone.style.borderRightWidth = 2;
            dropZone.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.justifyContent = Justify.Center;
            dropZone.style.alignItems = Align.Center;

            var dropLabel = new Label("여기에 .cs / 이미지(.png/.jpg 등)를 드래그하세요\n\nEditor/Runtime 자동 분류 + Resources 폴더 자동 + meta 자동");
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.color = new Color(0.65f, 0.65f, 0.65f);
            dropZone.Add(dropLabel);

            dropZone.RegisterCallback<DragEnterEvent>(OnDragEnter);
            dropZone.RegisterCallback<DragLeaveEvent>(OnDragLeave);
            dropZone.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            dropZone.RegisterCallback<DragPerformEvent>(OnDragPerform);

            parent.Add(dropZone);

            pendingListContainer = new VisualElement();
            pendingListContainer.style.marginTop = 10;
            pendingListContainer.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            pendingListContainer.style.borderTopLeftRadius = 8;
            pendingListContainer.style.borderTopRightRadius = 8;
            pendingListContainer.style.borderBottomLeftRadius = 8;
            pendingListContainer.style.borderBottomRightRadius = 8;
            pendingListContainer.style.paddingTop = 8;
            pendingListContainer.style.paddingBottom = 8;
            pendingListContainer.style.paddingLeft = 10;
            pendingListContainer.style.paddingRight = 10;
            pendingListContainer.style.display = DisplayStyle.None;
            parent.Add(pendingListContainer);

            confirmButton = new Button(ConfirmAddItems);
            confirmButton.text = "추가 확정";
            confirmButton.style.height = 36;
            confirmButton.style.marginTop = 10;
            confirmButton.style.backgroundColor = new Color(0.20f, 0.55f, 0.30f);
            confirmButton.style.display = DisplayStyle.None;
            parent.Add(confirmButton);

            var bottomRow = new VisualElement();
            bottomRow.style.flexDirection = FlexDirection.Row;
            bottomRow.style.marginTop = 10;

            var openFolderBtn = new Button(OpenPackageFolder);
            openFolderBtn.text = "패키지 폴더 열기";
            openFolderBtn.style.height = 28;
            openFolderBtn.style.flexGrow = 1;
            openFolderBtn.style.marginRight = 6;
            bottomRow.Add(openFolderBtn);

            var clearPendingBtn = new Button(ClearPending);
            clearPendingBtn.text = "대기 비우기";
            clearPendingBtn.style.height = 28;
            clearPendingBtn.style.width = 92;
            bottomRow.Add(clearPendingBtn);

            parent.Add(bottomRow);

            step2Status = new Label("← STEP 1을 먼저 완료하세요");
            step2Status.style.marginTop = 8;
            step2Status.style.color = Color.gray;
            step2Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            parent.Add(step2Status);
        }

        private void CreateStep2ListUI(VisualElement parent)
        {
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 8;

            var header = new Label("기능 목록");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.flexGrow = 1;
            headerRow.Add(header);

            var refreshBtn = new Button(RefreshFeatureList);
            refreshBtn.text = "새로고침";
            refreshBtn.style.height = 24;
            refreshBtn.style.width = 78;
            headerRow.Add(refreshBtn);

            parent.Add(headerRow);

            featureListContainer = new VisualElement();
            featureListContainer.style.flexDirection = FlexDirection.Row;
            featureListContainer.style.flexWrap = Wrap.Wrap;
            featureListContainer.style.alignItems = Align.FlexStart;
            featureListContainer.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            featureListContainer.style.borderTopLeftRadius = 10;
            featureListContainer.style.borderTopRightRadius = 10;
            featureListContainer.style.borderBottomLeftRadius = 10;
            featureListContainer.style.borderBottomRightRadius = 10;
            featureListContainer.style.paddingTop = 10;
            featureListContainer.style.paddingBottom = 10;
            featureListContainer.style.paddingLeft = 10;
            featureListContainer.style.paddingRight = 10;
            parent.Add(featureListContainer);
        }



        private void CreateOrganizerUI(VisualElement parent)
        {
            organizerContainer = new VisualElement();
            organizerContainer.style.marginTop = 14;
            organizerContainer.style.paddingTop = 10;
            organizerContainer.style.borderTopWidth = 1;
            organizerContainer.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            var header = new Label("정리 모드");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.flexGrow = 1;
            headerRow.Add(header);

            var reloadBtn = new Button(RefreshOrganizer);
            reloadBtn.text = "새로고침";
            reloadBtn.style.height = 22;
            reloadBtn.style.width = 72;
            headerRow.Add(reloadBtn);

            organizerContainer.Add(headerRow);

            var popupRow = new VisualElement();
            popupRow.style.flexDirection = FlexDirection.Row;
            popupRow.style.alignItems = Align.Center;
            popupRow.style.marginTop = 8;

            var popupLabel = new Label("기능:");
            popupLabel.style.width = 40;
            popupRow.Add(popupLabel);

            organizerFeaturePopup = new PopupField<string>(new List<string> { "-" }, 0);
            organizerFeaturePopup.style.flexGrow = 1;
            organizerFeaturePopup.RegisterValueChangedCallback(_ => RefreshOrganizer());
            popupRow.Add(organizerFeaturePopup);

            organizerContainer.Add(popupRow);

            organizerScroll = new ScrollView();
            organizerScroll.style.marginTop = 10;
            organizerScroll.style.height = 220;
            organizerScroll.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            organizerScroll.style.borderTopLeftRadius = 6;
            organizerScroll.style.borderTopRightRadius = 6;
            organizerScroll.style.borderBottomLeftRadius = 6;
            organizerScroll.style.borderBottomRightRadius = 6;
            organizerScroll.style.paddingTop = 8;
            organizerScroll.style.paddingBottom = 8;
            organizerScroll.style.paddingLeft = 10;
            organizerScroll.style.paddingRight = 10;

            organizerContainer.Add(organizerScroll);
            parent.Add(organizerContainer);

            RefreshOrganizerFeatureOptions();
            RefreshOrganizer();
        }

        private void ClearPending()
        {
            pendingItems.Clear();
            RefreshPendingList();
        }

        private void OnDragEnter(DragEnterEvent evt)
        {
            dropZone.style.borderTopColor = new Color(0.3f, 0.7f, 1f);
            dropZone.style.borderBottomColor = new Color(0.3f, 0.7f, 1f);
            dropZone.style.borderLeftColor = new Color(0.3f, 0.7f, 1f);
            dropZone.style.borderRightColor = new Color(0.3f, 0.7f, 1f);
            dropZone.style.backgroundColor = new Color(0.2f, 0.25f, 0.3f);
        }

        private void OnDragLeave(DragLeaveEvent evt)
        {
            ResetDropZoneStyle();
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            ResetDropZoneStyle();

            if (string.IsNullOrEmpty(currentPackagePath))
            {
                EditorUtility.DisplayDialog("Error", "먼저 STEP 1을 완료하세요.", "OK");
                return;
            }

            string featureName = NormalizeFeatureName(newFeatureNameField.value);
            if (string.IsNullOrEmpty(featureName))
            {
                EditorUtility.DisplayDialog("Error", "기능 이름을 먼저 입력하세요.\n(예: AudioVolume3D)", "OK");
                return;
            }

            var objects = DragAndDrop.objectReferences?.ToList() ?? new List<UnityEngine.Object>();
            if (objects.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "드래그된 항목이 없습니다.", "OK");
                return;
            }

            int added = 0;

            foreach (var obj in objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath)) continue;

                string fullPath = Path.GetFullPath(assetPath);

                if (obj is MonoScript)
                {
                    if (pendingItems.Any(p => p.SourceFullPath == fullPath)) continue;

                    string content = SafeReadAllText(fullPath);
                    bool detectedEditor = IsEditorScript(content);

                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Script,
                        Placement = PendingPlacement.Auto,
                        UseResources = false,
                        DetectedEditor = detectedEditor
                    });

                    added++;
                    continue;
                }

                if (IsImageAsset(assetPath))
                {
                    if (pendingItems.Any(p => p.SourceFullPath == fullPath)) continue;

                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Image,
                        Placement = PendingPlacement.Runtime,
                        UseResources = true,
                        DetectedEditor = false
                    });

                    added++;
                    continue;
                }
            }

            if (added == 0)
            {
                EditorUtility.DisplayDialog("Error", ".cs 또는 이미지 파일만 추가할 수 있어요.", "OK");
                return;
            }

            RefreshPendingList();
        }

        private void RefreshPendingList()
        {
            pendingListContainer.Clear();

            if (pendingItems.Count == 0)
            {
                pendingListContainer.style.display = DisplayStyle.None;
                confirmButton.style.display = DisplayStyle.None;
                return;
            }

            pendingListContainer.style.display = DisplayStyle.Flex;
            confirmButton.style.display = DisplayStyle.Flex;

            var header = new Label($"대기 중: {pendingItems.Count}개 (분류를 확인 후 확정)");
            header.style.marginBottom = 8;
            header.style.color = new Color(0.7f, 0.9f, 0.7f);
            pendingListContainer.Add(header);

            foreach (var item in pendingItems.ToList())
            {
                string fileName = Path.GetFileName(item.SourceFullPath);

                var row = new VisualElement();
                row.style.width = new Length(48, LengthUnit.Percent);
                row.style.marginRight = 10;
                row.style.marginBottom = 10;
                row.style.paddingTop = 8;
                row.style.paddingBottom = 8;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                row.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
                row.style.borderTopLeftRadius = 10;
                row.style.borderTopRightRadius = 10;
                row.style.borderBottomLeftRadius = 10;
                row.style.borderBottomRightRadius = 10;
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 6;

                var kindLabel = new Label(item.Kind == PendingKind.Script ? "CS" : "IMG");
                kindLabel.style.width = 34;
                kindLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                kindLabel.style.color = item.Kind == PendingKind.Script ? new Color(0.75f, 0.85f, 1f) : new Color(1f, 0.85f, 0.6f);
                row.Add(kindLabel);

                var nameLabel = new Label(fileName);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.marginRight = 8;
                row.Add(nameLabel);

                var placementChoices = item.Kind == PendingKind.Script
                    ? new List<string> { "Auto", "Editor", "Runtime" }
                    : new List<string> { "Editor", "Runtime" };

                int defaultIndex = 0;
                if (item.Kind == PendingKind.Script)
                {
                    defaultIndex = item.Placement == PendingPlacement.Auto ? 0 : (item.Placement == PendingPlacement.Editor ? 1 : 2);
                }
                else
                {
                    defaultIndex = item.Placement == PendingPlacement.Editor ? 0 : 1;
                }

                var placementPopup = new PopupField<string>(placementChoices, Mathf.Clamp(defaultIndex, 0, placementChoices.Count - 1));
                placementPopup.style.width = 92;
                placementPopup.style.marginRight = 6;
                placementPopup.RegisterValueChangedCallback(e =>
                {
                    if (item.Kind == PendingKind.Script)
                    {
                        item.Placement = e.newValue == "Auto" ? PendingPlacement.Auto : (e.newValue == "Editor" ? PendingPlacement.Editor : PendingPlacement.Runtime);
                    }
                    else
                    {
                        item.Placement = e.newValue == "Editor" ? PendingPlacement.Editor : PendingPlacement.Runtime;
                    }
                });
                row.Add(placementPopup);

                if (item.Kind == PendingKind.Image)
                {
                    var resourcesToggle = new Toggle("Res");
                    resourcesToggle.value = item.UseResources;
                    resourcesToggle.style.width = 56;
                    resourcesToggle.style.marginRight = 6;
                    resourcesToggle.RegisterValueChangedCallback(e => item.UseResources = e.newValue);
                    row.Add(resourcesToggle);
                }
                else
                {
                    var hint = new Label(item.DetectedEditor ? "Auto=E" : "Auto=R");
                    hint.style.width = 56;
                    hint.style.color = Color.gray;
                    hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                    hint.style.marginRight = 6;
                    row.Add(hint);
                }

                string capturedPath = item.SourceFullPath;
                var removeBtn = new Button(() => RemovePendingItem(capturedPath));
                removeBtn.text = "✕";
                removeBtn.style.width = 22;
                removeBtn.style.height = 18;
                removeBtn.style.backgroundColor = new Color(0.4f, 0.2f, 0.2f);
                row.Add(removeBtn);

                pendingListContainer.Add(row);
            }
        }

        private void RemovePendingItem(string path)
        {
            var target = pendingItems.FirstOrDefault(p => p.SourceFullPath == path);
            if (target != null) pendingItems.Remove(target);
            RefreshPendingList();
        }

        private void ConfirmAddItems()
        {
            if (pendingItems.Count == 0) return;

            string featureName = NormalizeFeatureName(newFeatureNameField.value);
            if (string.IsNullOrEmpty(featureName))
            {
                EditorUtility.DisplayDialog("Error", "기능 이름을 먼저 입력하세요.", "OK");
                return;
            }

            ProcessPendingItems(featureName);
        }

        private void ProcessPendingItems(string featureName)
        {
            EnsureEditorRootIfNeeded();
            EnsureRuntimeRootIfNeeded();

            string editorRoot = Path.Combine(currentPackagePath, "Editor");
            string runtimeRoot = Path.Combine(currentPackagePath, "Runtime");

            string editorFeaturePath = Path.Combine(editorRoot, featureName);
            string runtimeFeaturePath = Path.Combine(runtimeRoot, featureName);

            string editorResourcesPath = Path.Combine(editorRoot, "Resources", featureName);
            string runtimeResourcesPath = Path.Combine(runtimeRoot, "Resources", featureName);

            var addedScriptsEditor = new List<string>();
            var addedScriptsRuntime = new List<string>();
            var addedImagesEditor = new List<string>();
            var addedImagesRuntime = new List<string>();
            var addedImagesEditorRes = new List<string>();
            var addedImagesRuntimeRes = new List<string>();

            foreach (var item in pendingItems)
            {
                if (!File.Exists(item.SourceFullPath)) continue;

                string fileName = Path.GetFileName(item.SourceFullPath);

                if (item.Kind == PendingKind.Script)
                {
                    bool isEditor = item.Placement == PendingPlacement.Auto ? item.DetectedEditor : (item.Placement == PendingPlacement.Editor);
                    string targetDir = isEditor ? editorFeaturePath : runtimeFeaturePath;

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                        CreateFolderMeta(isEditor ? editorRoot : runtimeRoot, featureName);
                    }

                    string dest = Path.Combine(targetDir, fileName);
                    File.Copy(item.SourceFullPath, dest, true);
                    CreateMetaFromSourceOrDefault(item.SourceFullPath, dest, PendingKind.Script);

                    if (isEditor) addedScriptsEditor.Add(fileName);
                    else addedScriptsRuntime.Add(fileName);
                }
                else
                {
                    bool toEditor = item.Placement == PendingPlacement.Editor;
                    string root = toEditor ? editorRoot : runtimeRoot;

                    if (toEditor) EnsureEditorRootIfNeeded();
                    else EnsureRuntimeRootIfNeeded();

                    string targetDir;

                    if (item.UseResources)
                    {
                        string resRoot = Path.Combine(root, "Resources");
                        if (!Directory.Exists(resRoot))
                        {
                            Directory.CreateDirectory(resRoot);
                            CreateFolderMeta(root, "Resources");
                        }

                        targetDir = toEditor ? editorResourcesPath : runtimeResourcesPath;
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                            CreateFolderMeta(Path.Combine(root, "Resources"), featureName);
                        }
                    }
                    else
                    {
                        targetDir = toEditor ? editorFeaturePath : runtimeFeaturePath;
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                            CreateFolderMeta(root, featureName);
                        }
                    }

                    string dest = Path.Combine(targetDir, fileName);
                    File.Copy(item.SourceFullPath, dest, true);
                    CreateMetaFromSourceOrDefault(item.SourceFullPath, dest, PendingKind.Image);

                    if (toEditor)
                    {
                        if (item.UseResources) addedImagesEditorRes.Add(fileName);
                        else addedImagesEditor.Add(fileName);
                    }
                    else
                    {
                        if (item.UseResources) addedImagesRuntimeRes.Add(fileName);
                        else addedImagesRuntime.Add(fileName);
                    }
                }
            }

            newFeatureNameField.value = "";
            pendingItems.Clear();
            RefreshPendingList();
            RefreshFeatureList();
            RefreshOrganizerFeatureOptions();
            RefreshOrganizer();

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            string message = $"'{featureName}' 추가 완료!\n\n";
            if (addedScriptsEditor.Count > 0) message += $"✓ Editor Script: {addedScriptsEditor.Count}개 ({string.Join(", ", addedScriptsEditor)})\n";
            if (addedScriptsRuntime.Count > 0) message += $"✓ Runtime Script: {addedScriptsRuntime.Count}개 ({string.Join(", ", addedScriptsRuntime)})\n";
            if (addedImagesEditor.Count > 0) message += $"✓ Editor Image: {addedImagesEditor.Count}개 ({string.Join(", ", addedImagesEditor)})\n";
            if (addedImagesRuntime.Count > 0) message += $"✓ Runtime Image: {addedImagesRuntime.Count}개 ({string.Join(", ", addedImagesRuntime)})\n";
            if (addedImagesEditorRes.Count > 0) message += $"✓ Editor Resources: {addedImagesEditorRes.Count}개 ({string.Join(", ", addedImagesEditorRes)})\n";
            if (addedImagesRuntimeRes.Count > 0) message += $"✓ Runtime Resources: {addedImagesRuntimeRes.Count}개 ({string.Join(", ", addedImagesRuntimeRes)})\n";

            EditorUtility.DisplayDialog("완료", message, "OK");
        }

        private void RefreshOrganizerFeatureOptions()
        {
            if (organizerFeaturePopup == null) return;

            var features = GetAllFeatureNames();
            if (features.Count == 0) features.Add("-");

            string current = organizerFeaturePopup.value;
            organizerFeaturePopup.choices = features;

            int newIndex = features.IndexOf(current);
            if (newIndex < 0) newIndex = 0;

            organizerFeaturePopup.SetValueWithoutNotify(features[newIndex]);
        }

        private void RefreshOrganizer()
        {
            if (organizerScroll == null)
                return;

            organizerScroll.Clear();

            if (string.IsNullOrEmpty(currentPackagePath) || !Directory.Exists(currentPackagePath))
            {
                AddOrganizerEmpty("STEP 1을 먼저 완료하세요.");
                return;
            }

            var features = GetAllFeatureNames();
            if (features.Count == 0)
            {
                AddOrganizerEmpty("기능이 없습니다. STEP 2에서 먼저 추가하세요.");
                return;
            }

            string feature = organizerFeaturePopup != null ? organizerFeaturePopup.value : "-";
            if (string.IsNullOrEmpty(feature) || feature == "-")
            {
                AddOrganizerEmpty("정리할 기능을 선택하세요.");
                return;
            }

            var entries = ScanFeatureEntries(feature);
            if (entries.Count == 0)
            {
                AddOrganizerEmpty("이 기능 폴더에 파일이 없습니다.");
                return;
            }

            foreach (var entry in entries)
            {
                organizerScroll.Add(CreateOrganizerRow(feature, entry));
            }
        }

        private void AddOrganizerEmpty(string text)
        {
            var label = new Label(text);
            label.style.color = Color.gray;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginTop = 14;
            label.style.marginBottom = 14;
            organizerScroll.Add(label);
        }

        private VisualElement CreateOrganizerRow(string featureName, FeatureEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            var tag = new Label(entry.Tag);
            tag.style.width = 56;
            tag.style.unityTextAlign = TextAnchor.MiddleCenter;
            tag.style.color = entry.TagColor;
            row.Add(tag);

            var nameLabel = new Label(entry.FileName);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.marginRight = 8;
            row.Add(nameLabel);

            bool isScript = entry.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

            if (isScript)
            {
                row.Add(CreateMoveButton("Editor", () => MoveEntry(featureName, entry, FeatureLocation.EditorFeature)));
                row.Add(CreateMoveButton("Runtime", () => MoveEntry(featureName, entry, FeatureLocation.RuntimeFeature)));
            }
            else
            {
                row.Add(CreateMoveButton("E", () => MoveEntry(featureName, entry, FeatureLocation.EditorFeature)));
                row.Add(CreateMoveButton("R", () => MoveEntry(featureName, entry, FeatureLocation.RuntimeFeature)));
                row.Add(CreateMoveButton("E/Res", () => MoveEntry(featureName, entry, FeatureLocation.EditorResources)));
                row.Add(CreateMoveButton("R/Res", () => MoveEntry(featureName, entry, FeatureLocation.RuntimeResources)));
            }

            var delBtn = new Button(() => DeleteEntry(entry));
            delBtn.text = "✕";
            delBtn.style.width = 22;
            delBtn.style.height = 18;
            delBtn.style.marginLeft = 6;
            delBtn.style.backgroundColor = new Color(0.4f, 0.2f, 0.2f);
            row.Add(delBtn);

            return row;
        }

        private Button CreateMoveButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.width = text.Length >= 4 ? 56 : 40;
            btn.style.height = 18;
            btn.style.marginRight = 4;
            return btn;
        }

        private void DeleteEntry(FeatureEntry entry)
        {
            if (!EditorUtility.DisplayDialog("삭제 확인", $"{entry.FileName} 삭제할까요?", "삭제", "취소"))
                return;

            try
            {
                if (File.Exists(entry.FullPath))
                    File.Delete(entry.FullPath);

                string meta = entry.FullPath + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                RefreshFeatureList();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"삭제 실패:\n{e.Message}", "OK");
            }
        }

        private enum FeatureLocation
        {
            EditorFeature,
            RuntimeFeature,
            EditorResources,
            RuntimeResources
        }

        private class FeatureEntry
        {
            public string FileName;
            public string FullPath;
            public FeatureLocation Location;
            public string Tag;
            public Color TagColor;
        }

        private void MoveEntry(string featureName, FeatureEntry entry, FeatureLocation target)
        {
            if (entry.Location == target) return;

            try
            {
                EnsureEditorRootIfNeeded();
                EnsureRuntimeRootIfNeeded();

                string destDir = GetFeatureLocationPath(featureName, target);
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);

                    string parent = Directory.GetParent(destDir)?.FullName;
                    string folderName = Path.GetFileName(destDir);

                    if (!string.IsNullOrEmpty(parent))
                        CreateFolderMeta(parent, folderName);
                }

                string destPath = Path.Combine(destDir, entry.FileName);

                if (File.Exists(destPath))
                    File.Delete(destPath);

                File.Move(entry.FullPath, destPath);

                string srcMeta = entry.FullPath + ".meta";
                string dstMeta = destPath + ".meta";

                if (File.Exists(dstMeta))
                    File.Delete(dstMeta);

                if (File.Exists(srcMeta))
                {
                    File.Move(srcMeta, dstMeta);
                }
                else
                {
                    CreateMetaFromSourceOrDefault(destPath, destPath, entry.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? PendingKind.Script : PendingKind.Image);
                }

                if (target == FeatureLocation.RuntimeFeature || target == FeatureLocation.RuntimeResources)
                    EnsureRuntimeRootIfNeeded();

                if (target == FeatureLocation.EditorFeature || target == FeatureLocation.EditorResources)
                    EnsureEditorRootIfNeeded();

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                RefreshFeatureList();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"이동 실패:\n{e.Message}", "OK");
            }
        }

        private string GetFeatureLocationPath(string featureName, FeatureLocation location)
        {
            string editorRoot = Path.Combine(currentPackagePath, "Editor");
            string runtimeRoot = Path.Combine(currentPackagePath, "Runtime");

            switch (location)
            {
                case FeatureLocation.EditorFeature:
                    return Path.Combine(editorRoot, featureName);
                case FeatureLocation.RuntimeFeature:
                    return Path.Combine(runtimeRoot, featureName);
                case FeatureLocation.EditorResources:
                    return Path.Combine(editorRoot, "Resources", featureName);
                case FeatureLocation.RuntimeResources:
                    return Path.Combine(runtimeRoot, "Resources", featureName);
                default:
                    return Path.Combine(runtimeRoot, featureName);
            }
        }

        private List<FeatureEntry> ScanFeatureEntries(string featureName)
        {
            var results = new List<FeatureEntry>();

            string editorFeature = Path.Combine(currentPackagePath, "Editor", featureName);
            string runtimeFeature = Path.Combine(currentPackagePath, "Runtime", featureName);
            string editorRes = Path.Combine(currentPackagePath, "Editor", "Resources", featureName);
            string runtimeRes = Path.Combine(currentPackagePath, "Runtime", "Resources", featureName);

            AddEntriesFromDir(results, editorFeature, FeatureLocation.EditorFeature, "Editor", new Color(0.6f, 0.8f, 1f));
            AddEntriesFromDir(results, runtimeFeature, FeatureLocation.RuntimeFeature, "Runtime", new Color(0.6f, 1f, 0.6f));
            AddEntriesFromDir(results, editorRes, FeatureLocation.EditorResources, "E/Res", new Color(0.85f, 0.75f, 1f));
            AddEntriesFromDir(results, runtimeRes, FeatureLocation.RuntimeResources, "R/Res", new Color(1f, 0.85f, 0.6f));

            results = results
                .Where(r => !r.FileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Location.ToString())
                .ThenBy(r => r.FileName)
                .ToList();

            return results;
        }

        private void AddEntriesFromDir(List<FeatureEntry> list, string dir, FeatureLocation loc, string tag, Color tagColor)
        {
            if (!Directory.Exists(dir)) return;

            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var f in files)
            {
                list.Add(new FeatureEntry
                {
                    FileName = Path.GetFileName(f),
                    FullPath = f,
                    Location = loc,
                    Tag = tag,
                    TagColor = tagColor
                });
            }
        }

        private List<string> GetAllFeatureNames()
        {
            if (string.IsNullOrEmpty(currentPackagePath) || !Directory.Exists(currentPackagePath))
                return new List<string>();

            var set = new HashSet<string>();

            string editorPath = Path.Combine(currentPackagePath, "Editor");
            string runtimePath = Path.Combine(currentPackagePath, "Runtime");

            if (Directory.Exists(editorPath))
            {
                foreach (string dir in Directory.GetDirectories(editorPath))
                {
                    string name = Path.GetFileName(dir);
                    if (name == "Resources") continue;
                    set.Add(name);
                }

                string editorRes = Path.Combine(editorPath, "Resources");
                if (Directory.Exists(editorRes))
                {
                    foreach (string dir in Directory.GetDirectories(editorRes))
                    {
                        set.Add(Path.GetFileName(dir));
                    }
                }
            }

            if (Directory.Exists(runtimePath))
            {
                foreach (string dir in Directory.GetDirectories(runtimePath))
                {
                    string name = Path.GetFileName(dir);
                    if (name == "Resources") continue;
                    set.Add(name);
                }

                string runtimeRes = Path.Combine(runtimePath, "Resources");
                if (Directory.Exists(runtimeRes))
                {
                    foreach (string dir in Directory.GetDirectories(runtimeRes))
                    {
                        set.Add(Path.GetFileName(dir));
                    }
                }
            }

            return set.OrderBy(x => x).ToList();
        }

        private void ResetDropZoneStyle()
        {
            dropZone.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            dropZone.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        }

        private void CreateStep3(VisualElement root)
        {
            step3Container = CreateStepContainer(root, "STEP 3", "버전 업데이트 & 배포");

            var versionRow = new VisualElement();
            versionRow.style.flexDirection = FlexDirection.Row;
            versionRow.style.alignItems = Align.Center;
            versionRow.style.marginBottom = 10;

            var versionLabel = new Label("Version");
            versionLabel.style.width = 60;
            versionRow.Add(versionLabel);

            versionField = new TextField();
            versionField.value = "1.0.0";
            versionField.style.width = 90;
            versionField.style.marginRight = 15;
            versionField.style.height = 24;
            versionField.style.minHeight = 24;

            versionRow.Add(versionField);

            var patchBtn = new Button(() => BumpVersion("patch"));
            patchBtn.text = "+0.0.1";
            patchBtn.style.width = 64;
            patchBtn.style.marginRight = 5;
            versionRow.Add(patchBtn);

            var minorBtn = new Button(() => BumpVersion("minor"));
            minorBtn.text = "+0.1.0";
            minorBtn.style.width = 64;
            minorBtn.style.marginRight = 5;
            versionRow.Add(minorBtn);

            var majorBtn = new Button(() => BumpVersion("major"));
            majorBtn.text = "+1.0.0";
            majorBtn.style.width = 64;
            versionRow.Add(majorBtn);

            step3Container.Add(versionRow);

            var saveBtn = new Button(SavePackageJson);
            saveBtn.text = "💾 package.json 저장";
            saveBtn.style.height = 32;
            saveBtn.style.marginBottom = 10;
            step3Container.Add(saveBtn);

            var devModeSection = new VisualElement();
            devModeSection.style.marginTop = 15;
            devModeSection.style.paddingTop = 10;
            devModeSection.style.borderTopWidth = 1;
            devModeSection.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);

            var devModeLabel = new Label("현재 모드");
            devModeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            devModeLabel.style.marginBottom = 5;
            devModeSection.Add(devModeLabel);

            var devModeDesc = new Label("DEV MODE: file: 로컬 경로로 참조 (패키지 수정 가능)  /  DEPLOY MODE: git URL 참조 (일반적으로 읽기 전용)");
            devModeDesc.style.color = Color.gray;
            devModeDesc.style.fontSize = 11;
            devModeDesc.style.marginBottom = 8;
            devModeSection.Add(devModeDesc);

            devModeButton = new Button(ToggleDevMode);
            devModeButton.text = "개발 모드 활성화";
            devModeButton.style.height = 28;
            devModeSection.Add(devModeButton);

            devModeStatus = new Label("");
            devModeStatus.style.marginTop = 5;
            devModeStatus.style.unityTextAlign = TextAnchor.MiddleCenter;
            devModeStatus.style.fontSize = 11;
            devModeSection.Add(devModeStatus);

            step3Container.Add(devModeSection);
        }

        private VisualElement CreateStepContainer(VisualElement parent, string stepNum, string stepTitle)
        {
            var container = new VisualElement();
            container.style.marginBottom = 14;
            container.style.paddingTop = 0;
            container.style.paddingBottom = 12;
            container.style.paddingLeft = 12;
            container.style.paddingRight = 12;
            container.style.backgroundColor = new Color(0.20f, 0.20f, 0.20f);
            container.style.borderTopLeftRadius = 12;
            container.style.borderTopRightRadius = 12;
            container.style.borderBottomLeftRadius = 12;
            container.style.borderBottomRightRadius = 12;
            container.style.borderTopWidth = 1;
            container.style.borderBottomWidth = 1;
            container.style.borderLeftWidth = 1;
            container.style.borderRightWidth = 1;
            container.style.borderTopColor = new Color(0.30f, 0.30f, 0.30f);
            container.style.borderBottomColor = new Color(0.30f, 0.30f, 0.30f);
            container.style.borderLeftColor = new Color(0.30f, 0.30f, 0.30f);
            container.style.borderRightColor = new Color(0.30f, 0.30f, 0.30f);

            var headerBar = new VisualElement();
            headerBar.style.flexDirection = FlexDirection.Row;
            headerBar.style.alignItems = Align.Center;
            headerBar.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            headerBar.style.marginLeft = -12;
            headerBar.style.marginRight = -12;
            headerBar.style.paddingLeft = 12;
            headerBar.style.paddingRight = 12;
            headerBar.style.paddingTop = 10;
            headerBar.style.paddingBottom = 10;
            headerBar.style.borderTopLeftRadius = 12;
            headerBar.style.borderTopRightRadius = 12;
            headerBar.style.borderBottomWidth = 1;
            headerBar.style.borderBottomColor = new Color(0.26f, 0.26f, 0.26f);
            container.Add(headerBar);

            var badge = new Label(stepNum);
            badge.style.fontSize = 11;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = Color.white;
            badge.style.backgroundColor = new Color(0.24f, 0.40f, 0.70f);
            badge.style.paddingLeft = 8;
            badge.style.paddingRight = 8;
            badge.style.paddingTop = 3;
            badge.style.paddingBottom = 3;
            badge.style.borderTopLeftRadius = 10;
            badge.style.borderTopRightRadius = 10;
            badge.style.borderBottomLeftRadius = 10;
            badge.style.borderBottomRightRadius = 10;
            badge.style.marginRight = 10;
            headerBar.Add(badge);

            var titleLabel = new Label(stepTitle);
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.92f, 0.92f, 0.92f);
            headerBar.Add(titleLabel);

            var body = new VisualElement();
            body.style.paddingTop = 12;
            container.Add(body);

            parent.Add(container);
            return body;
        }


        private TextField CreateTextField(VisualElement parent, string label, string defaultValue)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;
            row.style.height = 28;

            var lbl = new Label(label);
            lbl.style.width = 130;
            row.Add(lbl);

            var field = new TextField();
            field.value = defaultValue;
            field.style.flexGrow = 1;
            field.style.height = 24;
            field.style.minHeight = 24;
            row.Add(field);

            parent.Add(row);
            return field;
        }


        private void UpdateStepStates()
        {
            bool hasPackage = !string.IsNullOrEmpty(currentPackagePath) && Directory.Exists(currentPackagePath);

            step2Container.SetEnabled(hasPackage);
            step3Container.SetEnabled(hasPackage);

            if (hasPackage)
            {
                step2Status.text = "";
                step2Status.style.display = DisplayStyle.None;
            }
            else
            {
                step2Status.text = "← STEP 1을 먼저 완료하세요";
                step2Status.style.display = DisplayStyle.Flex;
            }
        }

        private void TryLoadLastPackage()
        {
            string lastPath = EditorPrefs.GetString(PREF_LAST_PATH, "");
            if (!string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath))
            {
                LoadPackageFromPath(lastPath);
            }
        }

        private void LoadExistingPackage()
        {
            string folderPath = EditorUtility.OpenFolderPanel("Select Package Folder", "", "");
            if (string.IsNullOrEmpty(folderPath)) return;

            string packageJsonPath = Path.Combine(folderPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                EditorUtility.DisplayDialog("Error", "package.json이 없는 폴더입니다.", "OK");
                return;
            }

            LoadPackageFromPath(folderPath);
        }

        private void LoadPackageFromPath(string folderPath)
        {
            string packageJsonPath = Path.Combine(folderPath, "package.json");
            if (!File.Exists(packageJsonPath)) return;

            try
            {
                string json = File.ReadAllText(packageJsonPath);
                var packageInfo = JsonUtility.FromJson<PackageJson>(json);

                packageNameField.value = packageInfo.name ?? "com.teller.util";
                displayNameField.value = packageInfo.displayName ?? "TelleR Utilities";
                versionField.value = packageInfo.version ?? "1.0.0";
                descriptionField.value = packageInfo.description ?? "";
                unityVersionField.value = packageInfo.unity ?? "2021.3";
                if (packageInfo.author != null)
                    authorField.value = packageInfo.author.name ?? "TelleR";

                currentPackagePath = folderPath;
                EditorPrefs.SetString(PREF_LAST_PATH, folderPath);

                step1Status.text = $"✓ 로드됨: {Path.GetFileName(folderPath)}";
                step1Status.style.color = new Color(0.4f, 0.9f, 0.4f);

                UpdateStepStates();
                RefreshFeatureList();
                UpdateDevModeUI();
                EnsureMetaFiles(folderPath);
                RefreshOrganizerFeatureOptions();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"불러오기 실패:\n{e.Message}", "OK");
            }
        }

        private void CreateNewPackage()
        {
            string folderPath = EditorUtility.SaveFolderPanel("패키지를 저장할 폴더 선택", "", "");
            if (string.IsNullOrEmpty(folderPath)) return;

            string packageName = packageNameField.value;
            string packageRoot = Path.Combine(folderPath, packageName);

            if (Directory.Exists(packageRoot))
            {
                if (EditorUtility.DisplayDialog("폴더 존재", $"'{packageName}' 폴더가 이미 있습니다. 불러올까요?", "불러오기", "취소"))
                {
                    LoadPackageFromPath(packageRoot);
                }
                return;
            }

            Directory.CreateDirectory(packageRoot);

            currentPackagePath = packageRoot;
            SavePackageJson();
            CreateReadme(packageRoot);
            CreateLicense(packageRoot);
            CreateGitIgnore(packageRoot);

            EditorPrefs.SetString(PREF_LAST_PATH, packageRoot);

            step1Status.text = $"✓ 생성됨: {packageName}";
            step1Status.style.color = new Color(0.4f, 0.9f, 0.4f);

            UpdateStepStates();
            RefreshFeatureList();
            RefreshOrganizerFeatureOptions();
            RefreshOrganizer();

            EditorUtility.DisplayDialog("완료", "패키지가 생성되었습니다!\n\nSTEP 2에서 기능을 추가하세요.", "OK");
        }

        private void OpenPackageFolder()
        {
            if (string.IsNullOrEmpty(currentPackagePath))
            {
                EditorUtility.DisplayDialog("Error", "먼저 STEP 1을 완료하세요.", "OK");
                return;
            }

            EditorUtility.RevealInFinder(currentPackagePath);
        }

        private void RefreshFeatureList()
        {
            featureListContainer.Clear();

            if (string.IsNullOrEmpty(currentPackagePath)) return;

            string editorPath = Path.Combine(currentPackagePath, "Editor");
            string runtimePath = Path.Combine(currentPackagePath, "Runtime");
            string editorResPath = Path.Combine(editorPath, "Resources");
            string runtimeResPath = Path.Combine(runtimePath, "Resources");

            var features = new Dictionary<string, (bool hasEditor, bool hasRuntime, bool hasEditorRes, bool hasRuntimeRes, int editorFiles, int runtimeFiles, int editorResFiles, int runtimeResFiles)>();

            if (Directory.Exists(editorPath))
            {
                foreach (string dir in Directory.GetDirectories(editorPath))
                {
                    string name = Path.GetFileName(dir);
                    if (name == "Resources") continue;

                    int files = CountNonMetaFiles(dir);
                    features[name] = (true, false, false, false, files, 0, 0, 0);
                }
            }

            if (Directory.Exists(runtimePath))
            {
                foreach (string dir in Directory.GetDirectories(runtimePath))
                {
                    string name = Path.GetFileName(dir);
                    if (name == "Resources") continue;

                    int files = CountNonMetaFiles(dir);
                    if (features.ContainsKey(name))
                    {
                        var existing = features[name];
                        features[name] = (existing.hasEditor, true, existing.hasEditorRes, existing.hasRuntimeRes, existing.editorFiles, files, existing.editorResFiles, existing.runtimeResFiles);
                    }
                    else
                    {
                        features[name] = (false, true, false, false, 0, files, 0, 0);
                    }
                }
            }

            if (Directory.Exists(editorResPath))
            {
                foreach (string dir in Directory.GetDirectories(editorResPath))
                {
                    string name = Path.GetFileName(dir);
                    int files = CountNonMetaFiles(dir);

                    if (features.ContainsKey(name))
                    {
                        var existing = features[name];
                        features[name] = (existing.hasEditor, existing.hasRuntime, true, existing.hasRuntimeRes, existing.editorFiles, existing.runtimeFiles, files, existing.runtimeResFiles);
                    }
                    else
                    {
                        features[name] = (false, false, true, false, 0, 0, files, 0);
                    }
                }
            }

            if (Directory.Exists(runtimeResPath))
            {
                foreach (string dir in Directory.GetDirectories(runtimeResPath))
                {
                    string name = Path.GetFileName(dir);
                    int files = CountNonMetaFiles(dir);

                    if (features.ContainsKey(name))
                    {
                        var existing = features[name];
                        features[name] = (existing.hasEditor, existing.hasRuntime, existing.hasEditorRes, true, existing.editorFiles, existing.runtimeFiles, existing.editorResFiles, files);
                    }
                    else
                    {
                        features[name] = (false, false, false, true, 0, 0, 0, files);
                    }
                }
            }

            if (features.Count == 0)
            {
                AddEmptyLabel("기능 이름 입력 후 스크립트/이미지를 드래그하세요");
                return;
            }

            foreach (var kvp in features.OrderBy(k => k.Key))
            {
                string featureName = kvp.Key;
                var info = kvp.Value;

                var row = new VisualElement();
                row.style.width = new Length(48, LengthUnit.Percent);
                row.style.marginRight = 10;
                row.style.marginBottom = 10;
                row.style.paddingTop = 10;
                row.style.paddingBottom = 10;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                row.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
                row.style.borderTopLeftRadius = 10;
                row.style.borderTopRightRadius = 10;
                row.style.borderBottomLeftRadius = 10;
                row.style.borderBottomRightRadius = 10;
                row.style.borderTopWidth = 1;
                row.style.borderBottomWidth = 1;
                row.style.borderLeftWidth = 1;
                row.style.borderRightWidth = 1;
                row.style.borderTopColor = new Color(0.30f, 0.30f, 0.30f);
                row.style.borderBottomColor = new Color(0.30f, 0.30f, 0.30f);
                row.style.borderLeftColor = new Color(0.30f, 0.30f, 0.30f);
                row.style.borderRightColor = new Color(0.30f, 0.30f, 0.30f);

                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.NoWrap;
                row.style.alignItems = Align.Center;
                row.style.width = new Length(48, LengthUnit.Percent);
                row.style.minHeight = 46;
                row.style.marginRight = 10;
                row.style.marginBottom = 10;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                row.style.paddingTop = 8;
                row.style.paddingBottom = 8;
                row.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
                row.style.borderTopLeftRadius = 10;
                row.style.borderTopRightRadius = 10;
                row.style.borderBottomLeftRadius = 10;
                row.style.borderBottomRightRadius = 10;
                row.style.borderTopWidth = 1;
                row.style.borderBottomWidth = 1;
                row.style.borderLeftWidth = 1;
                row.style.borderRightWidth = 1;
                row.style.borderTopColor = new Color(0.26f, 0.26f, 0.26f);
                row.style.borderBottomColor = new Color(0.26f, 0.26f, 0.26f);
                row.style.borderLeftColor = new Color(0.26f, 0.26f, 0.26f);
                row.style.borderRightColor = new Color(0.26f, 0.26f, 0.26f);

                var icon = new Label("📁");
                icon.style.marginRight = 8;
                row.Add(icon);

                var label = new Label(featureName);
                label.style.flexGrow = 1;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(label);

                string typeText = "";
                if (info.hasEditor) typeText += $"E:{info.editorFiles} ";
                if (info.hasRuntime) typeText += $"R:{info.runtimeFiles} ";
                if (info.hasEditorRes) typeText += $"E/Res:{info.editorResFiles} ";
                if (info.hasRuntimeRes) typeText += $"R/Res:{info.runtimeResFiles} ";

                typeText = typeText.Trim();

                var countLabel = new Label(typeText);
                countLabel.style.color = Color.gray;
                countLabel.style.marginRight = 10;
                row.Add(countLabel);

                var focusBtn = new Button(() => FocusOrganizerFeature(featureName));
                focusBtn.text = "정리";
                focusBtn.style.width = 44;
                focusBtn.style.height = 20;
                focusBtn.style.marginRight = 6;
                row.Add(focusBtn);

                var deleteBtn = new Button(() => DeleteFeature(featureName));
                deleteBtn.text = "✕";
                deleteBtn.style.width = 24;
                deleteBtn.style.height = 20;
                deleteBtn.style.backgroundColor = new Color(0.5f, 0.2f, 0.2f);
                row.Add(deleteBtn);

                featureListContainer.Add(row);
            }
        }

        private int CountNonMetaFiles(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
        }

        private void FocusOrganizerFeature(string featureName)
        {
            if (organizerFeaturePopup == null) return;

            RefreshOrganizerFeatureOptions();

            int idx = organizerFeaturePopup.choices.IndexOf(featureName);
            if (idx < 0) return;

            organizerFeaturePopup.SetValueWithoutNotify(featureName);
            RefreshOrganizer();
        }

        private void AddEmptyLabel(string text)
        {
            var label = new Label(text);
            label.style.color = Color.gray;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginTop = 10;
            label.style.marginBottom = 10;
            featureListContainer.Add(label);
        }

        private void DeleteFeature(string featureName)
        {
            if (!EditorUtility.DisplayDialog("삭제 확인", $"'{featureName}' 기능을 삭제할까요?", "삭제", "취소"))
                return;

            try
            {
                DeleteDirIfExists(Path.Combine(currentPackagePath, "Editor", featureName));
                DeleteDirIfExists(Path.Combine(currentPackagePath, "Runtime", featureName));
                DeleteDirIfExists(Path.Combine(currentPackagePath, "Editor", "Resources", featureName));
                DeleteDirIfExists(Path.Combine(currentPackagePath, "Runtime", "Resources", featureName));

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                RefreshFeatureList();
                RefreshOrganizerFeatureOptions();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"삭제 실패:\n{e.Message}", "OK");
            }
        }

        private void DeleteDirIfExists(string dir)
        {
            if (!Directory.Exists(dir)) return;

            Directory.Delete(dir, true);

            string metaPath = dir + ".meta";
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }

        private void BumpVersion(string type)
        {
            string[] parts = versionField.value.Split('.');
            if (parts.Length != 3) parts = new[] { "1", "0", "0" };

            int.TryParse(parts[0], out int major);
            int.TryParse(parts[1], out int minor);
            int.TryParse(parts[2], out int patch);

            switch (type)
            {
                case "major":
                    major++;
                    minor = 0;
                    patch = 0;
                    break;
                case "minor":
                    minor++;
                    patch = 0;
                    break;
                case "patch":
                    patch++;
                    break;
            }

            versionField.value = $"{major}.{minor}.{patch}";
        }

        private void ToggleDevMode()
        {
            if (string.IsNullOrEmpty(currentPackagePath))
            {
                EditorUtility.DisplayDialog("Error", "먼저 패키지를 불러오세요.", "OK");
                return;
            }

            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                EditorUtility.DisplayDialog("Error", "manifest.json을 찾을 수 없습니다.", "OK");
                return;
            }

            string packageName = packageNameField.value;
            string manifestContent = File.ReadAllText(manifestPath);

            bool isDevMode = IsDevModeEnabled(manifestContent, packageName);

            if (isDevMode)
            {
                string gitUrl = EditorPrefs.GetString($"UPMCreator_GitUrl_{packageName}", "");
                if (string.IsNullOrEmpty(gitUrl))
                {
                    gitUrl = $"https://github.com/USERNAME/{packageName}.git";
                    int result = EditorUtility.DisplayDialogComplex(
                        "Git URL 입력",
                        "배포 모드로 전환하려면 Git URL이 필요합니다.\n기본값을 사용하시겠습니까?",
                        "기본값 사용",
                        "취소",
                        "");

                    if (result != 0) return;
                }

                manifestContent = SetPackageSource(manifestContent, packageName, gitUrl);
                File.WriteAllText(manifestPath, manifestContent);

                devModeStatus.text = "✓ 배포 모드로 전환됨 (읽기 전용)";
                devModeStatus.style.color = Color.gray;
                devModeButton.text = "개발 모드 활성화";
            }
            else
            {
                string localPath = $"file:{currentPackagePath.Replace("\\", "/")}";

                string currentUrl = GetCurrentPackageSource(manifestContent, packageName);
                if (!string.IsNullOrEmpty(currentUrl) && currentUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    EditorPrefs.SetString($"UPMCreator_GitUrl_{packageName}", currentUrl);
                }

                manifestContent = SetPackageSource(manifestContent, packageName, localPath);
                File.WriteAllText(manifestPath, manifestContent);

                devModeStatus.text = "✓ 개발 모드 활성화 (수정 가능)";
                devModeStatus.style.color = new Color(0.4f, 0.9f, 0.4f);
                devModeButton.text = "배포 모드로 전환";
            }

            EditorApplication.delayCall += () => { AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); };
        }

        private bool IsDevModeEnabled(string manifestContent, string packageName)
        {
            string source = GetCurrentPackageSource(manifestContent, packageName);
            return !string.IsNullOrEmpty(source) && source.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        }

        private string GetCurrentPackageSource(string manifestContent, string packageName)
        {
            string pattern = $"\"{packageName}\": \"";
            int startIndex = manifestContent.IndexOf(pattern, StringComparison.Ordinal);
            if (startIndex == -1) return null;

            startIndex += pattern.Length;
            int endIndex = manifestContent.IndexOf("\"", startIndex, StringComparison.Ordinal);
            if (endIndex == -1) return null;

            return manifestContent.Substring(startIndex, endIndex - startIndex);
        }

        private string SetPackageSource(string manifestContent, string packageName, string newSource)
        {
            string pattern = $"\"{packageName}\": \"";
            int startIndex = manifestContent.IndexOf(pattern, StringComparison.Ordinal);

            if (startIndex == -1)
            {
                int dependenciesIndex = manifestContent.IndexOf("\"dependencies\"", StringComparison.Ordinal);
                if (dependenciesIndex == -1) return manifestContent;

                int braceIndex = manifestContent.IndexOf("{", dependenciesIndex, StringComparison.Ordinal);
                if (braceIndex == -1) return manifestContent;

                string newEntry = $"\n    \"{packageName}\": \"{newSource}\",";
                return manifestContent.Insert(braceIndex + 1, newEntry);
            }

            startIndex += pattern.Length;
            int endIndex = manifestContent.IndexOf("\"", startIndex, StringComparison.Ordinal);
            if (endIndex == -1) return manifestContent;

            return manifestContent.Substring(0, startIndex) + newSource + manifestContent.Substring(endIndex);
        }

        private void UpdateDevModeUI()
        {
            if (string.IsNullOrEmpty(currentPackagePath) || devModeButton == null || devModeStatus == null) return;

            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                devModeStatus.text = "현재 모드 : UNKNOWN\nmanifest.json을 찾을 수 없습니다.";
                devModeStatus.style.color = Color.gray;
                devModeButton.text = "개발 모드 활성화";
                return;
            }

            string packageName = packageNameField.value;
            string manifestContent = File.ReadAllText(manifestPath);
            string source = GetCurrentPackageSource(manifestContent, packageName);
            bool isDevMode = !string.IsNullOrEmpty(source) && source.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

            if (isDevMode)
            {
                devModeStatus.text = $"현재 모드 : DEV MODE (패키지 수정 가능)\nmanifest: {source}";
                devModeStatus.style.color = new Color(0.45f, 0.95f, 0.45f);
                devModeButton.text = "배포 모드로 전환";
            }
            else
            {
                devModeStatus.text = $"현재 모드 : DEPLOY MODE (일반적으로 읽기 전용)\nmanifest: {(string.IsNullOrEmpty(source) ? "(not found)" : source)}";
                devModeStatus.style.color = Color.gray;
                devModeButton.text = "개발 모드로 전환";
            }
        }

        private void EnsureMetaFiles(string packagePath)
        {
            string[] filesToCheck = { "README.md", "LICENSE.md", "package.json", "CHANGELOG.md" };

            foreach (string fileName in filesToCheck)
            {
                string filePath = Path.Combine(packagePath, fileName);
                string metaPath = filePath + ".meta";

                if (File.Exists(filePath) && !File.Exists(metaPath))
                {
                    CreateFileMeta(filePath);
                }
            }

            string[] foldersToCheck = { "Editor", "Runtime" };

            foreach (string folderName in foldersToCheck)
            {
                string folderPath = Path.Combine(packagePath, folderName);
                string metaPath = folderPath + ".meta";

                if (Directory.Exists(folderPath) && !File.Exists(metaPath))
                {
                    CreateFolderMeta(packagePath, folderName);
                }
            }
        }

        private void SavePackageJson()
        {
            if (string.IsNullOrEmpty(currentPackagePath))
            {
                EditorUtility.DisplayDialog("Error", "먼저 STEP 1을 완료하세요.", "OK");
                return;
            }

            string json = $@"{{
  ""name"": ""{packageNameField.value}"",
  ""version"": ""{versionField.value}"",
  ""displayName"": ""{displayNameField.value}"",
  ""description"": ""{descriptionField.value}"",
  ""unity"": ""{unityVersionField.value}"",
  ""author"": {{
    ""name"": ""{authorField.value}""
  }},
  ""keywords"": []
}}";
            string path = Path.Combine(currentPackagePath, "package.json");
            File.WriteAllText(path, json);

            if (!File.Exists(path + ".meta")) CreateFileMeta(path);

            EditorUtility.DisplayDialog("저장 완료", $"v{versionField.value} 저장됨\n\nGitHub Desktop에서 Commit & Push", "OK");
        }

        private void CreateReadme(string root)
        {
            string path = Path.Combine(root, "README.md");
            string content = $"# {displayNameField.value}\n\n{descriptionField.value}\n\n## Installation\n\nPackage Manager → Add package from git URL\n\n## License\n\nMIT License\n";
            File.WriteAllText(path, content);
            CreateFileMeta(path);
        }

        private void CreateLicense(string root)
        {
            string path = Path.Combine(root, "LICENSE.md");
            string content = $"MIT License\n\nCopyright (c) {DateTime.Now.Year} {authorField.value}\n\nPermission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\nThe above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\nTHE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.\n";
            File.WriteAllText(path, content);
            CreateFileMeta(path);
        }

        private void CreateGitIgnore(string root)
        {
            File.WriteAllText(Path.Combine(root, ".gitignore"), "*.meta.bak\n.idea/\n.vs/\n.DS_Store\nThumbs.db\n");
        }

        private void EnsureEditorRootIfNeeded()
        {
            string editorPath = Path.Combine(currentPackagePath, "Editor");
            if (!Directory.Exists(editorPath))
            {
                Directory.CreateDirectory(editorPath);
                CreateFolderMeta(currentPackagePath, "Editor");
                CreateEditorAsmdef(editorPath);
            }
            else
            {
                CreateEditorAsmdef(editorPath);
            }
        }

        private void EnsureRuntimeRootIfNeeded()
        {
            string runtimePath = Path.Combine(currentPackagePath, "Runtime");
            if (!Directory.Exists(runtimePath))
            {
                Directory.CreateDirectory(runtimePath);
                CreateFolderMeta(currentPackagePath, "Runtime");
                CreateRuntimeAsmdef(runtimePath);

                string editorPath = Path.Combine(currentPackagePath, "Editor");
                if (Directory.Exists(editorPath))
                    UpdateEditorAsmdefWithRuntimeReference(editorPath);
            }
            else
            {
                CreateRuntimeAsmdef(runtimePath);

                string editorPath = Path.Combine(currentPackagePath, "Editor");
                if (Directory.Exists(editorPath))
                    UpdateEditorAsmdefWithRuntimeReference(editorPath);
            }
        }

        private void CreateEditorAsmdef(string editorPath)
        {
            string asmName = ConvertToAsmdefName(packageNameField.value) + ".Editor";
            string path = Path.Combine(editorPath, asmName + ".asmdef");
            if (File.Exists(path)) return;

            string runtimePath = Path.Combine(currentPackagePath, "Runtime");
            bool hasRuntime = Directory.Exists(runtimePath);
            string references = hasRuntime ? $"\"{ConvertToAsmdefName(packageNameField.value)}\"" : "";

            string content = $"{{\n  \"name\": \"{asmName}\",\n  \"rootNamespace\": \"{ConvertToNamespace(packageNameField.value)}\",\n  \"includePlatforms\": [\"Editor\"],\n  \"excludePlatforms\": [],\n  \"references\": [{references}]\n}}";
            File.WriteAllText(path, content);
            CreateAsmdefMeta(path);
        }

        private void CreateRuntimeAsmdef(string runtimePath)
        {
            string asmName = ConvertToAsmdefName(packageNameField.value);
            string path = Path.Combine(runtimePath, asmName + ".asmdef");
            if (File.Exists(path)) return;

            string content = $"{{\n  \"name\": \"{asmName}\",\n  \"rootNamespace\": \"{ConvertToNamespace(packageNameField.value)}\",\n  \"includePlatforms\": [],\n  \"excludePlatforms\": [],\n  \"references\": []\n}}";
            File.WriteAllText(path, content);
            CreateAsmdefMeta(path);
        }

        private void UpdateEditorAsmdefWithRuntimeReference(string editorPath)
        {
            string asmName = ConvertToAsmdefName(packageNameField.value) + ".Editor";
            string path = Path.Combine(editorPath, asmName + ".asmdef");
            if (!File.Exists(path)) return;

            string runtimeAsmName = ConvertToAsmdefName(packageNameField.value);
            string content = $"{{\n  \"name\": \"{asmName}\",\n  \"rootNamespace\": \"{ConvertToNamespace(packageNameField.value)}\",\n  \"includePlatforms\": [\"Editor\"],\n  \"excludePlatforms\": [],\n  \"references\": [\"{runtimeAsmName}\"]\n}}";
            File.WriteAllText(path, content);

            string meta = path + ".meta";
            if (!File.Exists(meta)) CreateAsmdefMeta(path);
        }

        private string ConvertToAsmdefName(string packageName)
        {
            string[] parts = packageName.Split('.');
            string result = "";
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                if (i > 1) result += ".";
                result += char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return string.IsNullOrEmpty(result) ? "Package" : result;
        }

        private string ConvertToNamespace(string packageName)
        {
            return ConvertToAsmdefName(packageName);
        }

        private void CreateMetaFromSourceOrDefault(string sourceFullPath, string destFilePath, PendingKind kind)
        {
            string sourceMeta = sourceFullPath + ".meta";
            string destMeta = destFilePath + ".meta";

            if (File.Exists(sourceMeta))
            {
                string meta = File.ReadAllText(sourceMeta);
                meta = ReplaceGuid(meta, GUID.Generate().ToString().Replace("-", ""));
                File.WriteAllText(destMeta, meta);
                return;
            }

            if (kind == PendingKind.Script) CreateScriptMeta(destFilePath);
            else CreateFileMeta(destFilePath);
        }

        private string ReplaceGuid(string metaContent, string newGuid)
        {
            using (var reader = new StringReader(metaContent))
            {
                var lines = new List<string>();
                string line;
                bool replaced = false;

                while ((line = reader.ReadLine()) != null)
                {
                    if (!replaced && line.StartsWith("guid:", StringComparison.Ordinal))
                    {
                        lines.Add("guid: " + newGuid);
                        replaced = true;
                    }
                    else
                    {
                        lines.Add(line);
                    }
                }

                if (!replaced)
                {
                    lines.Insert(1, "guid: " + newGuid);
                }

                return string.Join("\n", lines) + "\n";
            }
        }

        private void CreateFileMeta(string filePath)
        {
            string guid = GUID.Generate().ToString().Replace("-", "");
            File.WriteAllText(filePath + ".meta", $"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        }

        private void CreateScriptMeta(string filePath)
        {
            string guid = GUID.Generate().ToString().Replace("-", "");
            File.WriteAllText(filePath + ".meta", $"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        }

        private void CreateAsmdefMeta(string filePath)
        {
            string guid = GUID.Generate().ToString().Replace("-", "");
            File.WriteAllText(filePath + ".meta", $"fileFormatVersion: 2\nguid: {guid}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        }

        private void CreateFolderMeta(string parentPath, string folderName)
        {
            string guid = GUID.Generate().ToString().Replace("-", "");
            File.WriteAllText(Path.Combine(parentPath, folderName + ".meta"), $"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        }

        private bool IsEditorScript(string content)
        {
            if (content.Contains("using UnityEditor")) return true;
            if (content.Contains("[CustomEditor")) return true;
            if (content.Contains("[MenuItem")) return true;
            if (content.Contains(": EditorWindow")) return true;
            if (content.Contains(": Editor")) return true;
            if (content.Contains("EditorGUILayout")) return true;
            if (content.Contains("EditorUtility")) return true;
            if (content.Contains("[InitializeOnLoad]")) return true;
            if (content.Contains("SceneView")) return true;
            if (content.Contains("Handles.")) return true;
            return false;
        }

        private bool IsImageAsset(string assetPath)
        {
            string ext = Path.GetExtension(assetPath)?.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd" || ext == ".gif" || ext == ".bmp" || ext == ".hdr" || ext == ".exr" || ext == ".webp";
        }

        private string SafeReadAllText(string fullPath)
        {
            try
            {
                return File.ReadAllText(fullPath);
            }
            catch
            {
                return "";
            }
        }

        private string NormalizeFeatureName(string value)
        {
            if (value == null) return "";
            return value.Trim().Replace(" ", "");
        }

        [Serializable]
        private class PackageJson
        {
            public string name;
            public string version;
            public string displayName;
            public string description;
            public string unity;
            public Author author;
        }

        [Serializable]
        private class Author
        {
            public string name;
        }
    }
}
