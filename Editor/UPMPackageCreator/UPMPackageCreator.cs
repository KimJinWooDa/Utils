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
            Image,
            Resource,
            Shader
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
        private VisualElement step1Body;
        private Label step1Arrow;
        private Label step1SummaryLabel;
        private bool step1Collapsed = true;
        private VisualElement step2Container;
        private VisualElement step3Container;
        private VisualElement featureListContainer;
        private VisualElement dropZone;
        private Label dropLabel;
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

        private VisualElement step2TabsRow;
        private VisualElement step2TabAdd;
        private VisualElement step2TabList;
        private VisualElement step2TabOrganize;
        private Button step2TabAddBtn;
        private Button step2TabListBtn;
        private Button step2TabOrganizeBtn;

        private enum Step2Tab { Add, List, Organize }

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

        // ─── Design Tokens ───
        static readonly Color BgDeep       = new Color(0.16f, 0.16f, 0.18f);
        static readonly Color BgCard       = new Color(0.21f, 0.21f, 0.24f);
        static readonly Color BgCardHover  = new Color(0.24f, 0.24f, 0.28f);
        static readonly Color BgInput      = new Color(0.18f, 0.18f, 0.21f);
        static readonly Color Border       = new Color(0.30f, 0.30f, 0.35f);
        static readonly Color BorderLight  = new Color(0.36f, 0.36f, 0.42f);
        static readonly Color TextPrimary  = new Color(0.95f, 0.95f, 0.97f);
        static readonly Color TextSecondary= new Color(0.70f, 0.70f, 0.76f);
        static readonly Color TextMuted    = new Color(0.52f, 0.52f, 0.58f);
        static readonly Color AccentBlue   = new Color(0.40f, 0.60f, 1.0f);
        static readonly Color AccentGreen  = new Color(0.35f, 0.85f, 0.55f);
        static readonly Color AccentRed    = new Color(0.95f, 0.40f, 0.40f);
        static readonly Color AccentAmber  = new Color(1.0f, 0.80f, 0.30f);
        static readonly Color AccentPurple = new Color(0.70f, 0.50f, 1.0f);
        static readonly Color AccentCyan   = new Color(0.35f, 0.88f, 0.90f);
        const int RadiusLg = 8;
        const int RadiusMd = 6;
        const int RadiusSm = 4;
        const int RadiusXs = 3;

        [MenuItem("Tools/TelleR/Tool/UPM Package Creator")]
        public static void ShowWindow()
        {
            var window = GetWindow<UPMPackageCreator>();
            window.titleContent = new GUIContent("UPM Package Creator");
            window.minSize = new Vector2(480, 720);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = BgDeep;

            var pageScroll = new ScrollView();
            pageScroll.style.flexGrow = 1;
            pageScroll.style.paddingTop = 20;
            pageScroll.style.paddingLeft = 20;
            pageScroll.style.paddingRight = 20;
            pageScroll.style.paddingBottom = 20;
            root.Add(pageScroll);

            CreateStep1(pageScroll);
            CreateStep2(pageScroll);
            CreateStep3(pageScroll);

            UpdateStepStates();
            TryLoadLastPackage();
        }

        // ─── UI Builder Helpers ───

        void SetRadius(IStyle s, int r)
        {
            s.borderTopLeftRadius = r; s.borderTopRightRadius = r;
            s.borderBottomLeftRadius = r; s.borderBottomRightRadius = r;
        }

        void SetBorder(IStyle s, float w, Color c)
        {
            s.borderTopWidth = w; s.borderBottomWidth = w;
            s.borderLeftWidth = w; s.borderRightWidth = w;
            s.borderTopColor = c; s.borderBottomColor = c;
            s.borderLeftColor = c; s.borderRightColor = c;
        }

        void SetPadding(IStyle s, int v, int h)
        {
            s.paddingTop = v; s.paddingBottom = v;
            s.paddingLeft = h; s.paddingRight = h;
        }

        VisualElement CreateCard(VisualElement parent, string stepNum, string title,
            bool collapsible = false, bool startCollapsed = false,
            System.Action<VisualElement, Label, Label> onHeaderCreated = null)
        {
            var card = new VisualElement();
            card.style.marginBottom = 16;
            card.style.backgroundColor = BgCard;
            SetRadius(card.style, RadiusLg);
            SetBorder(card.style, 1, Border);
            card.style.overflow = Overflow.Hidden;
            parent.Add(card);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            SetPadding(header.style, 14, 18);
            header.style.backgroundColor = new Color(0.10f, 0.105f, 0.13f);
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = Border;
            card.Add(header);

            Label arrow = null;
            if (collapsible)
            {
                arrow = new Label(startCollapsed ? "\u25B6" : "\u25BC");
                arrow.style.fontSize = 11;
                arrow.style.color = TextSecondary;
                arrow.style.marginRight = 8;
                arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
                header.Add(arrow);
            }

            if (!string.IsNullOrEmpty(stepNum))
            {
                var badge = new Label(stepNum);
                badge.style.fontSize = 11;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.color = Color.white;
                badge.style.backgroundColor = AccentBlue;
                badge.style.paddingLeft = 10;
                badge.style.paddingRight = 10;
                badge.style.paddingTop = 4;
                badge.style.paddingBottom = 4;
                SetRadius(badge.style, RadiusSm);
                badge.style.marginRight = 12;
                header.Add(badge);
            }

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 15;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = TextPrimary;
            header.Add(titleLabel);

            Label summaryLabel = null;
            if (collapsible)
            {
                summaryLabel = new Label("");
                summaryLabel.style.fontSize = 12;
                summaryLabel.style.color = TextMuted;
                summaryLabel.style.marginLeft = 12;
                summaryLabel.style.flexGrow = 1;
                summaryLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                header.Add(summaryLabel);
            }

            var body = new VisualElement();
            SetPadding(body.style, 16, 18);
            card.Add(body);

            if (collapsible)
            {
                if (startCollapsed)
                    body.style.display = DisplayStyle.None;

                header.RegisterCallback<ClickEvent>(evt =>
                {
                    bool isHidden = body.style.display == DisplayStyle.None;
                    body.style.display = isHidden ? DisplayStyle.Flex : DisplayStyle.None;
                    if (arrow != null)
                        arrow.text = isHidden ? "\u25BC" : "\u25B6";
                });

                onHeaderCreated?.Invoke(header, arrow, summaryLabel);
            }

            return body;
        }

        TextField CreateStyledField(VisualElement parent, string label, string defaultValue)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;

            var lbl = new Label(label);
            lbl.style.width = 120;
            lbl.style.minWidth = 120;
            lbl.style.color = TextSecondary;
            lbl.style.fontSize = 13;
            row.Add(lbl);

            var field = new TextField();
            field.value = defaultValue;
            field.style.flexGrow = 1;
            field.style.height = 30;
            field.style.minHeight = 30;
            row.Add(field);

            parent.Add(row);
            return field;
        }

        Button CreatePrimaryButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.height = 36;
            btn.style.backgroundColor = AccentBlue;
            btn.style.color = Color.white;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.fontSize = 13;
            SetRadius(btn.style, RadiusSm);
            SetBorder(btn.style, 0, Color.clear);
            btn.style.marginTop = 2;
            btn.style.marginBottom = 2;
            return btn;
        }

        Button CreateSecondaryButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.height = 36;
            btn.style.backgroundColor = BgCardHover;
            btn.style.color = TextPrimary;
            btn.style.fontSize = 13;
            SetRadius(btn.style, RadiusSm);
            SetBorder(btn.style, 1, BorderLight);
            btn.style.marginTop = 2;
            btn.style.marginBottom = 2;
            return btn;
        }

        Button CreateGhostButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.height = 28;
            btn.style.backgroundColor = Color.clear;
            btn.style.color = TextSecondary;
            btn.style.fontSize = 12;
            SetRadius(btn.style, RadiusXs);
            SetBorder(btn.style, 1, Border);
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            return btn;
        }

        Button CreateDangerButton(string text, Action onClick, int size = 24)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.width = size;
            btn.style.height = size;
            btn.style.backgroundColor = new Color(AccentRed.r, AccentRed.g, AccentRed.b, 0.15f);
            btn.style.color = AccentRed;
            btn.style.fontSize = 11;
            SetRadius(btn.style, RadiusXs);
            SetBorder(btn.style, 1, new Color(AccentRed.r, AccentRed.g, AccentRed.b, 0.3f));
            return btn;
        }

        Button CreateSmallButton(string text, Action onClick, Color color)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.height = 22;
            btn.style.backgroundColor = new Color(color.r, color.g, color.b, 0.12f);
            btn.style.color = color;
            btn.style.fontSize = 11;
            btn.style.paddingLeft = 8;
            btn.style.paddingRight = 8;
            btn.style.marginRight = 4;
            SetRadius(btn.style, RadiusXs);
            SetBorder(btn.style, 1, new Color(color.r, color.g, color.b, 0.25f));
            return btn;
        }

        VisualElement CreatePill(string text, Color color)
        {
            var pill = new VisualElement();
            pill.style.backgroundColor = new Color(color.r, color.g, color.b, 0.15f);
            SetRadius(pill.style, 20);
            pill.style.paddingLeft = 8;
            pill.style.paddingRight = 8;
            pill.style.paddingTop = 2;
            pill.style.paddingBottom = 2;
            pill.style.marginRight = 6;

            var label = new Label(text);
            label.style.fontSize = 11;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            pill.Add(label);

            return pill;
        }

        VisualElement CreateDivider()
        {
            var div = new VisualElement();
            div.style.height = 1;
            div.style.backgroundColor = Border;
            div.style.marginTop = 12;
            div.style.marginBottom = 12;
            return div;
        }

        // ─── Step 1 ───

        private void CreateStep1(VisualElement root)
        {
            step1Container = CreateCard(root, "1", "Package Setup",
                collapsible: true, startCollapsed: true,
                onHeaderCreated: (header, arrow, summary) =>
                {
                    step1Arrow = arrow;
                    step1SummaryLabel = summary;
                });
            step1Body = step1Container;

            packageNameField = CreateStyledField(step1Container, "Package Name", "com.teller.util");
            displayNameField = CreateStyledField(step1Container, "Display Name", "TelleR Utilities");
            descriptionField = CreateStyledField(step1Container, "Description", "Unity editor utilities by TelleR - Fast Clone, and more");
            authorField = CreateStyledField(step1Container, "Author", "TelleR");
            unityVersionField = CreateStyledField(step1Container, "Min Unity", "2021.3");

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 12;

            var createBtn = CreatePrimaryButton("New Package", CreateNewPackage);
            createBtn.style.flexGrow = 1;
            createBtn.style.marginRight = 8;
            buttonRow.Add(createBtn);

            var loadBtn = CreateSecondaryButton("Load Existing", LoadExistingPackage);
            loadBtn.style.flexGrow = 1;
            buttonRow.Add(loadBtn);

            step1Container.Add(buttonRow);

            step1Status = new Label("");
            step1Status.style.marginTop = 10;
            step1Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            step1Status.style.fontSize = 12;
            step1Container.Add(step1Status);
        }

        // ─── Step 2 ───

        private void CreateStep2(VisualElement root)
        {
            step2Container = CreateCard(root, "2", "Feature Manager");

            step2TabsRow = new VisualElement();
            step2TabsRow.style.flexDirection = FlexDirection.Row;
            step2TabsRow.style.alignItems = Align.Center;
            step2TabsRow.style.marginBottom = 14;
            step2TabsRow.style.backgroundColor = new Color(0.10f, 0.105f, 0.13f);
            SetRadius(step2TabsRow.style, RadiusSm);
            SetPadding(step2TabsRow.style, 3, 3);
            step2Container.Add(step2TabsRow);

            step2TabAddBtn = CreateTabButton("Add", () => SelectStep2Tab(Step2Tab.Add));
            step2TabListBtn = CreateTabButton("Features", () => SelectStep2Tab(Step2Tab.List));
            step2TabOrganizeBtn = CreateTabButton("Organize", () => SelectStep2Tab(Step2Tab.Organize));

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

        private Button CreateTabButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.height = 30;
            btn.style.minHeight = 30;
            btn.style.paddingLeft = 16;
            btn.style.paddingRight = 16;
            btn.style.marginRight = 2;
            btn.style.flexGrow = 1;
            SetRadius(btn.style, RadiusXs);
            SetBorder(btn.style, 0, Color.clear);
            btn.style.backgroundColor = Color.clear;
            btn.style.color = TextMuted;
            btn.style.fontSize = 13;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
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
            button.style.backgroundColor = selected ? new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.20f) : Color.clear;
            button.style.color = selected ? AccentBlue : TextMuted;
        }

        // ─── Step 2: Add Tab ───

        private void CreateStep2AddUI(VisualElement parent)
        {
            var desc = new Label("Create a feature folder and drag files into it.");
            desc.style.color = TextMuted;
            desc.style.fontSize = 11;
            desc.style.marginBottom = 10;
            parent.Add(desc);

            var featureRow = new VisualElement();
            featureRow.style.flexDirection = FlexDirection.Row;
            featureRow.style.alignItems = Align.Center;
            featureRow.style.marginBottom = 12;

            var featureLabel = new Label("Feature");
            featureLabel.style.color = TextSecondary;
            featureLabel.style.fontSize = 13;
            featureLabel.style.width = 60;
            featureRow.Add(featureLabel);

            newFeatureNameField = new TextField();
            newFeatureNameField.value = "";
            newFeatureNameField.style.flexGrow = 1;
            newFeatureNameField.style.height = 30;
            newFeatureNameField.style.minHeight = 30;
            featureRow.Add(newFeatureNameField);

            parent.Add(featureRow);

            // Drop zone
            dropZone = new VisualElement();
            dropZone.style.minHeight = 130;
            dropZone.style.backgroundColor = new Color(0.10f, 0.105f, 0.13f);
            SetRadius(dropZone.style, RadiusMd);
            SetBorder(dropZone.style, 2, Border);
            dropZone.style.justifyContent = Justify.Center;
            dropZone.style.alignItems = Align.Center;
            SetPadding(dropZone.style, 20, 20);

            var dropIcon = new Label("+");
            dropIcon.style.fontSize = 28;
            dropIcon.style.color = TextMuted;
            dropIcon.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropIcon.style.marginBottom = 6;
            dropZone.Add(dropIcon);

            dropLabel = new Label("Drop scripts, materials, shaders here");
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.color = TextSecondary;
            dropLabel.style.fontSize = 13;
            dropZone.Add(dropLabel);

            var dropHint = new Label(".cs  /  Image  /  Material  /  ScriptableObject  /  Shader");
            dropHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropHint.style.color = TextMuted;
            dropHint.style.fontSize = 11;
            dropHint.style.marginTop = 4;
            dropZone.Add(dropHint);

            dropZone.RegisterCallback<DragEnterEvent>(OnDragEnter);
            dropZone.RegisterCallback<DragLeaveEvent>(OnDragLeave);
            dropZone.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            dropZone.RegisterCallback<DragPerformEvent>(OnDragPerform);

            parent.Add(dropZone);

            // Pending list
            pendingListContainer = new VisualElement();
            pendingListContainer.style.marginTop = 12;
            pendingListContainer.style.display = DisplayStyle.None;
            parent.Add(pendingListContainer);

            // Confirm button
            confirmButton = CreatePrimaryButton("Confirm & Add", ConfirmAddItems);
            confirmButton.style.backgroundColor = AccentGreen;
            confirmButton.style.marginTop = 8;
            confirmButton.style.display = DisplayStyle.None;
            parent.Add(confirmButton);

            // Bottom actions
            var bottomRow = new VisualElement();
            bottomRow.style.flexDirection = FlexDirection.Row;
            bottomRow.style.marginTop = 10;

            var openFolderBtn = CreateGhostButton("Open Folder", OpenPackageFolder);
            openFolderBtn.style.flexGrow = 1;
            openFolderBtn.style.marginRight = 6;
            bottomRow.Add(openFolderBtn);

            var clearPendingBtn = CreateGhostButton("Clear Queue", ClearPending);
            clearPendingBtn.style.width = 100;
            bottomRow.Add(clearPendingBtn);

            parent.Add(bottomRow);

            step2Status = new Label("Complete Step 1 first");
            step2Status.style.marginTop = 10;
            step2Status.style.color = TextMuted;
            step2Status.style.fontSize = 11;
            step2Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            parent.Add(step2Status);
        }

        // ─── Step 2: List Tab ───

        private void CreateStep2ListUI(VisualElement parent)
        {
            var desc = new Label("All feature folders in this package. Click to open in Explorer.");
            desc.style.color = TextMuted;
            desc.style.fontSize = 11;
            desc.style.marginBottom = 10;
            parent.Add(desc);

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 10;

            var header = new Label("Registered Features");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = TextPrimary;
            header.style.fontSize = 13;
            header.style.flexGrow = 1;
            headerRow.Add(header);

            var refreshBtn = CreateGhostButton("Refresh", RefreshFeatureList);
            headerRow.Add(refreshBtn);

            parent.Add(headerRow);

            featureListContainer = new VisualElement();
            featureListContainer.style.flexDirection = FlexDirection.Row;
            featureListContainer.style.flexWrap = Wrap.Wrap;
            featureListContainer.style.alignItems = Align.FlexStart;
            parent.Add(featureListContainer);
        }

        // ─── Step 2: Organize Tab ───

        private void CreateOrganizerUI(VisualElement parent)
        {
            organizerContainer = new VisualElement();

            var desc = new Label("Move files between Editor (editor-only) and Runtime (included in build).");
            desc.style.color = TextMuted;
            desc.style.fontSize = 11;
            desc.style.marginBottom = 10;
            organizerContainer.Add(desc);

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 10;

            var header = new Label("File Organizer");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = TextPrimary;
            header.style.fontSize = 13;
            header.style.flexGrow = 1;
            headerRow.Add(header);

            var reloadBtn = CreateGhostButton("Refresh", RefreshOrganizer);
            headerRow.Add(reloadBtn);

            organizerContainer.Add(headerRow);

            var popupRow = new VisualElement();
            popupRow.style.flexDirection = FlexDirection.Row;
            popupRow.style.alignItems = Align.Center;
            popupRow.style.marginBottom = 10;

            var popupLabel = new Label("Feature");
            popupLabel.style.width = 60;
            popupLabel.style.color = TextSecondary;
            popupLabel.style.fontSize = 13;
            popupRow.Add(popupLabel);

            organizerFeaturePopup = new PopupField<string>(new List<string> { "-" }, 0);
            organizerFeaturePopup.style.flexGrow = 1;
            organizerFeaturePopup.RegisterValueChangedCallback(_ => RefreshOrganizer());
            popupRow.Add(organizerFeaturePopup);

            organizerContainer.Add(popupRow);

            organizerScroll = new ScrollView();
            organizerScroll.style.minHeight = 180;
            organizerScroll.style.maxHeight = 300;
            organizerScroll.style.backgroundColor = new Color(0.10f, 0.105f, 0.13f);
            SetRadius(organizerScroll.style, RadiusSm);
            SetBorder(organizerScroll.style, 1, Border);
            SetPadding(organizerScroll.style, 10, 12);

            organizerContainer.Add(organizerScroll);
            parent.Add(organizerContainer);

            RefreshOrganizerFeatureOptions();
            RefreshOrganizer();
        }

        // ─── Step 3 ───

        private void CreateStep3(VisualElement root)
        {
            step3Container = CreateCard(root, "3", "Version & Deploy");

            var versionRow = new VisualElement();
            versionRow.style.flexDirection = FlexDirection.Row;
            versionRow.style.alignItems = Align.Center;
            versionRow.style.marginBottom = 12;

            var versionLabel = new Label("Version");
            versionLabel.style.width = 60;
            versionLabel.style.color = TextSecondary;
            versionLabel.style.fontSize = 13;
            versionRow.Add(versionLabel);

            versionField = new TextField();
            versionField.value = "1.0.0";
            versionField.style.width = 90;
            versionField.style.marginRight = 12;
            versionField.style.height = 30;
            versionField.style.minHeight = 30;
            versionRow.Add(versionField);

            var patchBtn = CreateSmallButton("+0.0.1", () => BumpVersion("patch"), AccentGreen);
            patchBtn.style.height = 28;
            versionRow.Add(patchBtn);

            var minorBtn = CreateSmallButton("+0.1.0", () => BumpVersion("minor"), AccentAmber);
            minorBtn.style.height = 28;
            versionRow.Add(minorBtn);

            var majorBtn = CreateSmallButton("+1.0.0", () => BumpVersion("major"), AccentRed);
            majorBtn.style.height = 28;
            versionRow.Add(majorBtn);

            step3Container.Add(versionRow);

            var saveBtn = CreatePrimaryButton("Save package.json", SavePackageJson);
            step3Container.Add(saveBtn);

            step3Container.Add(CreateDivider());

            var devModeLabel = new Label("Package Mode");
            devModeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            devModeLabel.style.color = TextPrimary;
            devModeLabel.style.fontSize = 13;
            devModeLabel.style.marginBottom = 4;
            step3Container.Add(devModeLabel);

            var devModeDesc = new Label("Dev = editable local folder  /  Deploy = read-only git URL for distribution");
            devModeDesc.style.color = TextMuted;
            devModeDesc.style.fontSize = 11;
            devModeDesc.style.marginBottom = 8;
            devModeDesc.style.whiteSpace = WhiteSpace.Normal;
            step3Container.Add(devModeDesc);

            devModeStatus = new Label("");
            devModeStatus.style.fontSize = 12;
            devModeStatus.style.color = TextPrimary;
            devModeStatus.style.backgroundColor = BgInput;
            SetRadius(devModeStatus.style, RadiusSm);
            SetPadding(devModeStatus.style, 10, 14);
            SetBorder(devModeStatus.style, 1, Border);
            devModeStatus.style.marginBottom = 10;
            devModeStatus.style.whiteSpace = WhiteSpace.Normal;
            step3Container.Add(devModeStatus);

            devModeButton = CreateSecondaryButton("Switch Mode", ToggleDevMode);
            step3Container.Add(devModeButton);
        }

        // ─── Drag & Drop ───

        private void ClearPending()
        {
            pendingItems.Clear();
            RefreshPendingList();
        }

        private void OnDragEnter(DragEnterEvent evt)
        {
            SetBorder(dropZone.style, 2, AccentBlue);
            dropZone.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.08f);
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
                EditorUtility.DisplayDialog("Error", "Complete Step 1 first.", "OK");
                return;
            }

            string featureName = NormalizeFeatureName(newFeatureNameField.value);
            if (string.IsNullOrEmpty(featureName))
            {
                EditorUtility.DisplayDialog("Error", "Enter a feature name first.\n(e.g. AudioVolume3D)", "OK");
                return;
            }

            var objects = DragAndDrop.objectReferences?.ToList() ?? new List<UnityEngine.Object>();
            if (objects.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No items detected.", "OK");
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

                if (IsShaderAsset(assetPath))
                {
                    if (pendingItems.Any(p => p.SourceFullPath == fullPath)) continue;

                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Shader,
                        Placement = PendingPlacement.Runtime,
                        UseResources = false,
                        DetectedEditor = false
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

                if (IsResourceAsset(assetPath))
                {
                    if (pendingItems.Any(p => p.SourceFullPath == fullPath)) continue;

                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Resource,
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
                EditorUtility.DisplayDialog("Error", "Unsupported file type.\n\nSupported: .cs / Image / Material / ScriptableObject / Shader", "OK");
                return;
            }

            RefreshPendingList();
        }

        private void ResetDropZoneStyle()
        {
            SetBorder(dropZone.style, 2, Border);
            dropZone.style.backgroundColor = new Color(0.10f, 0.105f, 0.13f);
        }

        // ─── Pending List UI ───

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

            var header = new Label($"Queue: {pendingItems.Count} item{(pendingItems.Count > 1 ? "s" : "")}");
            header.style.marginBottom = 8;
            header.style.color = AccentGreen;
            header.style.fontSize = 12;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            pendingListContainer.Add(header);

            foreach (var item in pendingItems.ToList())
            {
                string fileName = Path.GetFileName(item.SourceFullPath);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;
                row.style.backgroundColor = new Color(0.10f, 0.105f, 0.13f);
                SetRadius(row.style, RadiusSm);
                SetBorder(row.style, 1, Border);
                SetPadding(row.style, 6, 10);

                string kindText;
                Color kindColor;
                switch (item.Kind)
                {
                    case PendingKind.Script:   kindText = "CS";  kindColor = AccentBlue; break;
                    case PendingKind.Image:    kindText = "IMG"; kindColor = AccentAmber; break;
                    case PendingKind.Resource: kindText = "RES"; kindColor = AccentCyan; break;
                    case PendingKind.Shader:   kindText = "SHD"; kindColor = AccentPurple; break;
                    default:                   kindText = "?";   kindColor = TextMuted; break;
                }

                row.Add(CreatePill(kindText, kindColor));

                var nameLabel = new Label(fileName);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.color = TextPrimary;
                nameLabel.style.fontSize = 12;
                nameLabel.style.overflow = Overflow.Hidden;
                nameLabel.style.textOverflow = TextOverflow.Ellipsis;
                row.Add(nameLabel);

                List<string> placementChoices;
                if (item.Kind == PendingKind.Script)
                    placementChoices = new List<string> { "Auto", "Editor", "Runtime" };
                else
                    placementChoices = new List<string> { "Editor", "Runtime" };

                int defaultIndex = 0;
                if (item.Kind == PendingKind.Script)
                    defaultIndex = item.Placement == PendingPlacement.Auto ? 0 : (item.Placement == PendingPlacement.Editor ? 1 : 2);
                else
                    defaultIndex = item.Placement == PendingPlacement.Editor ? 0 : 1;

                var placementPopup = new PopupField<string>(placementChoices, Mathf.Clamp(defaultIndex, 0, placementChoices.Count - 1));
                placementPopup.style.width = 80;
                placementPopup.style.marginLeft = 6;
                placementPopup.style.marginRight = 4;
                placementPopup.RegisterValueChangedCallback(e =>
                {
                    if (item.Kind == PendingKind.Script)
                        item.Placement = e.newValue == "Auto" ? PendingPlacement.Auto : (e.newValue == "Editor" ? PendingPlacement.Editor : PendingPlacement.Runtime);
                    else
                        item.Placement = e.newValue == "Editor" ? PendingPlacement.Editor : PendingPlacement.Runtime;
                });
                row.Add(placementPopup);

                if (item.Kind == PendingKind.Script)
                {
                    var hint = new Label(item.DetectedEditor ? "E" : "R");
                    hint.style.width = 18;
                    hint.style.color = item.DetectedEditor ? AccentBlue : AccentGreen;
                    hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                    hint.style.fontSize = 10;
                    hint.style.unityFontStyleAndWeight = FontStyle.Bold;
                    hint.style.marginRight = 4;
                    row.Add(hint);
                }
                else
                {
                    var resourcesToggle = new Toggle("Res");
                    resourcesToggle.value = item.UseResources;
                    resourcesToggle.style.width = 46;
                    resourcesToggle.style.marginRight = 4;
                    resourcesToggle.RegisterValueChangedCallback(e => item.UseResources = e.newValue);
                    row.Add(resourcesToggle);
                }

                string capturedPath = item.SourceFullPath;
                row.Add(CreateDangerButton("x", () => RemovePendingItem(capturedPath), 20));

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
                EditorUtility.DisplayDialog("Error", "Enter a feature name first.", "OK");
                return;
            }

            ProcessPendingItems(featureName);
        }

        // ─── Feature List UI ───

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
                        var e = features[name];
                        features[name] = (e.hasEditor, true, e.hasEditorRes, e.hasRuntimeRes, e.editorFiles, files, e.editorResFiles, e.runtimeResFiles);
                    }
                    else
                        features[name] = (false, true, false, false, 0, files, 0, 0);
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
                        var e = features[name];
                        features[name] = (e.hasEditor, e.hasRuntime, true, e.hasRuntimeRes, e.editorFiles, e.runtimeFiles, files, e.runtimeResFiles);
                    }
                    else
                        features[name] = (false, false, true, false, 0, 0, files, 0);
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
                        var e = features[name];
                        features[name] = (e.hasEditor, e.hasRuntime, e.hasEditorRes, true, e.editorFiles, e.runtimeFiles, e.editorResFiles, files);
                    }
                    else
                        features[name] = (false, false, false, true, 0, 0, 0, files);
                }
            }

            if (features.Count == 0)
            {
                var empty = new Label("No features yet. Add files via the Add tab.");
                empty.style.color = TextMuted;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.fontSize = 11;
                empty.style.marginTop = 16;
                empty.style.marginBottom = 16;
                empty.style.flexGrow = 1;
                featureListContainer.Add(empty);
                return;
            }

            foreach (var kvp in features.OrderBy(k => k.Key))
            {
                string featureName = kvp.Key;
                var info = kvp.Value;

                var card = new VisualElement();
                card.style.width = new Length(100, LengthUnit.Percent);
                card.style.flexDirection = FlexDirection.Column;
                card.style.marginBottom = 6;
                card.style.backgroundColor = new Color(0.10f, 0.105f, 0.13f);
                SetRadius(card.style, RadiusSm);
                SetBorder(card.style, 1, Border);
                SetPadding(card.style, 10, 12);

                var topRow = new VisualElement();
                topRow.style.flexDirection = FlexDirection.Row;
                topRow.style.alignItems = Align.Center;

                var label = new Label(featureName);
                label.style.flexGrow = 1;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = TextPrimary;
                label.style.fontSize = 13;
                topRow.Add(label);

                var focusBtn = CreateSmallButton("Organize", () => {
                    FocusOrganizerFeature(featureName);
                    SelectStep2Tab(Step2Tab.Organize);
                }, AccentBlue);
                topRow.Add(focusBtn);

                var deleteBtn = CreateDangerButton("x", () => DeleteFeature(featureName), 22);
                deleteBtn.style.marginLeft = 4;
                topRow.Add(deleteBtn);

                card.Add(topRow);

                var badgeRow = new VisualElement();
                badgeRow.style.flexDirection = FlexDirection.Row;
                badgeRow.style.flexWrap = Wrap.Wrap;
                badgeRow.style.marginTop = 6;

                if (info.hasEditor) badgeRow.Add(CreatePill($"E:{info.editorFiles}", AccentBlue));
                if (info.hasRuntime) badgeRow.Add(CreatePill($"R:{info.runtimeFiles}", AccentGreen));
                if (info.hasEditorRes) badgeRow.Add(CreatePill($"E/Res:{info.editorResFiles}", AccentPurple));
                if (info.hasRuntimeRes) badgeRow.Add(CreatePill($"R/Res:{info.runtimeResFiles}", AccentAmber));

                card.Add(badgeRow);
                featureListContainer.Add(card);
            }
        }

        // ─── Organizer UI ───

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
            if (organizerScroll == null) return;

            organizerScroll.Clear();

            if (string.IsNullOrEmpty(currentPackagePath) || !Directory.Exists(currentPackagePath))
            {
                AddOrganizerEmpty("Complete Step 1 first.");
                return;
            }

            var features = GetAllFeatureNames();
            if (features.Count == 0)
            {
                AddOrganizerEmpty("No features. Add files first.");
                return;
            }

            string feature = organizerFeaturePopup != null ? organizerFeaturePopup.value : "-";
            if (string.IsNullOrEmpty(feature) || feature == "-")
            {
                AddOrganizerEmpty("Select a feature to organize.");
                return;
            }

            var entries = ScanFeatureEntries(feature);
            if (entries.Count == 0)
            {
                AddOrganizerEmpty("No files in this feature.");
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
            label.style.color = TextMuted;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 12;
            label.style.marginTop = 20;
            label.style.marginBottom = 20;
            organizerScroll.Add(label);
        }

        private VisualElement CreateOrganizerRow(string featureName, FeatureEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.backgroundColor = new Color(BgCard.r, BgCard.g, BgCard.b, 0.5f);
            SetRadius(row.style, RadiusXs);
            SetPadding(row.style, 5, 8);

            row.Add(CreatePill(entry.Tag, entry.TagColor));

            var nameLabel = new Label(entry.FileName);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.color = TextPrimary;
            nameLabel.style.fontSize = 12;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.marginRight = 6;
            row.Add(nameLabel);

            bool isScript = entry.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

            if (isScript)
            {
                row.Add(CreateSmallButton("Editor", () => MoveEntry(featureName, entry, FeatureLocation.EditorFeature), AccentBlue));
                row.Add(CreateSmallButton("Runtime", () => MoveEntry(featureName, entry, FeatureLocation.RuntimeFeature), AccentGreen));
            }
            else
            {
                row.Add(CreateSmallButton("E", () => MoveEntry(featureName, entry, FeatureLocation.EditorFeature), AccentBlue));
                row.Add(CreateSmallButton("R", () => MoveEntry(featureName, entry, FeatureLocation.RuntimeFeature), AccentGreen));
                row.Add(CreateSmallButton("E/Res", () => MoveEntry(featureName, entry, FeatureLocation.EditorResources), AccentPurple));
                row.Add(CreateSmallButton("R/Res", () => MoveEntry(featureName, entry, FeatureLocation.RuntimeResources), AccentAmber));
            }

            row.Add(CreateDangerButton("x", () => DeleteEntry(entry), 20));

            return row;
        }

        // ─── State Management ───

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
                step2Status.text = "Complete Step 1 first";
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

        // ─── Package Operations ───

        private void LoadExistingPackage()
        {
            string folderPath = EditorUtility.OpenFolderPanel("Select Package Folder", "", "");
            if (string.IsNullOrEmpty(folderPath)) return;

            string packageJsonPath = Path.Combine(folderPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                EditorUtility.DisplayDialog("Error", "package.json not found in this folder.", "OK");
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

                step1Status.text = $"Loaded: {Path.GetFileName(folderPath)}";
                step1Status.style.color = AccentGreen;
                UpdateStep1Summary(Path.GetFileName(folderPath));

                UpdateStepStates();
                RefreshFeatureList();
                UpdateDevModeUI();
                EnsureMetaFiles(folderPath);
                RefreshOrganizerFeatureOptions();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Load failed:\n{e.Message}", "OK");
            }
        }

        private void CreateNewPackage()
        {
            string folderPath = EditorUtility.SaveFolderPanel("Select folder for new package", "", "");
            if (string.IsNullOrEmpty(folderPath)) return;

            string packageName = packageNameField.value;
            string packageRoot = Path.Combine(folderPath, packageName);

            if (Directory.Exists(packageRoot))
            {
                if (EditorUtility.DisplayDialog("Folder Exists", $"'{packageName}' already exists. Load it?", "Load", "Cancel"))
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

            step1Status.text = $"Created: {packageName}";
            step1Status.style.color = AccentGreen;
            UpdateStep1Summary(packageName);

            UpdateStepStates();
            RefreshFeatureList();
            RefreshOrganizerFeatureOptions();
            RefreshOrganizer();

            EditorUtility.DisplayDialog("Done", "Package created!\n\nAdd features in Step 2.", "OK");
        }

        private void OpenPackageFolder()
        {
            if (string.IsNullOrEmpty(currentPackagePath))
            {
                EditorUtility.DisplayDialog("Error", "Complete Step 1 first.", "OK");
                return;
            }

            EditorUtility.RevealInFinder(currentPackagePath);
        }

        // ─── Process Pending Items ───

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
            var addedResourcesEditor = new List<string>();
            var addedResourcesRuntime = new List<string>();
            var addedResourcesEditorRes = new List<string>();
            var addedResourcesRuntimeRes = new List<string>();
            var addedShaders = new List<string>();
            var addedShadersRes = new List<string>();

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
                else if (item.Kind == PendingKind.Shader)
                {
                    bool toEditor = item.Placement == PendingPlacement.Editor;
                    string root = toEditor ? editorRoot : runtimeRoot;

                    string shadersDir = Path.Combine(root, featureName, "Shaders");
                    string featureDir = Path.Combine(root, featureName);

                    if (!Directory.Exists(featureDir))
                    {
                        Directory.CreateDirectory(featureDir);
                        CreateFolderMeta(root, featureName);
                    }

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
                        if (!Directory.Exists(shadersDir))
                        {
                            Directory.CreateDirectory(shadersDir);
                            CreateFolderMeta(featureDir, "Shaders");
                        }
                        targetDir = shadersDir;
                    }

                    string dest = Path.Combine(targetDir, fileName);
                    File.Copy(item.SourceFullPath, dest, true);
                    CreateMetaFromSourceOrDefault(item.SourceFullPath, dest, PendingKind.Shader);

                    if (item.UseResources) addedShadersRes.Add(fileName);
                    else addedShaders.Add(fileName);
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
                    CreateMetaFromSourceOrDefault(item.SourceFullPath, dest, item.Kind);

                    if (item.Kind == PendingKind.Image)
                    {
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
                    else
                    {
                        if (toEditor)
                        {
                            if (item.UseResources) addedResourcesEditorRes.Add(fileName);
                            else addedResourcesEditor.Add(fileName);
                        }
                        else
                        {
                            if (item.UseResources) addedResourcesRuntimeRes.Add(fileName);
                            else addedResourcesRuntime.Add(fileName);
                        }
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

            string message = $"'{featureName}' added!\n\n";
            if (addedScriptsEditor.Count > 0) message += $"Editor Script: {addedScriptsEditor.Count} ({string.Join(", ", addedScriptsEditor)})\n";
            if (addedScriptsRuntime.Count > 0) message += $"Runtime Script: {addedScriptsRuntime.Count} ({string.Join(", ", addedScriptsRuntime)})\n";
            if (addedImagesEditor.Count > 0) message += $"Editor Image: {addedImagesEditor.Count} ({string.Join(", ", addedImagesEditor)})\n";
            if (addedImagesRuntime.Count > 0) message += $"Runtime Image: {addedImagesRuntime.Count} ({string.Join(", ", addedImagesRuntime)})\n";
            if (addedImagesEditorRes.Count > 0) message += $"Editor Res(Image): {addedImagesEditorRes.Count} ({string.Join(", ", addedImagesEditorRes)})\n";
            if (addedImagesRuntimeRes.Count > 0) message += $"Runtime Res(Image): {addedImagesRuntimeRes.Count} ({string.Join(", ", addedImagesRuntimeRes)})\n";
            if (addedResourcesEditor.Count > 0) message += $"Editor Asset: {addedResourcesEditor.Count} ({string.Join(", ", addedResourcesEditor)})\n";
            if (addedResourcesRuntime.Count > 0) message += $"Runtime Asset: {addedResourcesRuntime.Count} ({string.Join(", ", addedResourcesRuntime)})\n";
            if (addedResourcesEditorRes.Count > 0) message += $"Editor Res(Asset): {addedResourcesEditorRes.Count} ({string.Join(", ", addedResourcesEditorRes)})\n";
            if (addedResourcesRuntimeRes.Count > 0) message += $"Runtime Res(Asset): {addedResourcesRuntimeRes.Count} ({string.Join(", ", addedResourcesRuntimeRes)})\n";
            if (addedShaders.Count > 0) message += $"Shader: {addedShaders.Count} ({string.Join(", ", addedShaders)})\n";
            if (addedShadersRes.Count > 0) message += $"Shader(Res): {addedShadersRes.Count} ({string.Join(", ", addedShadersRes)})\n";

            EditorUtility.DisplayDialog("Done", message, "OK");
        }

        // ─── Feature Entry Operations ───

        private void FocusOrganizerFeature(string featureName)
        {
            if (organizerFeaturePopup == null) return;

            RefreshOrganizerFeatureOptions();

            int idx = organizerFeaturePopup.choices.IndexOf(featureName);
            if (idx < 0) return;

            organizerFeaturePopup.SetValueWithoutNotify(featureName);
            RefreshOrganizer();
        }

        private void DeleteFeature(string featureName)
        {
            if (!EditorUtility.DisplayDialog("Delete Feature", $"Delete '{featureName}'?", "Delete", "Cancel"))
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
                EditorUtility.DisplayDialog("Error", $"Delete failed:\n{e.Message}", "OK");
            }
        }

        private void DeleteDirIfExists(string dir)
        {
            if (!Directory.Exists(dir)) return;
            Directory.Delete(dir, true);
            string metaPath = dir + ".meta";
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }

        private void DeleteEntry(FeatureEntry entry)
        {
            if (!EditorUtility.DisplayDialog("Delete", $"Delete {entry.FileName}?", "Delete", "Cancel"))
                return;

            try
            {
                if (File.Exists(entry.FullPath)) File.Delete(entry.FullPath);
                string meta = entry.FullPath + ".meta";
                if (File.Exists(meta)) File.Delete(meta);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                RefreshFeatureList();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Delete failed:\n{e.Message}", "OK");
            }
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
                    string parentDir = Directory.GetParent(destDir)?.FullName;
                    string folderName = Path.GetFileName(destDir);
                    if (!string.IsNullOrEmpty(parentDir))
                        CreateFolderMeta(parentDir, folderName);
                }

                string destPath = Path.Combine(destDir, entry.FileName);

                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(entry.FullPath, destPath);

                string srcMeta = entry.FullPath + ".meta";
                string dstMeta = destPath + ".meta";

                if (File.Exists(dstMeta)) File.Delete(dstMeta);

                if (File.Exists(srcMeta))
                    File.Move(srcMeta, dstMeta);
                else
                    CreateMetaFromSourceOrDefault(destPath, destPath, DetectPendingKindFromFileName(entry.FileName));

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                RefreshFeatureList();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Move failed:\n{e.Message}", "OK");
            }
        }

        private string GetFeatureLocationPath(string featureName, FeatureLocation location)
        {
            string editorRoot = Path.Combine(currentPackagePath, "Editor");
            string runtimeRoot = Path.Combine(currentPackagePath, "Runtime");

            switch (location)
            {
                case FeatureLocation.EditorFeature: return Path.Combine(editorRoot, featureName);
                case FeatureLocation.RuntimeFeature: return Path.Combine(runtimeRoot, featureName);
                case FeatureLocation.EditorResources: return Path.Combine(editorRoot, "Resources", featureName);
                case FeatureLocation.RuntimeResources: return Path.Combine(runtimeRoot, "Resources", featureName);
                default: return Path.Combine(runtimeRoot, featureName);
            }
        }

        private List<FeatureEntry> ScanFeatureEntries(string featureName)
        {
            var results = new List<FeatureEntry>();

            string editorFeature = Path.Combine(currentPackagePath, "Editor", featureName);
            string runtimeFeature = Path.Combine(currentPackagePath, "Runtime", featureName);
            string editorRes = Path.Combine(currentPackagePath, "Editor", "Resources", featureName);
            string runtimeRes = Path.Combine(currentPackagePath, "Runtime", "Resources", featureName);

            AddEntriesFromDir(results, editorFeature, FeatureLocation.EditorFeature, "Editor", AccentBlue);
            AddEntriesFromDir(results, runtimeFeature, FeatureLocation.RuntimeFeature, "Runtime", AccentGreen);
            AddEntriesFromDir(results, editorRes, FeatureLocation.EditorResources, "E/Res", AccentPurple);
            AddEntriesFromDir(results, runtimeRes, FeatureLocation.RuntimeResources, "R/Res", AccentAmber);

            return results
                .Where(r => !r.FileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Location.ToString())
                .ThenBy(r => r.FileName)
                .ToList();
        }

        private void AddEntriesFromDir(List<FeatureEntry> list, string dir, FeatureLocation loc, string tag, Color tagColor)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
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
                    foreach (string dir in Directory.GetDirectories(editorRes))
                        set.Add(Path.GetFileName(dir));
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
                    foreach (string dir in Directory.GetDirectories(runtimeRes))
                        set.Add(Path.GetFileName(dir));
            }

            return set.OrderBy(x => x).ToList();
        }

        private int CountNonMetaFiles(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Count(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
        }

        // ─── Version & Deploy ───

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
                EditorUtility.DisplayDialog("Error", "Load a package first.", "OK");
                return;
            }

            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                EditorUtility.DisplayDialog("Error", "manifest.json not found.", "OK");
                return;
            }

            string packageName = packageNameField.value;
            string manifestContent = ReadFileShared(manifestPath);

            bool isDevMode = IsDevModeEnabled(manifestContent, packageName);

            if (isDevMode)
            {
                string gitUrl = EditorPrefs.GetString($"UPMCreator_GitUrl_{packageName}", "");
                if (string.IsNullOrEmpty(gitUrl))
                {
                    gitUrl = $"https://github.com/USERNAME/{packageName}.git";
                    int result = EditorUtility.DisplayDialogComplex(
                        "Git URL",
                        "Need Git URL for deploy mode.\nUse default?",
                        "Use Default", "Cancel", "");

                    if (result != 0) return;
                }

                manifestContent = SetPackageSource(manifestContent, packageName, gitUrl);
                WriteFileShared(manifestPath, manifestContent);
            }
            else
            {
                string localPath = $"file:{currentPackagePath.Replace("\\", "/")}";

                string currentUrl = GetCurrentPackageSource(manifestContent, packageName);
                if (!string.IsNullOrEmpty(currentUrl) && currentUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    EditorPrefs.SetString($"UPMCreator_GitUrl_{packageName}", currentUrl);

                manifestContent = SetPackageSource(manifestContent, packageName, localPath);
                WriteFileShared(manifestPath, manifestContent);
            }

            UpdateDevModeUI();

            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                EditorApplication.delayCall += () =>
                {
                    UpdateDevModeUI();
                    RefreshFeatureList();
                    RefreshOrganizerFeatureOptions();
                    Repaint();
                };
            };
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

        private void UpdateStep1Summary(string packageName)
        {
            if (step1SummaryLabel != null)
            {
                step1SummaryLabel.text = packageName;
                step1SummaryLabel.style.color = AccentGreen;
                step1SummaryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
        }

        private void UpdateDevModeUI()
        {
            if (string.IsNullOrEmpty(currentPackagePath) || devModeButton == null || devModeStatus == null) return;

            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                devModeStatus.text = "manifest.json not found";
                devModeStatus.style.color = TextMuted;
                SetBorder(devModeStatus.style, 1, Border);
                devModeButton.text = "Enable Dev Mode";
                return;
            }

            string packageName = packageNameField.value;
            string manifestContent = ReadFileShared(manifestPath);
            string source = GetCurrentPackageSource(manifestContent, packageName);
            bool isDevMode = !string.IsNullOrEmpty(source) && source.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

            if (isDevMode)
            {
                devModeStatus.text = $"DEV MODE  -  local path (editable)\n{source}";
                devModeStatus.style.color = AccentGreen;
                SetBorder(devModeStatus.style, 1, new Color(AccentGreen.r, AccentGreen.g, AccentGreen.b, 0.4f));
                devModeStatus.style.backgroundColor = new Color(AccentGreen.r, AccentGreen.g, AccentGreen.b, 0.08f);
                devModeButton.text = "Deploy Mode (git URL)";
                devModeButton.style.backgroundColor = new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.15f);
                devModeButton.style.color = AccentAmber;
                SetBorder(devModeButton.style, 1, new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.3f));
            }
            else
            {
                devModeStatus.text = $"DEPLOY MODE  -  git URL (read-only)\n{(string.IsNullOrEmpty(source) ? "(not registered)" : source)}";
                devModeStatus.style.color = AccentAmber;
                SetBorder(devModeStatus.style, 1, new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.4f));
                devModeStatus.style.backgroundColor = new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.08f);
                devModeButton.text = "Dev Mode (local path)";
                devModeButton.style.backgroundColor = new Color(AccentGreen.r, AccentGreen.g, AccentGreen.b, 0.15f);
                devModeButton.style.color = AccentGreen;
                SetBorder(devModeButton.style, 1, new Color(AccentGreen.r, AccentGreen.g, AccentGreen.b, 0.3f));
            }
        }

        // ─── File System Helpers ───

        private void EnsureMetaFiles(string packagePath)
        {
            string[] filesToCheck = { "README.md", "LICENSE.md", "package.json", "CHANGELOG.md" };
            foreach (string fileName in filesToCheck)
            {
                string filePath = Path.Combine(packagePath, fileName);
                if (File.Exists(filePath) && !File.Exists(filePath + ".meta"))
                    CreateFileMeta(filePath);
            }

            string[] foldersToCheck = { "Editor", "Runtime" };
            foreach (string folderName in foldersToCheck)
            {
                string folderPath = Path.Combine(packagePath, folderName);
                if (Directory.Exists(folderPath) && !File.Exists(folderPath + ".meta"))
                    CreateFolderMeta(packagePath, folderName);
            }
        }

        private void SavePackageJson()
        {
            if (string.IsNullOrEmpty(currentPackagePath))
            {
                EditorUtility.DisplayDialog("Error", "Complete Step 1 first.", "OK");
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

            EditorUtility.DisplayDialog("Saved", $"v{versionField.value} saved.\n\nCommit & Push via GitHub Desktop.", "OK");
        }

        private void CreateReadme(string root)
        {
            string path = Path.Combine(root, "README.md");
            File.WriteAllText(path, $"# {displayNameField.value}\n\n{descriptionField.value}\n\n## Installation\n\nPackage Manager > Add package from git URL\n\n## License\n\nMIT License\n");
            CreateFileMeta(path);
        }

        private void CreateLicense(string root)
        {
            string path = Path.Combine(root, "LICENSE.md");
            File.WriteAllText(path, $"MIT License\n\nCopyright (c) {DateTime.Now.Year} {authorField.value}\n\nPermission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\nThe above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\nTHE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.\n");
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
            }
            CreateEditorAsmdef(editorPath);
        }

        private void EnsureRuntimeRootIfNeeded()
        {
            string runtimePath = Path.Combine(currentPackagePath, "Runtime");
            if (!Directory.Exists(runtimePath))
            {
                Directory.CreateDirectory(runtimePath);
                CreateFolderMeta(currentPackagePath, "Runtime");
            }
            CreateRuntimeAsmdef(runtimePath);

            string editorPath = Path.Combine(currentPackagePath, "Editor");
            if (Directory.Exists(editorPath))
                UpdateEditorAsmdefWithRuntimeReference(editorPath);
        }

        private void CreateEditorAsmdef(string editorPath)
        {
            string asmName = ConvertToAsmdefName(packageNameField.value) + ".Editor";
            string path = Path.Combine(editorPath, asmName + ".asmdef");
            if (File.Exists(path)) return;

            string runtimePath = Path.Combine(currentPackagePath, "Runtime");
            bool hasRuntime = Directory.Exists(runtimePath);
            string references = hasRuntime ? $"\"{ConvertToAsmdefName(packageNameField.value)}\"" : "";

            File.WriteAllText(path, $"{{\n  \"name\": \"{asmName}\",\n  \"rootNamespace\": \"{ConvertToNamespace(packageNameField.value)}\",\n  \"includePlatforms\": [\"Editor\"],\n  \"excludePlatforms\": [],\n  \"references\": [{references}]\n}}");
            CreateAsmdefMeta(path);
        }

        private void CreateRuntimeAsmdef(string runtimePath)
        {
            string asmName = ConvertToAsmdefName(packageNameField.value);
            string path = Path.Combine(runtimePath, asmName + ".asmdef");
            if (File.Exists(path)) return;

            File.WriteAllText(path, $"{{\n  \"name\": \"{asmName}\",\n  \"rootNamespace\": \"{ConvertToNamespace(packageNameField.value)}\",\n  \"includePlatforms\": [],\n  \"excludePlatforms\": [],\n  \"references\": []\n}}");
            CreateAsmdefMeta(path);
        }

        private void UpdateEditorAsmdefWithRuntimeReference(string editorPath)
        {
            string asmName = ConvertToAsmdefName(packageNameField.value) + ".Editor";
            string path = Path.Combine(editorPath, asmName + ".asmdef");
            if (!File.Exists(path)) return;

            string runtimeAsmName = ConvertToAsmdefName(packageNameField.value);
            File.WriteAllText(path, $"{{\n  \"name\": \"{asmName}\",\n  \"rootNamespace\": \"{ConvertToNamespace(packageNameField.value)}\",\n  \"includePlatforms\": [\"Editor\"],\n  \"excludePlatforms\": [],\n  \"references\": [\"{runtimeAsmName}\"]\n}}");

            if (!File.Exists(path + ".meta")) CreateAsmdefMeta(path);
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

        private string ConvertToNamespace(string packageName) => ConvertToAsmdefName(packageName);

        // ─── Meta Generators ───

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
            else if (kind == PendingKind.Shader) CreateShaderMeta(destFilePath);
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
                        lines.Add(line);
                }

                if (!replaced) lines.Insert(1, "guid: " + newGuid);
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

        private void CreateShaderMeta(string filePath)
        {
            string guid = GUID.Generate().ToString().Replace("-", "");
            File.WriteAllText(filePath + ".meta", $"fileFormatVersion: 2\nguid: {guid}\nShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
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

        // ─── Detection Helpers ───

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

        private bool IsShaderAsset(string assetPath)
        {
            string ext = Path.GetExtension(assetPath)?.ToLowerInvariant();
            return ext == ".shader" || ext == ".shadergraph" || ext == ".shadersubgraph" || ext == ".hlsl" || ext == ".cginc" || ext == ".compute";
        }

        private bool IsResourceAsset(string assetPath)
        {
            string ext = Path.GetExtension(assetPath)?.ToLowerInvariant();
            return ext == ".mat" || ext == ".asset" || ext == ".preset" || ext == ".physicmaterial"
                || ext == ".physicsmaterial" || ext == ".cubemap" || ext == ".flare"
                || ext == ".renderTexture" || ext == ".lighting" || ext == ".guiskin"
                || ext == ".fontsettings" || ext == ".mixer" || ext == ".controller"
                || ext == ".overrideController" || ext == ".anim" || ext == ".mask"
                || ext == ".signal" || ext == ".playable" || ext == ".brush";
        }

        private string SafeReadAllText(string fullPath)
        {
            try { return File.ReadAllText(fullPath); }
            catch { return ""; }
        }

        private string ReadFileShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                return sr.ReadToEnd();
        }

        private void WriteFileShared(string path, string content)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var sw = new StreamWriter(fs))
                sw.Write(content);
        }

        private PendingKind DetectPendingKindFromFileName(string fileName)
        {
            if (fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return PendingKind.Script;
            if (IsShaderAsset(fileName)) return PendingKind.Shader;
            if (IsImageAsset(fileName)) return PendingKind.Image;
            return PendingKind.Resource;
        }

        private string NormalizeFeatureName(string value)
        {
            if (value == null) return "";
            return value.Trim().Replace(" ", "");
        }

        // ─── Data Classes ───

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
