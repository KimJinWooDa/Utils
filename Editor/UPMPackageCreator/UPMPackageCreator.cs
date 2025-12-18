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

        private string currentPackagePath;
        private List<string> pendingScripts = new List<string>();

        [MenuItem("Tools/TelleR/UPM Package Creator")]
        public static void ShowWindow()
        {
            var window = GetWindow<UPMPackageCreator>();
            window.titleContent = new GUIContent("UPM Package Creator");
            window.minSize = new Vector2(480, 720);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingTop = 10;
            root.style.paddingLeft = 15;
            root.style.paddingRight = 15;
            root.style.paddingBottom = 10;

            var title = new Label("UPM Package Creator");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 15;
            title.style.alignSelf = Align.Center;
            root.Add(title);

            CreateStep1(root);
            CreateStep2(root);
            CreateStep3(root);

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

        private void CreateStep2(VisualElement root)
        {
            step2Container = CreateStepContainer(root, "STEP 2", "기능 추가하기");

            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.marginBottom = 10;
            addRow.style.alignItems = Align.Center;

            var featureLabel = new Label("기능 이름:");
            featureLabel.style.marginRight = 5;
            addRow.Add(featureLabel);

            newFeatureNameField = new TextField();
            newFeatureNameField.value = "";
            newFeatureNameField.style.flexGrow = 1;
            addRow.Add(newFeatureNameField);

            step2Container.Add(addRow);

            var featureScroll = new ScrollView();
            featureScroll.style.maxHeight = 80;
            featureScroll.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            featureScroll.style.borderTopLeftRadius = 5;
            featureScroll.style.borderTopRightRadius = 5;
            featureScroll.style.borderBottomLeftRadius = 5;
            featureScroll.style.borderBottomRightRadius = 5;
            featureScroll.style.paddingTop = 8;
            featureScroll.style.paddingBottom = 8;
            featureScroll.style.paddingLeft = 10;
            featureScroll.style.paddingRight = 10;

            featureListContainer = new VisualElement();
            featureScroll.Add(featureListContainer);
            step2Container.Add(featureScroll);

            step2Status = new Label("← STEP 1을 먼저 완료하세요");
            step2Status.style.marginTop = 8;
            step2Status.style.color = Color.gray;
            step2Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            step2Container.Add(step2Status);

            dropZone = new VisualElement();
            dropZone.style.marginTop = 10;
            dropZone.style.height = 100;
            dropZone.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            dropZone.style.borderTopLeftRadius = 8;
            dropZone.style.borderTopRightRadius = 8;
            dropZone.style.borderBottomLeftRadius = 8;
            dropZone.style.borderBottomRightRadius = 8;
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

            var dropLabel = new Label("📁 여기에 .cs 파일을 드래그하세요\n\nEditor/Runtime 자동 분류 + meta 자동 생성");
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            dropZone.Add(dropLabel);

            dropZone.RegisterCallback<DragEnterEvent>(OnDragEnter);
            dropZone.RegisterCallback<DragLeaveEvent>(OnDragLeave);
            dropZone.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            dropZone.RegisterCallback<DragPerformEvent>(OnDragPerform);

            step2Container.Add(dropZone);

            pendingListContainer = new VisualElement();
            pendingListContainer.style.marginTop = 8;
            pendingListContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            pendingListContainer.style.borderTopLeftRadius = 5;
            pendingListContainer.style.borderTopRightRadius = 5;
            pendingListContainer.style.borderBottomLeftRadius = 5;
            pendingListContainer.style.borderBottomRightRadius = 5;
            pendingListContainer.style.paddingTop = 8;
            pendingListContainer.style.paddingBottom = 8;
            pendingListContainer.style.paddingLeft = 10;
            pendingListContainer.style.paddingRight = 10;
            pendingListContainer.style.display = DisplayStyle.None;
            step2Container.Add(pendingListContainer);

            confirmButton = new Button(ConfirmAddScripts);
            confirmButton.text = "✓ 추가 확정";
            confirmButton.style.height = 32;
            confirmButton.style.marginTop = 8;
            confirmButton.style.backgroundColor = new Color(0.2f, 0.5f, 0.3f);
            confirmButton.style.display = DisplayStyle.None;
            step2Container.Add(confirmButton);

            var openFolderBtn = new Button(OpenPackageFolder);
            openFolderBtn.text = "📁 패키지 폴더 열기";
            openFolderBtn.style.height = 28;
            openFolderBtn.style.marginTop = 8;
            step2Container.Add(openFolderBtn);
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

            string featureName = newFeatureNameField.value.Trim().Replace(" ", "");
            if (string.IsNullOrEmpty(featureName))
            {
                EditorUtility.DisplayDialog("Error", "기능 이름을 먼저 입력하세요.\n(예: AudioVolume3D)", "OK");
                return;
            }

            var scripts = DragAndDrop.objectReferences
                .OfType<MonoScript>()
                .ToList();

            if (scripts.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", ".cs 파일만 드래그하세요.", "OK");
                return;
            }

            foreach (var script in scripts)
            {
                string sourcePath = AssetDatabase.GetAssetPath(script);
                string sourceFullPath = Path.GetFullPath(sourcePath);
                
                if (!pendingScripts.Contains(sourceFullPath))
                    pendingScripts.Add(sourceFullPath);
            }

            RefreshPendingList();
        }

        private void RefreshPendingList()
        {
            pendingListContainer.Clear();

            if (pendingScripts.Count == 0)
            {
                pendingListContainer.style.display = DisplayStyle.None;
                confirmButton.style.display = DisplayStyle.None;
                return;
            }

            pendingListContainer.style.display = DisplayStyle.Flex;
            confirmButton.style.display = DisplayStyle.Flex;

            var header = new Label($"대기 중: {pendingScripts.Count}개");
            header.style.marginBottom = 5;
            header.style.color = new Color(0.7f, 0.9f, 0.7f);
            pendingListContainer.Add(header);

            foreach (string path in pendingScripts)
            {
                string fileName = Path.GetFileName(path);
                string content = File.ReadAllText(path);
                bool isEditor = IsEditorScript(content);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                var typeLabel = new Label(isEditor ? "[E]" : "[R]");
                typeLabel.style.width = 25;
                typeLabel.style.color = isEditor ? new Color(0.6f, 0.8f, 1f) : new Color(0.6f, 1f, 0.6f);
                row.Add(typeLabel);

                var nameLabel = new Label(fileName);
                nameLabel.style.flexGrow = 1;
                row.Add(nameLabel);

                string capturedPath = path;
                var removeBtn = new Button(() => RemovePendingScript(capturedPath));
                removeBtn.text = "✕";
                removeBtn.style.width = 20;
                removeBtn.style.height = 18;
                removeBtn.style.backgroundColor = new Color(0.4f, 0.2f, 0.2f);
                row.Add(removeBtn);

                pendingListContainer.Add(row);
            }
        }

        private void RemovePendingScript(string path)
        {
            pendingScripts.Remove(path);
            RefreshPendingList();
        }

        private void ConfirmAddScripts()
        {
            if (pendingScripts.Count == 0) return;

            string featureName = newFeatureNameField.value.Trim().Replace(" ", "");
            if (string.IsNullOrEmpty(featureName))
            {
                EditorUtility.DisplayDialog("Error", "기능 이름을 먼저 입력하세요.", "OK");
                return;
            }

            ProcessPendingScripts(featureName);
        }

        private void ProcessPendingScripts(string featureName)
        {
            string editorPath = Path.Combine(currentPackagePath, "Editor");
            string runtimePath = Path.Combine(currentPackagePath, "Runtime");
            string editorFeaturePath = Path.Combine(editorPath, featureName);
            string runtimeFeaturePath = Path.Combine(runtimePath, featureName);

            List<string> editorScripts = new List<string>();
            List<string> runtimeScripts = new List<string>();

            foreach (string sourceFullPath in pendingScripts)
            {
                string content = File.ReadAllText(sourceFullPath);

                if (IsEditorScript(content))
                    editorScripts.Add(sourceFullPath);
                else
                    runtimeScripts.Add(sourceFullPath);
            }

            if (!Directory.Exists(editorPath))
            {
                Directory.CreateDirectory(editorPath);
                CreateFolderMeta(currentPackagePath, "Editor");
                CreateEditorAsmdef(editorPath);
            }

            if (runtimeScripts.Count > 0 && !Directory.Exists(runtimePath))
            {
                Directory.CreateDirectory(runtimePath);
                CreateFolderMeta(currentPackagePath, "Runtime");
                CreateRuntimeAsmdef(runtimePath);
                UpdateEditorAsmdefWithRuntimeReference(editorPath);
            }

            if (editorScripts.Count > 0)
            {
                if (!Directory.Exists(editorFeaturePath))
                {
                    Directory.CreateDirectory(editorFeaturePath);
                    CreateFolderMeta(editorPath, featureName);
                }

                foreach (string source in editorScripts)
                {
                    string fileName = Path.GetFileName(source);
                    string dest = Path.Combine(editorFeaturePath, fileName);
                    File.Copy(source, dest, true);
                    CreateScriptMeta(dest);
                }
            }

            if (runtimeScripts.Count > 0)
            {
                if (!Directory.Exists(runtimeFeaturePath))
                {
                    Directory.CreateDirectory(runtimeFeaturePath);
                    CreateFolderMeta(runtimePath, featureName);
                }

                foreach (string source in runtimeScripts)
                {
                    string fileName = Path.GetFileName(source);
                    string dest = Path.Combine(runtimeFeaturePath, fileName);
                    File.Copy(source, dest, true);
                    CreateScriptMeta(dest);
                }
            }

            newFeatureNameField.value = "";
            pendingScripts.Clear();
            RefreshPendingList();
            RefreshFeatureList();

            string message = $"'{featureName}' 추가 완료!\n\n";
            if (editorScripts.Count > 0)
                message += $"✓ Editor: {editorScripts.Count}개 ({string.Join(", ", editorScripts.Select(Path.GetFileName))})\n";
            if (runtimeScripts.Count > 0)
                message += $"✓ Runtime: {runtimeScripts.Count}개 ({string.Join(", ", runtimeScripts.Select(Path.GetFileName))})\n";

            EditorUtility.DisplayDialog("완료", message, "OK");
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
            versionField.style.width = 80;
            versionField.style.marginRight = 15;
            versionRow.Add(versionField);

            var patchBtn = new Button(() => BumpVersion("patch"));
            patchBtn.text = "+0.0.1";
            patchBtn.style.width = 60;
            patchBtn.style.marginRight = 5;
            versionRow.Add(patchBtn);

            var minorBtn = new Button(() => BumpVersion("minor"));
            minorBtn.text = "+0.1.0";
            minorBtn.style.width = 60;
            minorBtn.style.marginRight = 5;
            versionRow.Add(minorBtn);

            var majorBtn = new Button(() => BumpVersion("major"));
            majorBtn.text = "+1.0.0";
            majorBtn.style.width = 60;
            versionRow.Add(majorBtn);

            step3Container.Add(versionRow);

            var saveBtn = new Button(SavePackageJson);
            saveBtn.text = "💾 package.json 저장";
            saveBtn.style.height = 32;
            saveBtn.style.marginBottom = 10;
            step3Container.Add(saveBtn);

            var helpBox = new HelpBox(
                "저장 후 GitHub Desktop에서 Commit & Push\n\n" +
                "설치: Package Manager → Add package from git URL",
                HelpBoxMessageType.Info);
            step3Container.Add(helpBox);

            var devModeSection = new VisualElement();
            devModeSection.style.marginTop = 15;
            devModeSection.style.paddingTop = 10;
            devModeSection.style.borderTopWidth = 1;
            devModeSection.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);

            var devModeLabel = new Label("📝 개발 모드");
            devModeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            devModeLabel.style.marginBottom = 5;
            devModeSection.Add(devModeLabel);

            var devModeDesc = new Label("활성화하면 패키지를 직접 수정할 수 있음");
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
            container.style.marginBottom = 12;
            container.style.paddingTop = 10;
            container.style.paddingBottom = 12;
            container.style.paddingLeft = 12;
            container.style.paddingRight = 12;
            container.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
            container.style.borderTopLeftRadius = 8;
            container.style.borderTopRightRadius = 8;
            container.style.borderBottomLeftRadius = 8;
            container.style.borderBottomRightRadius = 8;
            container.style.borderLeftWidth = 3;
            container.style.borderLeftColor = new Color(0.3f, 0.5f, 0.8f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.marginBottom = 10;

            var numLabel = new Label(stepNum);
            numLabel.style.fontSize = 12;
            numLabel.style.color = new Color(0.4f, 0.7f, 1f);
            numLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            numLabel.style.marginRight = 10;
            header.Add(numLabel);

            var titleLabel = new Label(stepTitle);
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLabel);

            container.Add(header);
            parent.Add(container);

            return container;
        }

        private TextField CreateTextField(VisualElement parent, string label, string defaultValue)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var lbl = new Label(label);
            lbl.style.width = 120;
            row.Add(lbl);

            var field = new TextField();
            field.value = defaultValue;
            field.style.flexGrow = 1;
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

            var features = new Dictionary<string, (bool hasEditor, bool hasRuntime, int editorScripts, int runtimeScripts)>();

            if (Directory.Exists(editorPath))
            {
                foreach (string dir in Directory.GetDirectories(editorPath))
                {
                    string name = Path.GetFileName(dir);
                    int scripts = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Length;
                    features[name] = (true, false, scripts, 0);
                }
            }

            if (Directory.Exists(runtimePath))
            {
                foreach (string dir in Directory.GetDirectories(runtimePath))
                {
                    string name = Path.GetFileName(dir);
                    int scripts = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Length;
                    if (features.ContainsKey(name))
                    {
                        var existing = features[name];
                        features[name] = (existing.hasEditor, true, existing.editorScripts, scripts);
                    }
                    else
                    {
                        features[name] = (false, true, 0, scripts);
                    }
                }
            }

            if (features.Count == 0)
            {
                AddEmptyLabel("기능 이름 입력 후 스크립트를 드래그하세요");
                return;
            }

            foreach (var kvp in features)
            {
                string featureName = kvp.Key;
                var info = kvp.Value;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 4;
                row.style.alignItems = Align.Center;

                var icon = new Label("📁");
                icon.style.marginRight = 8;
                row.Add(icon);

                var label = new Label(featureName);
                label.style.flexGrow = 1;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(label);

                string typeText = "";
                if (info.hasEditor && info.hasRuntime)
                    typeText = $"E:{info.editorScripts} R:{info.runtimeScripts}";
                else if (info.hasEditor)
                    typeText = $"Editor:{info.editorScripts}";
                else if (info.hasRuntime)
                    typeText = $"Runtime:{info.runtimeScripts}";

                var countLabel = new Label(typeText);
                countLabel.style.color = Color.gray;
                countLabel.style.marginRight = 10;
                row.Add(countLabel);

                var deleteBtn = new Button(() => DeleteFeature(featureName));
                deleteBtn.text = "✕";
                deleteBtn.style.width = 24;
                deleteBtn.style.height = 20;
                deleteBtn.style.backgroundColor = new Color(0.5f, 0.2f, 0.2f);
                row.Add(deleteBtn);

                featureListContainer.Add(row);
            }
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
                string editorFeaturePath = Path.Combine(currentPackagePath, "Editor", featureName);
                string runtimeFeaturePath = Path.Combine(currentPackagePath, "Runtime", featureName);

                if (Directory.Exists(editorFeaturePath))
                {
                    Directory.Delete(editorFeaturePath, true);
                    string metaPath = editorFeaturePath + ".meta";
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                }

                if (Directory.Exists(runtimeFeaturePath))
                {
                    Directory.Delete(runtimeFeaturePath, true);
                    string metaPath = runtimeFeaturePath + ".meta";
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                }

                RefreshFeatureList();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"삭제 실패:\n{e.Message}", "OK");
            }
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
                case "major": major++; minor = 0; patch = 0; break;
                case "minor": minor++; patch = 0; break;
                case "patch": patch++; break;
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
                    gitUrl = EditorUtility.DisplayDialogComplex("Git URL 입력", 
                        "배포 모드로 전환하려면 Git URL이 필요합니다.\n기본값을 사용하시겠습니까?", 
                        "기본값 사용", "취소", "") == 0 ? gitUrl : null;
                    
                    if (string.IsNullOrEmpty(gitUrl)) return;
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
                if (!string.IsNullOrEmpty(currentUrl) && currentUrl.StartsWith("http"))
                {
                    EditorPrefs.SetString($"UPMCreator_GitUrl_{packageName}", currentUrl);
                }

                manifestContent = SetPackageSource(manifestContent, packageName, localPath);
                File.WriteAllText(manifestPath, manifestContent);
                
                devModeStatus.text = "✓ 개발 모드 활성화 (수정 가능)";
                devModeStatus.style.color = new Color(0.4f, 0.9f, 0.4f);
                devModeButton.text = "배포 모드로 전환";
            }

            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            };
        }

        private bool IsDevModeEnabled(string manifestContent, string packageName)
        {
            string source = GetCurrentPackageSource(manifestContent, packageName);
            return !string.IsNullOrEmpty(source) && source.StartsWith("file:");
        }

        private string GetCurrentPackageSource(string manifestContent, string packageName)
        {
            string pattern = $"\"{packageName}\": \"";
            int startIndex = manifestContent.IndexOf(pattern);
            if (startIndex == -1) return null;

            startIndex += pattern.Length;
            int endIndex = manifestContent.IndexOf("\"", startIndex);
            if (endIndex == -1) return null;

            return manifestContent.Substring(startIndex, endIndex - startIndex);
        }

        private string SetPackageSource(string manifestContent, string packageName, string newSource)
        {
            string pattern = $"\"{packageName}\": \"";
            int startIndex = manifestContent.IndexOf(pattern);
            
            if (startIndex == -1)
            {
                int dependenciesIndex = manifestContent.IndexOf("\"dependencies\"");
                if (dependenciesIndex == -1) return manifestContent;
                
                int braceIndex = manifestContent.IndexOf("{", dependenciesIndex);
                if (braceIndex == -1) return manifestContent;

                string newEntry = $"\n    \"{packageName}\": \"{newSource}\",";
                return manifestContent.Insert(braceIndex + 1, newEntry);
            }

            startIndex += pattern.Length;
            int endIndex = manifestContent.IndexOf("\"", startIndex);
            if (endIndex == -1) return manifestContent;

            return manifestContent.Substring(0, startIndex) + newSource + manifestContent.Substring(endIndex);
        }

        private void UpdateDevModeUI()
        {
            if (string.IsNullOrEmpty(currentPackagePath) || devModeButton == null) return;

            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;

            string packageName = packageNameField.value;
            string manifestContent = File.ReadAllText(manifestPath);
            bool isDevMode = IsDevModeEnabled(manifestContent, packageName);

            if (isDevMode)
            {
                devModeStatus.text = "✓ 개발 모드 활성화 중";
                devModeStatus.style.color = new Color(0.4f, 0.9f, 0.4f);
                devModeButton.text = "배포 모드로 전환";
            }
            else
            {
                devModeStatus.text = "";
                devModeButton.text = "개발 모드 활성화";
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
        }

        private string ConvertToAsmdefName(string packageName)
        {
            string[] parts = packageName.Split('.');
            string result = "";
            for (int i = 1; i < parts.Length; i++)
            {
                if (i > 1) result += ".";
                result += char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return result;
        }

        private string ConvertToNamespace(string packageName)
        {
            return ConvertToAsmdefName(packageName);
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