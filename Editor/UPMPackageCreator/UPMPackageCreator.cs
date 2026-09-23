using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TelleR
{
    public class UPMPackageCreator : EditorWindow
    {
        private const string PREF_LAST_PATH = "UPMCreator_LastPath";
        private const string LOG = "[TelleR/UPMPackageCreator]";
        private const string PackageNamePlaceholder = "com.company.package";
        private const string GitUrlPlaceholder = "https://github.com/USER/Repo.git";

        private const string PackageNameRuleText =
            "패키지 이름은 소문자 역도메인 형식이어야 합니다. (예: com.company.package)\n" +
            "소문자·숫자·'-'·'_'만 쓰고, 점(.)으로 나뉜 부분이 2개 이상이어야 합니다.";

        private static readonly Regex PackageNameRegex = new Regex(@"^[a-z0-9][a-z0-9_-]*(\.[a-z0-9_-]+)+$");
        private static readonly Regex SemVerRegex = new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$");
        private static readonly Regex UnityVersionRegex = new Regex(@"^\d{4}\.\d+$");
        private static readonly Regex GuidTokenRegex = new Regex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{32}(?![0-9A-Fa-f])");
        private static readonly Regex YamlGuidRefRegex = new Regex(@"guid: ([0-9a-f]{32})");
        private static readonly Regex MetaGuidRegex = new Regex(@"^guid:\s*([0-9A-Fa-f]{32})", RegexOptions.Multiline);

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
        private Label packageNameError;

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
        private TextField gitUrlField;

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

        private IVisualElementScheduledItem skinWatch;
        private bool builtForProSkin;

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

        // 버튼 hover/pressed 표시용 상태 — 인라인 배경색은 USS :hover/:active를 덮어쓰므로 콜백으로 직접 처리한다
        private sealed class HoverState
        {
            public Color Base;
            public bool Hovered;
            public bool Pressed;
        }

        // ─── Design Tokens (Dark / Light 두 팔레트 — 에디터 스킨에 따라 선택) ───
        static bool Pro => EditorGUIUtility.isProSkin;
        static Color P(Color dark, Color light) => Pro ? dark : light;

        static Color BgDeep        => P(new Color(0.16f, 0.16f, 0.18f), new Color(0.78f, 0.78f, 0.80f));
        static Color BgCard        => P(new Color(0.21f, 0.21f, 0.24f), new Color(0.89f, 0.89f, 0.91f));
        static Color BgCardHover   => P(new Color(0.24f, 0.24f, 0.28f), new Color(0.83f, 0.83f, 0.86f));
        static Color BgInput       => P(new Color(0.18f, 0.18f, 0.21f), new Color(0.95f, 0.95f, 0.96f));
        static Color BgInset       => P(new Color(0.10f, 0.105f, 0.13f), new Color(0.83f, 0.83f, 0.85f));
        static Color BgHeader      => P(new Color(0.10f, 0.105f, 0.13f), new Color(0.76f, 0.76f, 0.79f));
        static Color Border        => P(new Color(0.30f, 0.30f, 0.35f), new Color(0.64f, 0.64f, 0.68f));
        static Color BorderLight   => P(new Color(0.36f, 0.36f, 0.42f), new Color(0.56f, 0.56f, 0.60f));
        static Color TextPrimary   => P(new Color(0.95f, 0.95f, 0.97f), new Color(0.08f, 0.08f, 0.10f));
        static Color TextSecondary => P(new Color(0.70f, 0.70f, 0.76f), new Color(0.22f, 0.22f, 0.26f));
        static Color TextMuted     => P(new Color(0.52f, 0.52f, 0.58f), new Color(0.36f, 0.36f, 0.40f));
        static Color AccentBlue    => P(new Color(0.40f, 0.60f, 1.0f), new Color(0.12f, 0.36f, 0.80f));
        static Color AccentGreen   => P(new Color(0.35f, 0.85f, 0.55f), new Color(0.05f, 0.50f, 0.20f));
        static Color AccentRed     => P(new Color(0.95f, 0.40f, 0.40f), new Color(0.75f, 0.12f, 0.12f));
        static Color AccentAmber   => P(new Color(1.0f, 0.80f, 0.30f), new Color(0.62f, 0.40f, 0.00f));
        static Color AccentPurple  => P(new Color(0.70f, 0.50f, 1.0f), new Color(0.45f, 0.22f, 0.80f));
        static Color AccentCyan    => P(new Color(0.35f, 0.88f, 0.90f), new Color(0.00f, 0.45f, 0.50f));
        static Color HelpText      => P(new Color(0.65f, 0.78f, 1.0f), new Color(0.10f, 0.28f, 0.62f));
        const int RadiusLg = 8;
        const int RadiusMd = 6;
        const int RadiusSm = 4;
        const int RadiusXs = 3;

        [MenuItem("Tools/TelleR/UPM Package Creator", false, 141)]
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
            builtForProSkin = Pro;
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
            RefreshPendingList();

            // 패키지가 없으면 1단계를 펼쳐 첫 사용자가 바로 할 일을 보게 한다
            SetStep1Collapsed(HasPackage);

            // 창이 열린 채로 에디터 스킨이 바뀌면 팔레트를 다시 적용한다
            if (skinWatch == null)
                skinWatch = root.schedule.Execute(() =>
                {
                    if (Pro != builtForProSkin) CreateGUI();
                }).Every(1000);
        }

        private bool HasPackage => !string.IsNullOrEmpty(currentPackagePath) && Directory.Exists(currentPackagePath);

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

        static Color HoverColor(Color c)
        {
            if (c.a < 0.05f) return Pro ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.07f);
            Color mixed = Color.Lerp(c, Pro ? Color.white : Color.black, 0.12f);
            mixed.a = Mathf.Min(1f, c.a + 0.10f);
            return mixed;
        }

        // Unity 기본 버튼처럼 누른 동안은 기본색보다 어둡게 표시한다
        static Color PressedColor(Color c)
        {
            if (c.a < 0.05f) return Pro ? new Color(0f, 0f, 0f, 0.22f) : new Color(0f, 0f, 0f, 0.14f);
            Color mixed = Color.Lerp(c, Color.black, 0.18f);
            mixed.a = Mathf.Min(1f, c.a + 0.20f);
            return mixed;
        }

        static Color StateColor(HoverState hs)
        {
            if (hs.Pressed) return PressedColor(hs.Base);
            return hs.Hovered ? HoverColor(hs.Base) : hs.Base;
        }

        // 현재 인라인 배경색을 기준으로 hover/pressed 강조를 붙인다. 이후 배경색 변경은 SetBg로 한다.
        void AddHover(VisualElement el)
        {
            var hs = new HoverState { Base = el.style.backgroundColor.value };
            el.userData = hs;
            el.RegisterCallback<MouseEnterEvent>(_ =>
            {
                hs.Hovered = true;
                if (el.enabledInHierarchy) el.style.backgroundColor = StateColor(hs);
            });
            el.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                hs.Hovered = false;
                hs.Pressed = false;
                el.style.backgroundColor = hs.Base;
            });
            // Button의 Clickable은 down 이벤트를 StopImmediatePropagation하므로 TrickleDown으로 먼저 받는다
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !el.enabledInHierarchy) return;
                hs.Pressed = true;
                el.style.backgroundColor = StateColor(hs);
            }, TrickleDown.TrickleDown);
            el.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || !hs.Pressed) return;
                hs.Pressed = false;
                el.style.backgroundColor = StateColor(hs);
            }, TrickleDown.TrickleDown);
            el.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!hs.Pressed) return;
                hs.Pressed = false;
                el.style.backgroundColor = StateColor(hs);
            });
        }

        void SetBg(VisualElement el, Color c)
        {
            if (el.userData is HoverState hs)
            {
                hs.Base = c;
                el.style.backgroundColor = StateColor(hs);
            }
            else el.style.backgroundColor = c;
        }

        // UI Toolkit placeholder는 2023.1+ 전용이라 라벨을 겹쳐 직접 구현한다
        void AddPlaceholder(TextField field, string text)
        {
            VisualElement host = field.Q("unity-text-input") ?? (VisualElement)field;
            var ph = new Label(text);
            ph.pickingMode = PickingMode.Ignore;
            ph.style.position = Position.Absolute;
            ph.style.left = 4;
            ph.style.right = 0;
            ph.style.top = 0;
            ph.style.bottom = 0;
            ph.style.unityTextAlign = TextAnchor.MiddleLeft;
            ph.style.unityFontStyleAndWeight = FontStyle.Italic;
            ph.style.color = TextMuted;
            host.Add(ph);

            void Sync() => ph.style.display = string.IsNullOrEmpty(field.value) ? DisplayStyle.Flex : DisplayStyle.None;
            field.RegisterValueChangedCallback(_ => Sync());
            field.RegisterCallback<AttachToPanelEvent>(_ => Sync());
            Sync();
        }

        VisualElement CreateCard(VisualElement parent, string stepNum, string title,
            bool collapsible = false, bool startCollapsed = false,
            System.Action<VisualElement, Label, Label> onHeaderCreated = null,
            System.Action<bool> onToggled = null)
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
            header.style.backgroundColor = BgHeader;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = Border;
            card.Add(header);

            Label arrow = null;
            if (collapsible)
            {
                arrow = new Label(startCollapsed ? "▶" : "▼");
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

                AddHover(header);
                header.RegisterCallback<ClickEvent>(evt =>
                {
                    bool isHidden = body.style.display == DisplayStyle.None;
                    body.style.display = isHidden ? DisplayStyle.Flex : DisplayStyle.None;
                    if (arrow != null)
                        arrow.text = isHidden ? "▼" : "▶";
                    onToggled?.Invoke(!isHidden);
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
            AddHover(btn);
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
            AddHover(btn);
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
            AddHover(btn);
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
            AddHover(btn);
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
            AddHover(btn);
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

        VisualElement CreateHelpLabel(string text)
        {
            var container = new VisualElement();
            container.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.08f);
            SetRadius(container.style, RadiusSm);
            SetPadding(container.style, 8, 12);
            container.style.marginBottom = 12;
            SetBorder(container.style, 1, new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.15f));

            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = HelpText;
            label.style.whiteSpace = WhiteSpace.Normal;
            container.Add(label);

            return container;
        }

        // ─── Step 1 ───

        private void CreateStep1(VisualElement root)
        {
            step1Container = CreateCard(root, "1", "Package Setup",
                collapsible: true, startCollapsed: step1Collapsed,
                onHeaderCreated: (header, arrow, summary) =>
                {
                    step1Arrow = arrow;
                    step1SummaryLabel = summary;
                },
                onToggled: collapsed => step1Collapsed = collapsed);
            step1Body = step1Container;

            // 기본값은 비워 두고 placeholder로 형식만 안내 — 제작자 본인 패키지 값이 기본으로 들어가면 이름이 충돌한다
            packageNameField = CreateStyledField(step1Container, "Package Name", "");
            packageNameField.tooltip = PackageNameRuleText;
            AddPlaceholder(packageNameField, PackageNamePlaceholder);

            packageNameError = new Label("");
            packageNameError.style.color = AccentRed;
            packageNameError.style.fontSize = 11;
            packageNameError.style.marginLeft = 120;
            packageNameError.style.marginTop = -4;
            packageNameError.style.marginBottom = 8;
            packageNameError.style.whiteSpace = WhiteSpace.Normal;
            packageNameError.style.display = DisplayStyle.None;
            step1Container.Add(packageNameError);
            packageNameField.RegisterValueChangedCallback(e => UpdatePackageNameError(e.newValue));

            displayNameField = CreateStyledField(step1Container, "Display Name", "");
            AddPlaceholder(displayNameField, "My Package");
            descriptionField = CreateStyledField(step1Container, "Description", "");
            AddPlaceholder(descriptionField, "패키지 설명");
            authorField = CreateStyledField(step1Container, "Author", "");
            AddPlaceholder(authorField, "작성자 이름");
            unityVersionField = CreateStyledField(step1Container, "Min Unity", "2021.3");
            unityVersionField.tooltip = "지원하는 최소 Unity 버전 (예: 2021.3). 비워 두면 지정하지 않습니다.";
            AddPlaceholder(unityVersionField, "2021.3");

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

            step1Status = new Label("새 패키지를 만들거나 기존 패키지 폴더를 불러오세요.");
            step1Status.style.marginTop = 10;
            step1Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            step1Status.style.fontSize = 12;
            step1Status.style.color = TextMuted;
            step1Status.style.whiteSpace = WhiteSpace.Normal;
            step1Container.Add(step1Status);
        }

        private void SetStep1Collapsed(bool collapsed)
        {
            step1Collapsed = collapsed;
            if (step1Body != null) step1Body.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (step1Arrow != null) step1Arrow.text = collapsed ? "▶" : "▼";
        }

        private void UpdatePackageNameError(string value)
        {
            if (packageNameError == null) return;
            string v = (value ?? "").Trim();
            bool show = v.Length > 0 && !IsValidPackageName(v);
            packageNameError.text = show ? PackageNameRuleText : "";
            packageNameError.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ─── Step 2 ───

        private void CreateStep2(VisualElement root)
        {
            step2Container = CreateCard(root, "2", "Feature Manager");

            step2TabsRow = new VisualElement();
            step2TabsRow.style.flexDirection = FlexDirection.Row;
            step2TabsRow.style.alignItems = Align.Center;
            step2TabsRow.style.marginBottom = 14;
            step2TabsRow.style.backgroundColor = BgInset;
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
            AddHover(btn);
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
            SetBg(button, selected ? new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.20f) : Color.clear);
            button.style.color = selected ? AccentBlue : TextMuted;
        }

        // ─── Step 2: Add Tab ───

        private void CreateStep2AddUI(VisualElement parent)
        {
            var desc = CreateHelpLabel("ℹ  파일을 아래 영역에 드롭하고, 기능 이름을 입력한 뒤 Confirm & Add를 누르세요.\n같은 이름의 파일이 패키지에 있으면 덮어쓸지 먼저 묻습니다.");
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
            AddPlaceholder(newFeatureNameField, "AudioVolume3D");

            parent.Add(featureRow);

            // Drop zone
            dropZone = new VisualElement();
            dropZone.style.minHeight = 130;
            dropZone.style.backgroundColor = BgInset;
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

            dropLabel = new Label("스크립트, 머티리얼, 프리팹, 셰이더 등 파일을 여기에 드롭하세요");
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.color = TextSecondary;
            dropLabel.style.fontSize = 13;
            dropLabel.style.whiteSpace = WhiteSpace.Normal;
            dropZone.Add(dropLabel);

            var dropHint = new Label(".cs  /  Image  /  Material  /  Prefab  /  Shader  /  Audio  /  Any asset");
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
            SetBg(confirmButton, AccentGreen);
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

            step2Status = new Label("먼저 1단계에서 패키지를 만들거나 불러오세요.");
            step2Status.style.marginTop = 10;
            step2Status.style.color = TextMuted;
            step2Status.style.fontSize = 11;
            step2Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            parent.Add(step2Status);
        }

        // ─── Step 2: List Tab ───

        private void CreateStep2ListUI(VisualElement parent)
        {
            var desc = CreateHelpLabel("ℹ  패키지에 등록된 기능 폴더 목록입니다. Organize로 파일 위치를 바꿀 수 있습니다.");
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

            var desc = CreateHelpLabel("ℹ  파일을 Editor(에디터 전용) 또는 Runtime(빌드 포함) 폴더로 이동합니다.");
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
            organizerScroll.style.backgroundColor = BgInset;
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
            versionField.tooltip = "MAJOR.MINOR.PATCH 형식 (예: 1.2.0, 1.2.0-preview.1). 버전 올리기 버튼은 -preview 같은 접미사를 유지합니다.";
            versionField.style.width = 110;
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

            var saveBtn = CreatePrimaryButton("Save package.json", () => TrySavePackageJson(true));
            step3Container.Add(saveBtn);

            step3Container.Add(CreateDivider());

            var devModeLabel = new Label("Package Mode");
            devModeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            devModeLabel.style.color = TextPrimary;
            devModeLabel.style.fontSize = 13;
            devModeLabel.style.marginBottom = 4;
            step3Container.Add(devModeLabel);

            var devModeDesc = CreateHelpLabel(
                "ℹ  이 스위치는 현재 프로젝트의 Packages/manifest.json에서 이 패키지의 설치 소스를 바꿉니다.\n" +
                "Dev = file:로컬 폴더 (수정 즉시 반영)  /  Deploy = git URL (푸시된 커밋 사용)\n" +
                "Dev 상태의 manifest.json은 이 PC 폴더 구조에 묶이므로 커밋 전에 Deploy로 되돌리세요.");
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

            var gitUrlRow = new VisualElement();
            gitUrlRow.style.flexDirection = FlexDirection.Row;
            gitUrlRow.style.alignItems = Align.Center;
            gitUrlRow.style.marginBottom = 10;

            var gitUrlLabel = new Label("Git URL");
            gitUrlLabel.style.width = 60;
            gitUrlLabel.style.color = TextSecondary;
            gitUrlLabel.style.fontSize = 13;
            gitUrlRow.Add(gitUrlLabel);

            gitUrlField = new TextField();
            gitUrlField.style.flexGrow = 1;
            gitUrlField.style.height = 30;
            gitUrlField.style.minHeight = 30;
            gitUrlField.tooltip = "Deploy Mode 전환에 사용할 git 저장소 URL (https://…/Repo.git, git@host:user/Repo.git, ssh://…)";
            gitUrlRow.Add(gitUrlField);
            AddPlaceholder(gitUrlField, GitUrlPlaceholder);

            step3Container.Add(gitUrlRow);

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
            DragAndDrop.AcceptDrag();

            if (!HasPackage)
            {
                TelleRGUI.Info("패키지 없음", "먼저 1단계에서 패키지를 만들거나 불러오세요.");
                return;
            }

            var skipped = new List<string>();
            var sources = CollectDroppedFiles(skipped);

            if (sources.Count == 0 && skipped.Count == 0)
            {
                TelleRGUI.Info("드롭 항목 없음", "추가할 파일을 찾지 못했습니다.\nProject 창의 에셋이나 탐색기의 파일을 드롭하세요.");
                return;
            }

            foreach (string fullPath in sources)
            {
                if (pendingItems.Any(p => SamePath(p.SourceFullPath, fullPath))) continue;

                string ext = (Path.GetExtension(fullPath) ?? "").ToLowerInvariant();
                bool underEditorFolder = IsUnderEditorFolder(fullPath);

                if (ext == ".cs")
                {
                    string content = SafeReadAllText(fullPath);
                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Script,
                        Placement = PendingPlacement.Auto,
                        UseResources = false,
                        DetectedEditor = underEditorFolder || IsEditorScript(content)
                    });
                }
                else if (IsShaderAsset(fullPath))
                {
                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Shader,
                        Placement = underEditorFolder ? PendingPlacement.Editor : PendingPlacement.Runtime,
                        UseResources = false,
                        DetectedEditor = false
                    });
                }
                else if (IsImageAsset(fullPath))
                {
                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Image,
                        Placement = underEditorFolder ? PendingPlacement.Editor : PendingPlacement.Runtime,
                        UseResources = true,
                        DetectedEditor = false
                    });
                }
                else
                {
                    // 알려진 리소스 타입은 기존처럼 Resources 기본, 그 외 에셋(프리팹·씬·오디오·모델 등)은 기능 폴더 기본
                    pendingItems.Add(new PendingItem
                    {
                        SourceFullPath = fullPath,
                        Kind = PendingKind.Resource,
                        Placement = underEditorFolder ? PendingPlacement.Editor : PendingPlacement.Runtime,
                        UseResources = IsResourceAsset(fullPath),
                        DetectedEditor = false
                    });
                }
            }

            if (skipped.Count > 0)
            {
                TelleRGUI.Info("일부 항목 제외",
                    "다음 항목은 대기 목록에 넣지 않았습니다:\n" + FormatList(skipped, 15));
            }

            RefreshPendingList();
        }

        // Project 창 에셋(objectReferences/paths)과 탐색기 파일(paths)을 모두 실제 파일 경로로 모은다
        private List<string> CollectDroppedFiles(List<string> skipped)
        {
            var result = new List<string>();
            var candidates = new List<string>();
            if (DragAndDrop.paths != null) candidates.AddRange(DragAndDrop.paths);
            if (DragAndDrop.objectReferences != null)
            {
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    string p = obj != null ? AssetDatabase.GetAssetPath(obj) : null;
                    if (!string.IsNullOrEmpty(p)) candidates.Add(p);
                }
            }

            foreach (string raw in candidates)
            {
                string full = ToFullPath(raw);
                if (string.IsNullOrEmpty(full)) continue;
                string name = Path.GetFileName(full);

                if (Directory.Exists(full))
                {
                    AddUnique(skipped, name + " (폴더는 지원하지 않음 — 안의 파일을 선택해 드롭하세요)");
                    continue;
                }
                if (!File.Exists(full)) continue;

                string ext = (Path.GetExtension(full) ?? "").ToLowerInvariant();
                if (ext == ".meta")
                {
                    AddUnique(skipped, name + " (.meta는 자동 생성됨)");
                    continue;
                }
                if (ext == ".asmdef" || ext == ".asmref")
                {
                    AddUnique(skipped, name + " (어셈블리 정의는 이 도구가 관리 — 복사하면 이름 중복 컴파일 오류가 남)");
                    continue;
                }
                if (IsInsidePackage(full))
                {
                    AddUnique(skipped, name + " (이미 이 패키지 안의 파일)");
                    continue;
                }

                if (!result.Any(r => SamePath(r, full))) result.Add(full);
            }
            return result;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (!list.Contains(value)) list.Add(value);
        }

        private static string FormatList(List<string> items, int max)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < max; i++)
                sb.Append("- ").Append(items[i]).Append('\n');
            if (items.Count > max) sb.Append($"… 외 {items.Count - max}개\n");
            return sb.ToString().TrimEnd('\n');
        }

        // 프로젝트 상대 경로(Assets/…, Packages/…)나 절대 경로를 디스크 경로로 바꾼다
        private static string ToFullPath(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                if (Path.IsPathRooted(raw)) return Path.GetFullPath(raw);

                string norm = raw.Replace('\\', '/');
                if (norm.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    // Packages/ 아래 에셋은 실제 위치가 Library/PackageCache나 외부 폴더일 수 있다
                    var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(norm);
                    if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                    {
                        string prefix = "Packages/" + info.name;
                        string rel = norm.Length > prefix.Length ? norm.Substring(prefix.Length).TrimStart('/') : "";
                        return Path.GetFullPath(Path.Combine(info.resolvedPath, rel));
                    }
                }
                return Path.GetFullPath(norm);
            }
            catch
            {
                return null;
            }
        }

        // 절대 경로 전체를 보면 프로젝트가 D:/Work/Editor/MyGame 같은 곳에 있을 때 모든 파일이 에디터용으로 잡힌다.
        // 파일을 담은 유니티 프로젝트(Assets+ProjectSettings) 또는 패키지(package.json+.meta) 루트 아래 부분만 본다.
        // 어느 루트에도 속하지 않은 낱개 파일은 바로 위 폴더 이름만 본다 (스크립트는 내용으로도 판별됨).
        internal static bool IsUnderEditorFolder(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return false;
            string p = fullPath.Replace('\\', '/');
            string root = FindContainingRoot(p);
            if (root == null || !p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetFileName((Path.GetDirectoryName(p) ?? "").Replace('\\', '/').TrimEnd('/'));
                return string.Equals(parent, "Editor", StringComparison.OrdinalIgnoreCase);
            }
            string rel = p.Substring(root.Length);
            return rel.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 파일에서 위로 올라가며 가장 가까운 유니티 프로젝트 루트를, 없으면 가장 가까운 패키지 루트를 찾는다
        private static string FindContainingRoot(string filePath)
        {
            string packageRoot = null;
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                while (!string.IsNullOrEmpty(dir))
                {
                    string norm = dir.Replace('\\', '/').TrimEnd('/');
                    if (Directory.Exists(Path.Combine(dir, "Assets")) && Directory.Exists(Path.Combine(dir, "ProjectSettings")))
                        return norm;
                    // package.json.meta까지 있어야 유니티 패키지 — 상위의 Node용 package.json은 무시
                    if (packageRoot == null && File.Exists(Path.Combine(dir, "package.json")) && File.Exists(Path.Combine(dir, "package.json.meta")))
                        packageRoot = norm;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch
            {
                // 접근 불가 폴더 등 — 찾은 데까지만 사용
            }
            return packageRoot;
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\' || Application.platform == RuntimePlatform.OSXEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static string NormalizeDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(NormalizeDir(a), NormalizeDir(b), PathComparison);
        }

        private bool IsInsidePackage(string fullPath)
        {
            if (string.IsNullOrEmpty(currentPackagePath)) return false;
            string root = NormalizeDir(currentPackagePath) + "/";
            return NormalizeDir(fullPath).StartsWith(root, PathComparison);
        }

        private string PackageRelative(string fullPath)
        {
            if (string.IsNullOrEmpty(currentPackagePath)) return fullPath;
            string root = NormalizeDir(currentPackagePath) + "/";
            string p = NormalizeDir(fullPath);
            return p.StartsWith(root, PathComparison) ? p.Substring(root.Length) : p;
        }

        private void ResetDropZoneStyle()
        {
            SetBorder(dropZone.style, 2, Border);
            dropZone.style.backgroundColor = BgInset;
        }

        // ─── Pending List UI ───

        private void RefreshPendingList()
        {
            if (pendingListContainer == null || confirmButton == null) return;
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
                row.style.backgroundColor = BgInset;
                SetRadius(row.style, RadiusSm);
                SetBorder(row.style, 1, Border);
                SetPadding(row.style, 6, 10);
                row.tooltip = item.SourceFullPath;

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
                    hint.tooltip = item.DetectedEditor ? "Auto: 에디터 스크립트로 감지됨" : "Auto: 런타임 스크립트로 감지됨";
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
                    resourcesToggle.tooltip = "켜면 Resources/<기능> 폴더에 넣어 Resources.Load로 불러올 수 있습니다.";
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
                TelleRGUI.Info("기능 이름 필요",
                    "Feature 칸에 올바른 기능 이름을 입력하세요. (예: AudioVolume3D)\n경로 문자, '..', Resources/Editor/Runtime은 사용할 수 없습니다.");
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
                var empty = new Label("아직 기능이 없습니다. Add 탭에서 파일을 추가하세요.");
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
                card.style.backgroundColor = BgInset;
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
                AddOrganizerEmpty("먼저 1단계에서 패키지를 만들거나 불러오세요.");
                return;
            }

            var features = GetAllFeatureNames();
            if (features.Count == 0)
            {
                AddOrganizerEmpty("기능이 없습니다. 먼저 파일을 추가하세요.");
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
                AddOrganizerEmpty("이 기능에는 파일이 없습니다.");
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
            row.tooltip = PackageRelative(entry.FullPath);

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
            bool hasPackage = HasPackage;

            step2Container.SetEnabled(hasPackage);
            step3Container.SetEnabled(hasPackage);

            if (hasPackage)
            {
                step2Status.text = "";
                step2Status.style.display = DisplayStyle.None;
            }
            else
            {
                step2Status.text = "먼저 1단계에서 패키지를 만들거나 불러오세요.";
                step2Status.style.display = DisplayStyle.Flex;
            }
        }

        private void TryLoadLastPackage()
        {
            string lastPath = EditorPrefs.GetString(PREF_LAST_PATH, "");
            if (!string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath) && File.Exists(Path.Combine(lastPath, "package.json")))
            {
                LoadPackageFromPath(lastPath);
            }
        }

        // ─── Package Operations ───

        private void LoadExistingPackage()
        {
            string folderPath = EditorUtility.OpenFolderPanel("불러올 패키지 폴더 선택", "", "");
            if (string.IsNullOrEmpty(folderPath)) return;

            string packageJsonPath = Path.Combine(folderPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                TelleRGUI.Info("package.json 없음", "선택한 폴더에 package.json이 없습니다.\n패키지 루트 폴더를 선택하세요.");
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
                var fields = UPMJson.ReadPackageFields(json);

                packageNameField.value = fields.Name ?? "";
                displayNameField.value = fields.DisplayName ?? "";
                versionField.value = fields.Version ?? "1.0.0";
                descriptionField.value = fields.Description ?? "";
                unityVersionField.value = fields.Unity ?? "";
                authorField.value = fields.Author ?? "";

                currentPackagePath = folderPath;
                EditorPrefs.SetString(PREF_LAST_PATH, folderPath);

                step1Status.text = $"불러옴: {Path.GetFileName(folderPath)}";
                step1Status.style.color = AccentGreen;
                UpdateStep1Summary(string.IsNullOrEmpty(fields.Name) ? Path.GetFileName(folderPath) : fields.Name);

                UpdateStepStates();
                RefreshFeatureList();
                UpdateDevModeUI();
                EnsureMetaFiles(folderPath);
                RefreshOrganizerFeatureOptions();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                TelleRGUI.Info("불러오기 실패", $"package.json을 읽지 못했습니다:\n{packageJsonPath}\n\n{e.Message}");
            }
        }

        private void CreateNewPackage()
        {
            string packageName = (packageNameField.value ?? "").Trim();
            if (!IsValidPackageName(packageName))
            {
                UpdatePackageNameError(packageName.Length == 0 ? "!" : packageName);
                TelleRGUI.Info("패키지 이름 오류",
                    (packageName.Length == 0 ? "Package Name을 입력하세요.\n\n" : $"'{packageName}'은(는) 쓸 수 없는 이름입니다.\n\n") + PackageNameRuleText);
                SetStep1Collapsed(false);
                return;
            }
            packageNameField.value = packageName;
            if (string.IsNullOrWhiteSpace(displayNameField.value)) displayNameField.value = packageName;

            // 폴더를 만들기 전에 저장될 값을 먼저 검증해 반쯤 만들어진 패키지가 남지 않게 한다
            if (!ValidateSaveFields(out string fieldError))
            {
                TelleRGUI.Info("입력값 오류", fieldError);
                return;
            }

            string folderPath = EditorUtility.SaveFolderPanel("새 패키지를 만들 상위 폴더 선택", "", "");
            if (string.IsNullOrEmpty(folderPath)) return;

            string packageRoot = Path.Combine(folderPath, packageName);

            if (Directory.Exists(packageRoot))
            {
                if (File.Exists(Path.Combine(packageRoot, "package.json")))
                {
                    if (TelleRGUI.Confirm("이미 있는 패키지", $"'{packageRoot}'에 이미 패키지가 있습니다.\n이 패키지를 불러올까요?", "불러오기", "취소"))
                        LoadPackageFromPath(packageRoot);
                }
                else
                {
                    TelleRGUI.Info("폴더가 이미 있음", $"같은 이름의 폴더가 있지만 package.json이 없습니다:\n{packageRoot}\n\n다른 위치를 고르거나 폴더를 정리한 뒤 다시 시도하세요.");
                }
                return;
            }

            string assetsDir = NormalizeDir(Application.dataPath) + "/";
            if ((NormalizeDir(packageRoot) + "/").StartsWith(assetsDir, PathComparison))
            {
                if (!TelleRGUI.Confirm("Assets 안에 패키지 생성",
                    "Assets 폴더 안에 만들면 일반 스크립트로 컴파일되어, 나중에 Dev Mode로 설치할 때 같은 코드가 두 번 컴파일됩니다.\n" +
                    "프로젝트 밖(또는 Packages 폴더)에 만드는 것을 권장합니다. 그래도 계속할까요?", "계속", "취소"))
                    return;
            }

            string previousPath = currentPackagePath;
            try
            {
                Directory.CreateDirectory(packageRoot);
                currentPackagePath = packageRoot;

                File.WriteAllText(Path.Combine(packageRoot, "package.json"),
                    BuildPackageJson(null), new UTF8Encoding(false));
                CreateFileMeta(Path.Combine(packageRoot, "package.json"));
                CreateReadme(packageRoot);
                CreateChangelog(packageRoot);
                CreateLicense(packageRoot);
                CreateGitIgnore(packageRoot);
            }
            catch (Exception e)
            {
                currentPackagePath = previousPath;
                TryDeleteIfEmpty(packageRoot);
                UpdateStepStates();
                TelleRGUI.Info("패키지 생성 실패", $"패키지 폴더를 만들지 못했습니다:\n{packageRoot}\n\n{e.Message}");
                return;
            }

            EditorPrefs.SetString(PREF_LAST_PATH, packageRoot);

            step1Status.text = $"생성됨: {packageName}";
            step1Status.style.color = AccentGreen;
            UpdateStep1Summary(packageName);

            UpdateStepStates();
            RefreshFeatureList();
            UpdateDevModeUI();
            RefreshOrganizerFeatureOptions();
            RefreshOrganizer();

            TelleRGUI.Info("패키지 생성 완료",
                $"'{packageName}' 패키지를 만들었습니다.\n{packageRoot}\n\n" +
                "2단계에서 기능 파일을 추가하고, 변경 사항은 git으로 커밋·푸시하세요.");
        }

        private static void TryDeleteIfEmpty(string dir)
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch
            {
                // 정리 실패는 무시 — 빈 폴더만 남는다
            }
        }

        private void OpenPackageFolder()
        {
            if (!HasPackage)
            {
                TelleRGUI.Info("패키지 없음", "먼저 1단계에서 패키지를 만들거나 불러오세요.");
                return;
            }

            EditorUtility.RevealInFinder(currentPackagePath);
        }

        // ─── Process Pending Items ───

        private sealed class CopyPlan
        {
            public PendingItem Item;
            public string FileName;
            public string DestDir;
            public string DestPath;
            public bool ToEditor;
            public string Category;
        }

        private CopyPlan PlanItem(PendingItem item, string featureName)
        {
            bool toEditor = item.Kind == PendingKind.Script
                ? (item.Placement == PendingPlacement.Auto ? item.DetectedEditor : item.Placement == PendingPlacement.Editor)
                : item.Placement == PendingPlacement.Editor;
            bool useResources = item.Kind != PendingKind.Script && item.UseResources;

            string rootPath = Path.Combine(currentPackagePath, toEditor ? "Editor" : "Runtime");
            string destDir;
            if (useResources) destDir = Path.Combine(rootPath, "Resources", featureName);
            else if (item.Kind == PendingKind.Shader) destDir = Path.Combine(rootPath, featureName, "Shaders");
            else destDir = Path.Combine(rootPath, featureName);

            string kindLabel;
            switch (item.Kind)
            {
                case PendingKind.Script: kindLabel = "Script"; break;
                case PendingKind.Image: kindLabel = "Image"; break;
                case PendingKind.Shader: kindLabel = "Shader"; break;
                default: kindLabel = "Asset"; break;
            }

            string fileName = Path.GetFileName(item.SourceFullPath);
            return new CopyPlan
            {
                Item = item,
                FileName = fileName,
                DestDir = destDir,
                DestPath = Path.Combine(destDir, fileName),
                ToEditor = toEditor,
                Category = $"{(toEditor ? "Editor" : "Runtime")} {kindLabel}{(useResources ? " (Resources)" : "")}"
            };
        }

        private static IEqualityComparer<string> PathComparer =>
            PathComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        // 같은 대상 경로로 들어가는 항목들을 "대상 ← 원본1, 원본2" 한 줄씩으로 돌려준다
        private List<string> FindBatchDuplicates(List<CopyPlan> plans)
        {
            return plans.GroupBy(p => NormalizeDir(p.DestPath), PathComparer)
                .Where(g => g.Count() > 1)
                .Select(g => $"{PackageRelative(g.Key)} ← {string.Join(", ", g.Select(p => SourceDisplay(p.Item.SourceFullPath)))}")
                .ToList();
        }

        // 건너뛴 원본의 GUID → 패키지에 이미 있는 같은 이름 파일의 GUID
        private static void MapSkippedToExisting(List<CopyPlan> skipped, Dictionary<string, string> guidMap)
        {
            foreach (var c in skipped)
            {
                string srcGuid = ReadMetaGuid(c.Item.SourceFullPath + ".meta");
                string destGuid = ReadMetaGuid(c.DestPath + ".meta");
                if (!string.IsNullOrEmpty(srcGuid) && !string.IsNullOrEmpty(destGuid) && srcGuid != destGuid)
                    guidMap[srcGuid] = destGuid;
            }
        }

        // 프로젝트 안의 파일은 Assets/… 형태로 짧게 보여준다
        private static string SourceDisplay(string fullPath)
        {
            try
            {
                string project = NormalizeDir(Path.GetDirectoryName(Application.dataPath)) + "/";
                string p = NormalizeDir(fullPath);
                return p.StartsWith(project, PathComparison) ? p.Substring(project.Length) : p;
            }
            catch
            {
                return fullPath;
            }
        }

        private void ProcessPendingItems(string featureName)
        {
            var failedItems = new List<string>();
            var plans = new List<CopyPlan>();
            var handled = new List<PendingItem>();

            foreach (var item in pendingItems)
            {
                if (!File.Exists(item.SourceFullPath))
                {
                    failedItems.Add(Path.GetFileName(item.SourceFullPath));
                    handled.Add(item);
                    continue;
                }
                plans.Add(PlanItem(item, featureName));
            }

            // ── 대기 목록 안의 충돌: 다른 폴더의 같은 이름 파일이 같은 위치로 들어가면 하나가 사라지므로 복사 전에 막는다 ──
            var batchDups = FindBatchDuplicates(plans);
            if (batchDups.Count > 0)
            {
                TelleRGUI.Info("대기 목록 안에서 이름이 겹침",
                    $"다음 파일은 대기 목록 안에서 같은 위치로 들어갑니다:\n{FormatList(batchDups, 10)}\n\n" +
                    "하나만 남기고 목록에서 빼거나(x 버튼) 배치(Editor/Runtime, Res)를 다르게 바꾼 뒤 다시 시도하세요.\n아무 파일도 복사하지 않았습니다.");
                return;
            }

            // ── 패키지에 이미 있는 파일과의 충돌 ──
            var conflicts = plans.Where(p => File.Exists(p.DestPath)).ToList();
            var conflictSkipped = new List<string>();
            var guidMap = new Dictionary<string, string>();
            if (conflicts.Count > 0)
            {
                var names = conflicts.Select(c => PackageRelative(c.DestPath)).ToList();
                int choice = EditorUtility.DisplayDialogComplex("같은 이름의 파일",
                    $"패키지에 같은 이름의 파일이 {names.Count}개 있습니다:\n{FormatList(names, 15)}\n\n" +
                    "덮어쓰면 기존 파일 내용은 되돌릴 수 없습니다. (기존 GUID는 유지되어 참조는 끊기지 않습니다)",
                    "모두 덮어쓰기", "취소", "충돌 건너뛰기");
                if (choice == 1) return;
                if (choice == 2)
                {
                    foreach (var c in conflicts)
                    {
                        plans.Remove(c);
                        handled.Add(c.Item);
                        conflictSkipped.Add(c.FileName);
                    }
                    // 건너뛴 파일은 패키지에 있는 같은 이름 파일이 대신한다 — 함께 복사되는 에셋의 참조도 그쪽으로 돌린다
                    MapSkippedToExisting(conflicts, guidMap);
                }
            }

            var copied = new List<CopyPlan>();
            var remapTargets = new List<string>();
            bool cancelled = false;
            Exception error = null;

            try
            {
                // 필요한 쪽(Editor/Runtime)의 어셈블리만 준비 — 스크립트가 없는 쪽에는 asmdef를 만들지 않는다
                if (plans.Any(p => p.Item.Kind == PendingKind.Script && !p.ToEditor))
                    EnsureAssemblyFor(false, plans.First(p => p.Item.Kind == PendingKind.Script && !p.ToEditor).DestDir);
                if (plans.Any(p => p.Item.Kind == PendingKind.Script && p.ToEditor))
                    EnsureAssemblyFor(true, plans.First(p => p.Item.Kind == PendingKind.Script && p.ToEditor).DestDir);

                for (int i = 0; i < plans.Count; i++)
                {
                    var plan = plans[i];
                    if (EditorUtility.DisplayCancelableProgressBar("UPM Package Creator",
                        $"파일 복사 중 ({i + 1}/{plans.Count}) {plan.FileName}", plans.Count == 0 ? 1f : (float)i / plans.Count))
                    {
                        cancelled = true;
                        break;
                    }

                    EnsureFolder(plan.DestDir);
                    File.Copy(plan.Item.SourceFullPath, plan.DestPath, true);
                    string destGuid = CreateMetaFromSourceOrDefault(plan.Item.SourceFullPath, plan.DestPath, plan.Item.Kind);
                    string srcGuid = ReadMetaGuid(plan.Item.SourceFullPath + ".meta");
                    if (!string.IsNullOrEmpty(srcGuid) && !string.IsNullOrEmpty(destGuid) && srcGuid != destGuid)
                        guidMap[srcGuid] = destGuid;

                    remapTargets.Add(plan.DestPath);
                    remapTargets.Add(plan.DestPath + ".meta");
                    copied.Add(plan);
                    handled.Add(plan.Item);
                }
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 복사본은 새 GUID를 받으므로, 함께 복사된 에셋끼리의 참조(머티리얼→텍스처, 프리팹→스크립트 등)를 새 GUID로 바꾼다
            var externalRefs = new List<string>();
            try
            {
                RemapCopiedGuids(remapTargets, guidMap, externalRefs);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LOG} GUID 참조 갱신 중 오류: {e.Message}");
            }

            foreach (var h in handled) pendingItems.Remove(h);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            RefreshPendingList();
            RefreshFeatureList();
            RefreshOrganizerFeatureOptions();
            RefreshOrganizer();

            if (error != null)
            {
                TelleRGUI.Info("복사 중단",
                    $"복사 중 오류로 중단되었습니다. {copied.Count}개만 복사되었습니다:\n{error.Message}\n\n복사되지 않은 항목은 대기 목록에 남아 있습니다.");
                return;
            }

            if (copied.Count == 0)
            {
                string reason = cancelled ? "사용자가 취소했습니다." :
                    conflictSkipped.Count > 0 ? "모든 항목이 충돌로 건너뛰어졌습니다." :
                    "원본이 이동·삭제되었는지 확인하세요.";
                TelleRGUI.Info("복사된 파일 없음", "복사된 파일이 없습니다. " + reason);
                return;
            }

            if (pendingItems.Count == 0) newFeatureNameField.value = "";

            var sb = new StringBuilder();
            sb.Append($"'{featureName}' 기능에 {copied.Count}개 파일을 추가했습니다.\n\n");
            foreach (var group in copied.GroupBy(c => c.Category))
                sb.Append($"{group.Key}: {group.Count()} ({string.Join(", ", group.Select(g => g.FileName))})\n");
            if (conflictSkipped.Count > 0) sb.Append($"\n건너뜀(충돌): {string.Join(", ", conflictSkipped)}");
            if (failedItems.Count > 0) sb.Append($"\n건너뜀(원본 없음): {string.Join(", ", failedItems)}");
            if (cancelled) sb.Append($"\n취소됨: 나머지 {pendingItems.Count}개는 대기 목록에 남아 있습니다.");
            if (externalRefs.Count > 0)
            {
                sb.Append(sb[sb.Length - 1] == '\n' ? "\n" : "\n\n");
                sb.Append("주의: 패키지 밖(Assets/)의 에셋을 참조합니다. 함께 추가하지 않으면 다른 프로젝트에서 Missing이 됩니다:\n");
                sb.Append(FormatList(externalRefs, 10));
            }

            TelleRGUI.Info("추가 완료", sb.ToString().TrimEnd('\n'));
        }

        private void RemapCopiedGuids(List<string> files, Dictionary<string, string> guidMap, List<string> externalRefs)
        {
            var newGuids = new HashSet<string>(guidMap.Values);
            foreach (string file in files)
            {
                if (!File.Exists(file) || !LooksLikeText(file)) continue;

                byte[] bytes = File.ReadAllBytes(file);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                string text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

                if (guidMap.Count > 0)
                {
                    string replaced = RemapGuids(text, guidMap);
                    if (!ReferenceEquals(replaced, text) && replaced != text)
                    {
                        text = replaced;
                        File.WriteAllText(file, text, new UTF8Encoding(bom));
                    }
                }

                // 프로젝트 Assets/에만 있는 에셋을 가리키는 참조 — 패키지를 다른 프로젝트에 설치하면 Missing
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || !text.StartsWith("%YAML", StringComparison.Ordinal)) continue;
                foreach (Match m in YamlGuidRefRegex.Matches(text))
                {
                    string guid = m.Groups[1].Value;
                    if (newGuids.Contains(guid) || guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                        AddUnique(externalRefs, $"{Path.GetFileName(file)} → {assetPath}");
                }
            }
        }

        /// <summary>텍스트 안의 32자리 GUID 토큰을 map(소문자 키)에 따라 치환한다. 바뀐 것이 없으면 같은 인스턴스를 돌려준다.</summary>
        internal static string RemapGuids(string text, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(text) || map == null || map.Count == 0) return text;
            bool changed = false;
            string result = GuidTokenRegex.Replace(text, m =>
            {
                if (map.TryGetValue(m.Value.ToLowerInvariant(), out string n))
                {
                    changed = true;
                    return n;
                }
                return m.Value;
            });
            return changed ? result : text;
        }

        private static bool LooksLikeText(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0 || info.Length > 64L * 1024 * 1024) return false;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var buf = new byte[8192];
                    int n = fs.Read(buf, 0, buf.Length);
                    for (int k = 0; k < n; k++)
                        if (buf[k] == 0) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 폴더와 (패키지 안이라면) 폴더 .meta를 상위부터 차례로 만든다 — git 설치(불변 폴더)에서는 .meta가 없으면 무시되기 때문
        private void EnsureFolder(string dir)
        {
            string full = Path.GetFullPath(dir);
            string parent = Path.GetDirectoryName(full);
            bool inside = IsInsidePackage(full);

            if (!Directory.Exists(full))
            {
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent) && IsInsidePackage(parent))
                    EnsureFolder(parent);
                Directory.CreateDirectory(full);
            }
            if (inside && !string.IsNullOrEmpty(parent) && !File.Exists(full.TrimEnd('\\', '/') + ".meta"))
                CreateFolderMeta(parent, Path.GetFileName(full.TrimEnd('\\', '/')));
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
            // 기존 폴더 구조에서 예약어 이름이 기능 카드로 노출된 경우의 루트 삭제 사고 방어
            if (string.IsNullOrEmpty(featureName) ||
                featureName.Equals("Resources", StringComparison.OrdinalIgnoreCase) ||
                featureName.Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
                featureName.Equals("Runtime", StringComparison.OrdinalIgnoreCase))
            {
                TelleRGUI.Info("삭제 불가", $"'{featureName}'은(는) 구조 폴더명이라 여기서 삭제할 수 없습니다.\n탐색기에서 직접 정리해 주세요.");
                return;
            }

            var deleteTargets = new List<string>
            {
                Path.Combine(currentPackagePath, "Editor", featureName),
                Path.Combine(currentPackagePath, "Runtime", featureName),
                Path.Combine(currentPackagePath, "Editor", "Resources", featureName),
                Path.Combine(currentPackagePath, "Runtime", "Resources", featureName),
            };
            var existingTargets = deleteTargets.Where(Directory.Exists).ToList();
            if (existingTargets.Count == 0) return;

            string listText = string.Join("\n", existingTargets.Select(p => $"- {PackageRelative(p)} (파일 {CountNonMetaFiles(p)}개)"));
            if (!TelleRGUI.Confirm("기능 삭제",
                $"'{featureName}' 기능을 삭제할까요?\n\n다음 폴더가 영구 삭제됩니다 (휴지통 미사용, 되돌릴 수 없음):\n{listText}", "삭제", "취소"))
                return;

            try
            {
                foreach (string dir in existingTargets)
                    DeleteDirIfExists(dir);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                RefreshFeatureList();
                RefreshOrganizerFeatureOptions();
                RefreshOrganizer();
            }
            catch (Exception e)
            {
                TelleRGUI.Info("삭제 실패", $"기능 폴더를 삭제하지 못했습니다:\n{e.Message}");
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
            if (!TelleRGUI.Confirm("파일 삭제",
                $"'{entry.FileName}'을(를) 삭제할까요?\n{PackageRelative(entry.FullPath)}\n\n휴지통을 거치지 않으며 되돌릴 수 없습니다.", "삭제", "취소"))
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
                TelleRGUI.Info("삭제 실패", $"파일을 삭제하지 못했습니다:\n{e.Message}");
            }
        }

        private void MoveEntry(string featureName, FeatureEntry entry, FeatureLocation target)
        {
            if (entry.Location == target) return;

            try
            {
                string destDir = GetFeatureLocationPath(featureName, target);
                bool toEditor = target == FeatureLocation.EditorFeature || target == FeatureLocation.EditorResources;

                string destPath = Path.Combine(destDir, entry.FileName);

                if (File.Exists(destPath))
                {
                    if (!TelleRGUI.Confirm("파일 덮어쓰기",
                        $"'{entry.FileName}'이(가) 대상 폴더에 이미 있습니다.\n{PackageRelative(destPath)}\n\n덮어쓰면 기존 파일은 영구 삭제됩니다.", "덮어쓰기", "취소"))
                        return;
                }

                // 스크립트가 들어가는 쪽에만 어셈블리를 준비한다
                if (entry.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    EnsureAssemblyFor(toEditor, destDir);
                EnsureFolder(destDir);

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
                TelleRGUI.Info("이동 실패", $"파일을 이동하지 못했습니다:\n{e.Message}");
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
            string bumped = BumpVersionString(versionField.value, type);
            if (bumped == null)
            {
                TelleRGUI.Info("버전 형식 오류",
                    $"'{versionField.value}'은(는) MAJOR.MINOR.PATCH 형식이 아닙니다.\n예: 1.0.0, 1.2.0-preview.1");
                return;
            }
            versionField.value = bumped;
        }

        /// <summary>semver 숫자 부분만 올리고 -preview 같은 접미사는 유지한다. 형식이 아니면 null.</summary>
        internal static string BumpVersionString(string version, string type)
        {
            var m = Regex.Match((version ?? "").Trim(), @"^(\d+)\.(\d+)\.(\d+)(.*)$");
            if (!m.Success) return null;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
                return null;

            switch (type)
            {
                case "major": major++; minor = 0; patch = 0; break;
                case "minor": minor++; patch = 0; break;
                case "patch": patch++; break;
            }

            return $"{major}.{minor}.{patch}{m.Groups[4].Value}";
        }

        /// <summary>UPM이 git 소스로 인식하는 URL 형식인지 (https/http, git@, ssh://, git://, git+…)</summary>
        internal static bool IsGitUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string s = url.Trim();
            string[] prefixes = { "https://", "http://", "git@", "ssh://", "git://", "git+https://", "git+http://", "git+ssh://", "git+file://" };
            foreach (string p in prefixes)
                if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>fromDir 기준 toPath의 상대 경로 ('/' 구분). 드라이브가 다르면 null.</summary>
        internal static string MakeRelativePath(string fromDir, string toPath)
        {
            string a = NormalizeDir(fromDir);
            string b = NormalizeDir(toPath);
            string[] aParts = a.Split('/');
            string[] bParts = b.Split('/');
            var cmp = PathComparison;

            if (aParts.Length == 0 || bParts.Length == 0 || !string.Equals(aParts[0], bParts[0], cmp)) return null;

            int common = 0;
            while (common < aParts.Length && common < bParts.Length && string.Equals(aParts[common], bParts[common], cmp))
                common++;

            var parts = new List<string>();
            for (int k = common; k < aParts.Length; k++) parts.Add("..");
            for (int k = common; k < bParts.Length; k++) parts.Add(bParts[k]);
            return parts.Count == 0 ? "." : string.Join("/", parts);
        }

        private static string ManifestPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));

        private void ToggleDevMode()
        {
            if (!HasPackage)
            {
                TelleRGUI.Info("패키지 없음", "먼저 1단계에서 패키지를 만들거나 불러오세요.");
                return;
            }

            string manifestPath = ManifestPath;
            if (!File.Exists(manifestPath))
            {
                TelleRGUI.Info("manifest.json 없음", $"이 프로젝트의 Packages/manifest.json을 찾지 못했습니다:\n{manifestPath}");
                return;
            }

            // 전환 키는 입력 필드가 아니라 디스크의 package.json name 기준 (필드 편집 중 오염 방지)
            string packageName = GetLoadedPackageName();
            if (!IsValidPackageName(packageName))
            {
                TelleRGUI.Info("패키지 이름 오류", $"package.json의 name이 올바르지 않아 manifest.json을 바꾸지 않았습니다: '{packageName}'\n\n{PackageNameRuleText}");
                return;
            }
            string manifestContent;
            try
            {
                manifestContent = ReadFileShared(manifestPath);
            }
            catch (Exception e)
            {
                TelleRGUI.Info("manifest.json 읽기 실패", $"manifest.json을 읽지 못해 모드를 바꾸지 않았습니다:\n{manifestPath}\n\n{e.Message}");
                return;
            }
            string originalManifest = manifestContent;

            bool isDevMode = IsDevModeEnabled(manifestContent, packageName);

            if (isDevMode)
            {
                // → Deploy: 실제 URL만 허용 (플레이스홀더가 기록되면 UPM resolve가 깨짐)
                string gitUrl = (gitUrlField != null ? gitUrlField.value : "").Trim();
                if (string.IsNullOrEmpty(gitUrl))
                    gitUrl = EditorPrefs.GetString($"UPMCreator_GitUrl_{packageName}", "");

                if (string.IsNullOrEmpty(gitUrl) || gitUrl.Contains("USERNAME") || gitUrl == GitUrlPlaceholder)
                {
                    TelleRGUI.Info("Git URL 필요",
                        $"Deploy Mode는 실제 git 저장소 URL이 필요합니다.\nGit URL 칸에 입력해 주세요.\n예: {GitUrlPlaceholder}");
                    return;
                }

                if (!IsGitUrl(gitUrl))
                {
                    TelleRGUI.Info("Git URL 형식 오류",
                        $"git URL 형식이 아닙니다:\n{gitUrl}\n\nhttps://, git@, ssh://, git:// 또는 git+… 로 시작해야 합니다.");
                    return;
                }

                if (gitUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !Regex.IsMatch(gitUrl, @"\.git(/?)([?#].*)?$", RegexOptions.IgnoreCase))
                {
                    if (!TelleRGUI.Confirm("Git URL 확인",
                        $"https URL이 .git으로 끝나지 않습니다:\n{gitUrl}\n\nPackage Manager는 .git으로 끝나는 https 주소만 git 저장소로 인식합니다. 그래도 사용할까요?",
                        "사용", "취소"))
                        return;
                }

                if (!TelleRGUI.Confirm("Deploy Mode로 전환",
                    $"manifest.json의 '{packageName}' 소스를 git URL로 바꿉니다:\n{gitUrl}\n\n" +
                    "확인해 주세요:\n" +
                    "• 로컬 수정을 커밋 & 푸시했나요?\n" +
                    "• 버전을 올려 package.json을 저장했나요?\n\n" +
                    "전환하면 저장소의 최신 커밋을 받아옵니다.",
                    "전환", "취소"))
                    return;

                EditorPrefs.SetString($"UPMCreator_GitUrl_{packageName}", gitUrl);
                manifestContent = SetPackageSource(manifestContent, packageName, gitUrl);
                if (!TryWriteManifest(manifestPath, originalManifest, manifestContent)) return;
                // lock에 커밋 해시가 고정되어 있으면 push해도 옛 코드가 유지됨 — 항목을 지워 재해석 강제
                RemovePackagesLockEntry(packageName);
            }
            else
            {
                // manifest.json의 file: 상대 경로는 Packages 폴더 기준 — 같은 드라이브면 상대 경로로 써서 절대 경로 커밋 사고를 줄인다
                string packagesDir = Path.GetDirectoryName(manifestPath);
                string rel = MakeRelativePath(packagesDir, currentPackagePath);
                string localPath = rel != null
                    ? "file:" + rel
                    : "file:" + NormalizeDir(currentPackagePath);

                if (!TelleRGUI.Confirm("Dev Mode로 전환",
                    $"manifest.json의 '{packageName}' 소스를 로컬 폴더로 바꿉니다:\n{localPath}\n\n" +
                    (rel != null
                        ? "Packages 폴더 기준 상대 경로라서, 같은 폴더 구조가 아닌 PC에서는 패키지를 찾지 못합니다.\n"
                        : "프로젝트와 드라이브가 달라 절대 경로를 씁니다. 다른 PC에서는 패키지를 찾지 못합니다.\n") +
                    "manifest.json을 이 상태로 커밋하지 말고, 커밋 전에 Deploy Mode로 되돌리세요.",
                    "전환", "취소"))
                    return;

                string currentUrl = GetCurrentPackageSource(manifestContent, packageName);
                if (IsGitUrl(currentUrl))
                {
                    EditorPrefs.SetString($"UPMCreator_GitUrl_{packageName}", currentUrl);
                    if (gitUrlField != null && string.IsNullOrEmpty(gitUrlField.value))
                        gitUrlField.value = currentUrl;
                }

                manifestContent = SetPackageSource(manifestContent, packageName, localPath);
                if (!TryWriteManifest(manifestPath, originalManifest, manifestContent)) return;
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

        // manifest.json 쓰기 실패 시: 반쯤 쓰인 파일은 원본으로 되돌리고, 되돌리지 못하면 원본을 콘솔에 남긴다
        // (manifest가 깨지면 프로젝트의 모든 패키지 해석이 실패하므로)
        private bool TryWriteManifest(string manifestPath, string original, string updated)
        {
            try
            {
                WriteFileShared(manifestPath, updated);
                return true;
            }
            catch (Exception e)
            {
                string after = null;
                try { after = ReadFileShared(manifestPath); } catch { }
                bool intact = after == original;
                if (!intact && after != null)
                {
                    try { WriteFileShared(manifestPath, original); intact = true; } catch { }
                }
                if (!intact)
                    Debug.LogWarning($"{LOG} manifest.json 쓰기 실패 — 수동 복구용 원본 내용:\n{original}");

                TelleRGUI.Info("manifest.json 쓰기 실패",
                    $"manifest.json을 저장하지 못해 모드를 바꾸지 않았습니다:\n{manifestPath}\n\n{e.Message}\n\n" +
                    (intact
                        ? "파일은 원래 내용 그대로입니다. 다른 프로그램이 파일을 쓰고 있지 않은지, 읽기 전용이 아닌지 확인해 주세요."
                        : "파일 상태를 확인하지 못했습니다. 콘솔에 원본 내용을 남겼으니 필요하면 그 내용으로 복구하세요."));
                return false;
            }
        }

        private bool IsDevModeEnabled(string manifestContent, string packageName)
        {
            string source = GetCurrentPackageSource(manifestContent, packageName);
            return !string.IsNullOrEmpty(source) && source.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        }

        // 전환 키·asmdef 이름의 진실의 원천 — Step 1 입력 필드가 아니라 디스크에 저장된 package.json의 name
        private string GetLoadedPackageName()
        {
            try
            {
                string jsonPath = Path.Combine(currentPackagePath, "package.json");
                if (File.Exists(jsonPath))
                {
                    var fields = UPMJson.ReadPackageFields(SafeReadAllText(jsonPath));
                    if (!string.IsNullOrEmpty(fields.Name)) return fields.Name;
                }
            }
            catch
            {
                // 해석 실패 시 입력 필드 값으로 대체
            }
            return packageNameField != null ? (packageNameField.value ?? "").Trim() : "";
        }

        // packages-lock.json에서 이 패키지 항목 제거 — git 의존성은 커밋 해시로 잠기므로
        // 지우지 않으면 Deploy 전환 후에도 옛 커밋 코드가 유지된다
        private void RemovePackagesLockEntry(string packageName)
        {
            try
            {
                string lockPath = Path.Combine(Application.dataPath, "..", "Packages", "packages-lock.json");
                if (!File.Exists(lockPath)) return;

                string content = ReadFileShared(lockPath);
                var m = Regex.Match(content, $"\"{Regex.Escape(packageName)}\"\\s*:\\s*\\{{");
                if (!m.Success) return;

                int start = m.Index;
                int depth = 0;
                int end = -1;
                for (int i = m.Index + m.Length - 1; i < content.Length; i++)
                {
                    if (content[i] == '{') depth++;
                    else if (content[i] == '}')
                    {
                        depth--;
                        if (depth == 0) { end = i; break; }
                    }
                }
                if (end < 0) return;

                int removeEnd = end + 1;
                int probe = removeEnd;
                while (probe < content.Length && char.IsWhiteSpace(content[probe])) probe++;
                if (probe < content.Length && content[probe] == ',')
                    removeEnd = probe + 1;
                else
                {
                    int back = start - 1;
                    while (back >= 0 && char.IsWhiteSpace(content[back])) back--;
                    if (back >= 0 && content[back] == ',') start = back;
                }

                content = content.Remove(start, removeEnd - start);
                WriteFileShared(lockPath, content);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LOG} packages-lock 갱신 실패 (Package Manager의 Update로 대체 가능): {e.Message}");
            }
        }

        private string GetCurrentPackageSource(string manifestContent, string packageName)
        {
            // 콜론 뒤 공백 유무와 무관하게 매칭 (기존 고정 문자열 매칭은 포맷이 다르면 중복 키를 만들었음)
            var m = Regex.Match(manifestContent, $"\"{Regex.Escape(packageName)}\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private string SetPackageSource(string manifestContent, string packageName, string newSource)
        {
            string escaped = UPMJson.EscapeString(newSource);
            var m = Regex.Match(manifestContent, $"\"{Regex.Escape(packageName)}\"\\s*:\\s*\"([^\"]*)\"");

            if (!m.Success)
            {
                var dep = Regex.Match(manifestContent, "\"dependencies\"\\s*:\\s*\\{");
                if (!dep.Success) return manifestContent;

                int insertAt = dep.Index + dep.Length;
                int probe = insertAt;
                while (probe < manifestContent.Length && char.IsWhiteSpace(manifestContent[probe])) probe++;
                bool emptyDeps = probe < manifestContent.Length && manifestContent[probe] == '}';
                // 빈 dependencies에 트레일링 콤마를 남기면 manifest 전체 파싱이 깨져 모든 패키지 로드가 실패함
                string newEntry = emptyDeps
                    ? $"\n    \"{packageName}\": \"{escaped}\"\n  "
                    : $"\n    \"{packageName}\": \"{escaped}\",";
                return manifestContent.Insert(insertAt, newEntry);
            }

            var g = m.Groups[1];
            return manifestContent.Substring(0, g.Index) + escaped + manifestContent.Substring(g.Index + g.Length);
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

            string manifestPath = ManifestPath;
            if (!File.Exists(manifestPath))
            {
                devModeStatus.text = "이 프로젝트에서 Packages/manifest.json을 찾지 못했습니다.";
                devModeStatus.style.color = TextMuted;
                SetBorder(devModeStatus.style, 1, Border);
                devModeButton.text = "Enable Dev Mode";
                return;
            }

            string packageName = GetLoadedPackageName();
            string manifestContent;
            try
            {
                manifestContent = ReadFileShared(manifestPath);
            }
            catch (Exception e)
            {
                devModeStatus.text = $"manifest.json을 읽지 못했습니다: {e.Message}";
                devModeStatus.style.color = TextMuted;
                return;
            }
            string source = GetCurrentPackageSource(manifestContent, packageName);
            bool isDevMode = !string.IsNullOrEmpty(source) && source.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

            // Deploy 전환용 URL 칸 자동 채움 (현재 git 소스 → 저장된 URL 순)
            if (gitUrlField != null && string.IsNullOrEmpty(gitUrlField.value))
            {
                if (!isDevMode && IsGitUrl(source))
                    gitUrlField.value = source;
                else
                {
                    string savedUrl = EditorPrefs.GetString($"UPMCreator_GitUrl_{packageName}", "");
                    if (!string.IsNullOrEmpty(savedUrl)) gitUrlField.value = savedUrl;
                }
            }

            if (isDevMode)
            {
                devModeStatus.text = $"DEV MODE — 로컬 폴더 참조 (수정이 즉시 반영됨)\n{source}";
                devModeStatus.style.color = AccentGreen;
                SetBorder(devModeStatus.style, 1, new Color(AccentGreen.r, AccentGreen.g, AccentGreen.b, 0.4f));
                devModeStatus.style.backgroundColor = new Color(AccentGreen.r, AccentGreen.g, AccentGreen.b, 0.08f);
                devModeButton.text = "Switch to Deploy Mode (git URL)";
                SetBg(devModeButton, new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.15f));
                devModeButton.style.color = AccentAmber;
                SetBorder(devModeButton.style, 1, new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.3f));
            }
            else
            {
                if (string.IsNullOrEmpty(source))
                    devModeStatus.text = "미등록 — 이 프로젝트의 manifest.json에 아직 없습니다.\nDev Mode로 전환하면 로컬 경로(file:)로 추가됩니다.";
                else if (IsGitUrl(source))
                    devModeStatus.text = $"DEPLOY MODE — git 참조 (푸시 후 재전환·Update 전까지 코드 고정)\n{source}";
                else
                    devModeStatus.text = $"레지스트리 버전 참조\n{source}";
                devModeStatus.style.color = AccentAmber;
                SetBorder(devModeStatus.style, 1, new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.4f));
                devModeStatus.style.backgroundColor = new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.08f);
                devModeButton.text = "Switch to Dev Mode (local folder)";
                SetBg(devModeButton, new Color(AccentGreen.r, AccentGreen.g, AccentGreen.b, 0.15f));
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

        internal static bool IsValidPackageName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 214 && PackageNameRegex.IsMatch(name);
        }

        private bool ValidateSaveFields(out string error)
        {
            string name = (packageNameField.value ?? "").Trim();
            string version = (versionField.value ?? "").Trim();
            string unity = (unityVersionField.value ?? "").Trim();

            if (!IsValidPackageName(name))
            {
                error = (name.Length == 0 ? "Package Name을 입력하세요.\n\n" : $"'{name}'은(는) 쓸 수 없는 패키지 이름입니다.\n\n") + PackageNameRuleText;
                return false;
            }
            if (!SemVerRegex.IsMatch(version))
            {
                error = $"'{version}'은(는) 올바른 버전이 아닙니다.\nMAJOR.MINOR.PATCH 형식이어야 합니다. (예: 1.0.0, 1.2.0-preview.1)";
                return false;
            }
            if (unity.Length > 0 && !UnityVersionRegex.IsMatch(unity))
            {
                error = $"Min Unity '{unity}'의 형식이 올바르지 않습니다.\n'2021.3'처럼 연도.마이너 형식이어야 합니다. 비워 두면 최소 버전을 지정하지 않습니다.";
                return false;
            }
            error = null;
            return true;
        }

        // 기존 JSON을 순서 보존 트리로 읽고 UI가 관리하는 키만 바꾼다 — dependencies·samples·keywords 등은 그대로 유지
        private string BuildPackageJson(string original)
        {
            return UPMJson.UpdatePackageJson(original,
                (packageNameField.value ?? "").Trim(),
                (versionField.value ?? "").Trim(),
                (displayNameField.value ?? "").Trim(),
                (descriptionField.value ?? "").Trim(),
                (unityVersionField.value ?? "").Trim(),
                (authorField.value ?? "").Trim());
        }

        private bool TrySavePackageJson(bool showDialog)
        {
            if (!HasPackage)
            {
                TelleRGUI.Info("패키지 없음", "먼저 1단계에서 패키지를 만들거나 불러오세요.");
                return false;
            }

            if (!ValidateSaveFields(out string fieldError))
            {
                TelleRGUI.Info("입력값 오류", fieldError);
                return false;
            }

            string path = Path.Combine(currentPackagePath, "package.json");
            string original = null;
            string updated;
            try
            {
                if (File.Exists(path)) original = File.ReadAllText(path);

                string oldName = original != null ? UPMJson.ReadPackageFields(original).Name : null;
                string newName = (packageNameField.value ?? "").Trim();
                if (!string.IsNullOrEmpty(oldName) && oldName != newName &&
                    !TelleRGUI.Confirm("패키지 이름 변경",
                        $"패키지 이름을 '{oldName}'에서 '{newName}'(으)로 바꿉니다.\n\n" +
                        "이 패키지를 설치한 프로젝트의 manifest.json 항목, Dev/Deploy 전환 기록은 옛 이름을 가리키므로 직접 고쳐야 합니다.",
                        "변경", "취소"))
                    return false;

                updated = BuildPackageJson(original);
            }
            catch (FormatException e)
            {
                TelleRGUI.Info("package.json 해석 실패",
                    "기존 package.json을 해석할 수 없어 저장하지 않았습니다.\n(UI에 없는 항목이 사라지지 않도록 덮어쓰지 않음)\n\n" + e.Message);
                return false;
            }
            catch (Exception e)
            {
                TelleRGUI.Info("저장 실패", $"package.json을 읽지 못했습니다:\n{e.Message}");
                return false;
            }

            if (original == updated)
            {
                if (showDialog) TelleRGUI.Info("변경 없음", $"package.json이 이미 v{versionField.value.Trim()} 내용과 같습니다.");
                return true;
            }

            try
            {
                File.WriteAllText(path, updated, new UTF8Encoding(false));
                if (!File.Exists(path + ".meta")) CreateFileMeta(path);
            }
            catch (Exception e)
            {
                TelleRGUI.Info("저장 실패", $"package.json을 쓰지 못했습니다:\n{e.Message}");
                return false;
            }

            UpdateStep1Summary((packageNameField.value ?? "").Trim());
            UpdateDevModeUI();

            if (showDialog)
                TelleRGUI.Info("저장 완료", $"package.json을 v{versionField.value.Trim()}(으)로 저장했습니다.\n\n변경 사항을 git으로 커밋·푸시하면 Deploy 사용자에게 반영됩니다.");
            return true;
        }

        private void CreateReadme(string root)
        {
            string path = Path.Combine(root, "README.md");
            string title = string.IsNullOrWhiteSpace(displayNameField.value) ? packageNameField.value.Trim() : displayNameField.value.Trim();
            File.WriteAllText(path, $"# {title}\n\n{descriptionField.value.Trim()}\n\n## Installation\n\nPackage Manager > + > Add package from git URL\n\n## License\n\nMIT License\n");
            CreateFileMeta(path);
        }

        private void CreateChangelog(string root)
        {
            string path = Path.Combine(root, "CHANGELOG.md");
            string version = (versionField.value ?? "1.0.0").Trim();
            File.WriteAllText(path, $"# Changelog\n\n## [{version}] - {DateTime.Now:yyyy-MM-dd}\n\n- Initial release\n");
            CreateFileMeta(path);
        }

        private void CreateLicense(string root)
        {
            string path = Path.Combine(root, "LICENSE.md");
            string holder = string.IsNullOrWhiteSpace(authorField.value) ? "the package authors" : authorField.value.Trim();
            File.WriteAllText(path, $"MIT License\n\nCopyright (c) {DateTime.Now.Year} {holder}\n\nPermission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\nThe above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\nTHE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.\n");
            CreateFileMeta(path);
        }

        private void CreateGitIgnore(string root)
        {
            File.WriteAllText(Path.Combine(root, ".gitignore"), "*.meta.bak\n.idea/\n.vs/\n.DS_Store\nThumbs.db\n");
        }

        // ─── Assembly Definitions ───

        private static string[] AssemblyFilesIn(string dir)
        {
            if (!Directory.Exists(dir)) return new string[0];
            return Directory.GetFiles(dir, "*.asmdef").Concat(Directory.GetFiles(dir, "*.asmref")).ToArray();
        }

        // 대상 폴더에서 패키지 루트까지 올라가며 asmdef/asmref가 있는 가장 가까운 폴더를 찾는다
        private string FindNearestAssemblyFolder(string dir)
        {
            string pkg = NormalizeDir(currentPackagePath);
            string cur = NormalizeDir(dir);
            while (!string.IsNullOrEmpty(cur) && (SamePath(cur, pkg) || cur.StartsWith(pkg + "/", PathComparison)))
            {
                if (AssemblyFilesIn(cur).Length > 0) return cur;
                if (SamePath(cur, pkg)) break;
                cur = Path.GetDirectoryName(cur)?.Replace('\\', '/');
            }
            return null;
        }

        private static bool IsEditorOnlyAssemblyFolder(string dir)
        {
            foreach (string f in Directory.GetFiles(dir, "*.asmdef"))
            {
                try
                {
                    var obj = UPMJson.Parse(File.ReadAllText(f)) as UPMJson.JsonObject;
                    var inc = obj?.Get("includePlatforms") as List<object>;
                    if (inc != null && inc.Count == 1 && "Editor".Equals(inc[0] as string)) return true;
                    // defineConstraints에 UNITY_EDITOR가 있어도 플레이어 빌드에서 빠진다
                    var defs = obj?.Get("defineConstraints") as List<object>;
                    if (defs != null && defs.Any(d => "UNITY_EDITOR".Equals(d as string))) return true;
                }
                catch
                {
                    // 해석 실패한 asmdef는 판단 불가 — 런타임용으로 간주
                }
            }
            return false;
        }

        /// <summary>
        /// 스크립트가 들어갈 targetDir을 덮는 어셈블리가 없을 때만 Editor/ 또는 Runtime/ 루트에 asmdef를 만든다.
        /// 같은 폴더에 asmdef가 둘이면 컴파일 오류이므로, 이름이 달라도 이미 있으면 새로 만들지 않는다.
        /// </summary>
        private void EnsureAssemblyFor(bool editor, string targetDir)
        {
            string rootPath = Path.Combine(currentPackagePath, editor ? "Editor" : "Runtime");
            EnsureFolder(rootPath);

            string nearest = FindNearestAssemblyFolder(targetDir);
            bool covered;
            bool rootMismatch = false;
            if (nearest == null) covered = false;
            else if (SamePath(nearest, currentPackagePath))
            {
                // 패키지 루트 asmdef가 반대 용도면 덮인 것으로 보지 않는다:
                // 런타임 루트에 에디터 스크립트가 섞이면 빌드가 깨지고, 에디터 전용 루트에 런타임 스크립트가 섞이면 빌드에서 빠진다
                covered = IsEditorOnlyAssemblyFolder(nearest) == editor;
                rootMismatch = !covered;
            }
            else covered = true;

            if (!covered && AssemblyFilesIn(rootPath).Length == 0)
            {
                if (!rootMismatch || ConfirmSplitFromRootAssembly(editor, rootPath))
                {
                    if (editor) CreateEditorAsmdef(rootPath);
                    else CreateRuntimeAsmdef(rootPath);
                }
            }

            LinkEditorToRuntime();
        }

        // 루트 어셈블리에 속해 있던 스크립트가 rootPath에 이미 있으면, 새 asmdef로 옮겨지는 것을 먼저 알린다
        private bool ConfirmSplitFromRootAssembly(bool editor, string rootPath)
        {
            int moving = 0;
            try
            {
                foreach (string f in Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories))
                    if (SamePath(FindNearestAssemblyFolder(Path.GetDirectoryName(f)), currentPackagePath)) moving++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LOG} {PackageRelative(rootPath)} 스크립트 목록을 읽지 못했습니다: {e.Message}");
            }
            if (moving == 0) return true;

            string folder = editor ? "Editor" : "Runtime";
            return TelleRGUI.Confirm("어셈블리 정의 생성",
                $"패키지 루트의 asmdef는 {(editor ? "런타임" : "에디터 전용")} 어셈블리라서, " +
                $"{(editor ? "에디터 스크립트가 섞이면 플레이어 빌드가 깨집니다" : "런타임 스크립트가 플레이어 빌드에서 빠집니다")}.\n" +
                $"{folder}/에 {(editor ? "에디터" : "런타임")} asmdef를 만들어 분리합니다.\n\n" +
                $"이미 {folder}/에 있는 스크립트 {moving}개도 새 어셈블리로 옮겨집니다. " +
                (editor ? "루트 어셈블리의 코드가 이 스크립트의 클래스를 쓰고 있으면" : "이 스크립트가 루트(에디터 전용) 어셈블리의 코드를 쓰고 있으면") +
                " 컴파일 오류가 날 수 있습니다.\n\n" +
                "만들지 않으면 스크립트는 루트 어셈블리에 그대로 포함됩니다.",
                "생성", "그대로 두기");
        }

        private HashSet<string> ExistingAssemblyNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string f in Directory.GetFiles(currentPackagePath, "*.asmdef", SearchOption.AllDirectories))
                {
                    string n = ReadAsmdefName(f);
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LOG} asmdef 목록을 읽지 못했습니다: {e.Message}");
            }
            try
            {
                foreach (var a in CompilationPipeline.GetAssemblies())
                    names.Add(a.name);
            }
            catch
            {
                // 컴파일 파이프라인 조회 실패 시 패키지 안 이름만으로 판단
            }
            return names;
        }

        private static string ReadAsmdefName(string asmdefPath)
        {
            try
            {
                var obj = UPMJson.Parse(File.ReadAllText(asmdefPath)) as UPMJson.JsonObject;
                return obj?.Get("name") as string;
            }
            catch
            {
                return null;
            }
        }

        private string MakeUniqueAssemblyName(string baseName)
        {
            var existing = ExistingAssemblyNames();
            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; i < 100; i++)
            {
                string candidate = baseName + i.ToString(CultureInfo.InvariantCulture);
                if (!existing.Contains(candidate)) return candidate;
            }
            return baseName + "." + GUID.Generate().ToString().Substring(0, 6);
        }

        private void CreateEditorAsmdef(string editorPath)
        {
            string pkgName = GetLoadedPackageName();
            string asmName = MakeUniqueAssemblyName(ConvertToAsmdefName(pkgName) + ".Editor");
            string path = Path.Combine(editorPath, asmName + ".asmdef");
            if (File.Exists(path)) return;

            var obj = new UPMJson.JsonObject();
            obj.Set("name", asmName);
            obj.Set("rootNamespace", ConvertToNamespace(pkgName));
            obj.Set("references", new List<object>());
            obj.Set("includePlatforms", new List<object> { "Editor" });
            obj.Set("excludePlatforms", new List<object>());
            File.WriteAllText(path, UPMJson.Serialize(obj, "    ", "\n") + "\n", new UTF8Encoding(false));
            CreateAsmdefMeta(path);
            Debug.Log($"{LOG} Editor 어셈블리 정의를 만들었습니다: {PackageRelative(path)}");
        }

        private void CreateRuntimeAsmdef(string runtimePath)
        {
            string pkgName = GetLoadedPackageName();
            string asmName = MakeUniqueAssemblyName(ConvertToAsmdefName(pkgName));
            string path = Path.Combine(runtimePath, asmName + ".asmdef");
            if (File.Exists(path)) return;

            var obj = new UPMJson.JsonObject();
            obj.Set("name", asmName);
            obj.Set("rootNamespace", ConvertToNamespace(pkgName));
            obj.Set("references", new List<object>());
            obj.Set("includePlatforms", new List<object>());
            obj.Set("excludePlatforms", new List<object>());
            File.WriteAllText(path, UPMJson.Serialize(obj, "    ", "\n") + "\n", new UTF8Encoding(false));
            CreateAsmdefMeta(path);
            Debug.Log($"{LOG} Runtime 어셈블리 정의를 만들었습니다: {PackageRelative(path)}");
        }

        // folder(Editor/ 또는 Runtime/)의 asmdef, 없으면 용도가 맞는 패키지 루트 asmdef (루트에 하나일 때만)
        private string PickAssembly(string folder, bool editor)
        {
            string[] own = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.asmdef") : new string[0];
            if (own.Length == 1) return own[0];
            if (own.Length > 1 || AssemblyFilesIn(folder).Length > 0) return null; // 여러 개거나 asmref뿐이면 판단하지 않음

            string[] rootAsms = Directory.GetFiles(currentPackagePath, "*.asmdef");
            if (rootAsms.Length != 1 || IsEditorOnlyAssemblyFolder(currentPackagePath) != editor) return null;
            return rootAsms[0];
        }

        private static bool ReferencesAssembly(string asmdefPath, string name, string guid)
        {
            try
            {
                var obj = UPMJson.Parse(File.ReadAllText(asmdefPath)) as UPMJson.JsonObject;
                var refs = obj?.Get("references") as List<object>;
                if (refs == null) return false;
                foreach (var r in refs)
                {
                    string s = r as string;
                    if (s == null) continue;
                    if (s == name) return true;
                    if (guid != null && s.Equals("GUID:" + guid, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch
            {
                // 해석 실패 — 참조 없음으로 간주
            }
            return false;
        }

        // 에디터 asmdef(Editor/, 없으면 에디터 전용 루트)가 런타임 asmdef(Runtime/, 없으면 런타임 루트)를
        // 참조하도록 references에 추가한다 (이름·GUID 참조 모두 인식, 다른 항목은 보존)
        private void LinkEditorToRuntime()
        {
            if (!Directory.Exists(currentPackagePath)) return;
            string editorPath = PickAssembly(Path.Combine(currentPackagePath, "Editor"), true);
            string runtimePath = PickAssembly(Path.Combine(currentPackagePath, "Runtime"), false);
            if (editorPath == null || runtimePath == null || SamePath(editorPath, runtimePath)) return;

            string runtimeName = ReadAsmdefName(runtimePath);
            if (string.IsNullOrEmpty(runtimeName)) return;
            string runtimeGuid = ReadMetaGuid(runtimePath + ".meta");

            // 런타임 쪽이 이미 에디터 쪽을 참조하면 반대 참조는 순환 참조 컴파일 오류가 된다
            if (ReferencesAssembly(runtimePath, ReadAsmdefName(editorPath), ReadMetaGuid(editorPath + ".meta")))
            {
                Debug.LogWarning($"{LOG} {Path.GetFileName(runtimePath)}가 {Path.GetFileName(editorPath)}를 참조하고 있어 반대 참조를 추가하지 않았습니다 (순환 참조 방지).");
                return;
            }

            try
            {
                string original = File.ReadAllText(editorPath);
                if (!(UPMJson.Parse(original) is UPMJson.JsonObject obj)) return;

                var refs = obj.Get("references") as List<object>;
                if (refs == null)
                {
                    refs = new List<object>();
                    obj.Set("references", refs);
                }

                foreach (var r in refs)
                {
                    string s = r as string;
                    if (s == null) continue;
                    if (s == runtimeName) return;
                    if (runtimeGuid != null && s.Equals("GUID:" + runtimeGuid, StringComparison.OrdinalIgnoreCase)) return;
                }

                bool useGuid = runtimeGuid != null && refs.Any(r => r is string s && s.StartsWith("GUID:", StringComparison.Ordinal));
                refs.Add(useGuid ? "GUID:" + runtimeGuid : runtimeName);

                string nl = original.Contains("\r\n") ? "\r\n" : "\n";
                string text = UPMJson.Serialize(obj, UPMJson.DetectIndent(original, "    "), nl);
                if (original.EndsWith("\n", StringComparison.Ordinal)) text += nl;
                File.WriteAllText(editorPath, text, new UTF8Encoding(false));
                if (!File.Exists(editorPath + ".meta")) CreateAsmdefMeta(editorPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LOG} {Path.GetFileName(editorPath)}에 Runtime 참조를 추가하지 못했습니다: {e.Message}");
            }
        }

        /// <summary>com.company.my-package → Company.MyPackage (C# 식별자로 쓸 수 있게 정리)</summary>
        internal static string ConvertToAsmdefName(string packageName)
        {
            string[] parts = (packageName ?? "").Split('.');
            var segs = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                string s = ToPascalIdentifier(parts[i]);
                if (s.Length > 0) segs.Add(s);
            }
            if (segs.Count == 0 && parts.Length > 0)
            {
                string s = ToPascalIdentifier(parts[0]);
                if (s.Length > 0) segs.Add(s);
            }
            return segs.Count == 0 ? "Package" : string.Join(".", segs);
        }

        private static string ToPascalIdentifier(string part)
        {
            var sb = new StringBuilder();
            bool upper = true;
            foreach (char c in part ?? "")
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else upper = true;
            }
            if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        private string ConvertToNamespace(string packageName) => ConvertToAsmdefName(packageName);

        // ─── Meta Generators ───

        /// <summary>대상 .meta를 만들고(이미 있으면 유지) 대상 GUID를 돌려준다.</summary>
        private string CreateMetaFromSourceOrDefault(string sourceFullPath, string destFilePath, PendingKind kind)
        {
            string sourceMeta = sourceFullPath + ".meta";
            string destMeta = destFilePath + ".meta";

            // 같은 파일 재추가(덮어쓰기) 시 기존 GUID를 유지해 패키지 내 참조가 깨지지 않게 함
            if (File.Exists(destMeta)) return ReadMetaGuid(destMeta);

            if (File.Exists(sourceMeta) && !SamePath(sourceMeta, destMeta))
            {
                string guid = GUID.Generate().ToString().Replace("-", "");
                string meta = File.ReadAllText(sourceMeta);
                meta = ReplaceGuid(meta, guid);
                File.WriteAllText(destMeta, meta);
                return guid;
            }

            if (kind == PendingKind.Script) CreateScriptMeta(destFilePath);
            else if (kind == PendingKind.Shader) CreateShaderMeta(destFilePath);
            else CreateFileMeta(destFilePath);
            return ReadMetaGuid(destMeta);
        }

        private static string ReadMetaGuid(string metaPath)
        {
            try
            {
                if (!File.Exists(metaPath)) return null;
                var m = MetaGuidRegex.Match(File.ReadAllText(metaPath));
                return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
            }
            catch
            {
                return null;
            }
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

                if (!replaced) lines.Insert(Mathf.Min(1, lines.Count), "guid: " + newGuid);
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
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd" || ext == ".gif" || ext == ".bmp" || ext == ".hdr" || ext == ".exr" || ext == ".webp"
                || ext == ".tif" || ext == ".tiff" || ext == ".iff" || ext == ".pict";
        }

        private bool IsShaderAsset(string assetPath)
        {
            string ext = Path.GetExtension(assetPath)?.ToLowerInvariant();
            return ext == ".shader" || ext == ".shadergraph" || ext == ".shadersubgraph" || ext == ".hlsl" || ext == ".cginc" || ext == ".compute"
                || ext == ".shadervariants" || ext == ".raytrace";
        }

        // Resources 폴더를 기본으로 쓰는 에셋 타입 (그 외 에셋도 모두 Resource 종류로 받되 기능 폴더가 기본)
        private bool IsResourceAsset(string assetPath)
        {
            string ext = Path.GetExtension(assetPath)?.ToLowerInvariant();
            return ext == ".mat" || ext == ".asset" || ext == ".preset" || ext == ".physicmaterial"
                || ext == ".physicsmaterial" || ext == ".cubemap" || ext == ".flare"
                || ext == ".rendertexture" || ext == ".lighting" || ext == ".guiskin"
                || ext == ".fontsettings" || ext == ".mixer" || ext == ".controller"
                || ext == ".overridecontroller" || ext == ".anim" || ext == ".mask"
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
            string name = value.Trim().Replace(" ", "");
            if (name.Length == 0 || name.Contains("..")) return "";
            foreach (char c in name)
                if (c == '/' || c == '\\' || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0) return "";
            // 구조 폴더명은 금지 — "Resources"를 기능명으로 쓰면 삭제 시 리소스 루트 전체가 지워짐
            if (name.Equals("Resources", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Runtime", StringComparison.OrdinalIgnoreCase)) return "";
            return name;
        }
    }

    /// <summary>
    /// 순서를 보존하는 최소 JSON 파서/직렬화기. package.json·asmdef를 고칠 때 UI가 모르는 키
    /// (dependencies, samples, keywords, author.email 등)를 잃지 않도록 전체 트리를 그대로 들고 다닌다.
    /// 숫자는 원문 문자열 그대로 보존한다.
    /// </summary>
    internal static class UPMJson
    {
        internal sealed class JsonObject
        {
            public readonly List<KeyValuePair<string, object>> Entries = new List<KeyValuePair<string, object>>();

            public int IndexOf(string key)
            {
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i].Key == key) return i;
                return -1;
            }

            public bool Has(string key) => IndexOf(key) >= 0;

            public object Get(string key)
            {
                int i = IndexOf(key);
                return i >= 0 ? Entries[i].Value : null;
            }

            /// <summary>있으면 같은 자리의 값만 바꾸고, 없으면 끝에 추가한다.</summary>
            public void Set(string key, object value)
            {
                int i = IndexOf(key);
                if (i >= 0) Entries[i] = new KeyValuePair<string, object>(key, value);
                else Entries.Add(new KeyValuePair<string, object>(key, value));
            }

            public void Insert(int index, string key, object value)
            {
                Entries.Insert(Mathf.Clamp(index, 0, Entries.Count), new KeyValuePair<string, object>(key, value));
            }

            public void Remove(string key)
            {
                Entries.RemoveAll(e => e.Key == key);
            }
        }

        internal sealed class JsonNumber
        {
            public readonly string Raw;
            public JsonNumber(string raw) { Raw = raw; }
            public override string ToString() => Raw;
        }

        internal sealed class JsonNull
        {
            public static readonly JsonNull Instance = new JsonNull();
            private JsonNull() { }
            public override string ToString() => "null";
        }

        internal sealed class PackageFields
        {
            public string Name, Version, DisplayName, Description, Unity, Author;
        }

        // ── Parse ──

        public static object Parse(string text)
        {
            return new Parser(text ?? "").ParseRoot();
        }

        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string text) { s = text; }

            public object ParseRoot()
            {
                if (s.Length > 0 && s[0] == '﻿') i = 1;
                SkipWs();
                object v = ParseValue(0);
                SkipWs();
                if (i != s.Length) throw Error("JSON 값 뒤에 불필요한 내용이 있습니다");
                return v;
            }

            private char Peek() => i < s.Length ? s[i] : '\0';

            private void SkipWs()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
            }

            private object ParseValue(int depth)
            {
                if (depth > 512) throw Error("중첩이 너무 깊습니다");
                SkipWs();
                if (i >= s.Length) throw Error("값이 필요합니다");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return JsonNull.Instance;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error($"예상하지 못한 문자 '{c}'");
                }
            }

            private void Expect(string literal)
            {
                if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0) throw Error($"'{literal}'이(가) 필요합니다");
                i += literal.Length;
            }

            private JsonObject ParseObject(int depth)
            {
                var obj = new JsonObject();
                i++; // {
                SkipWs();
                if (Peek() == '}') { i++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw Error("키(문자열)가 필요합니다");
                    string key = ParseString();
                    SkipWs();
                    if (Peek() != ':') throw Error("':'가 필요합니다");
                    i++;
                    object v = ParseValue(depth + 1);
                    obj.Entries.Add(new KeyValuePair<string, object>(key, v));
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { i++; continue; }
                    if (c == '}') { i++; return obj; }
                    throw Error("',' 또는 '}'가 필요합니다");
                }
            }

            private List<object> ParseArray(int depth)
            {
                var list = new List<object>();
                i++; // [
                SkipWs();
                if (Peek() == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ParseValue(depth + 1));
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { i++; continue; }
                    if (c == ']') { i++; return list; }
                    throw Error("',' 또는 ']'가 필요합니다");
                }
            }

            private string ParseString()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Error("문자열이 닫히지 않았습니다");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length) throw Error("문자열이 닫히지 않았습니다");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("잘못된 \\u 이스케이프");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int h = HexValue(s[i + k]);
                                if (h < 0) throw Error("잘못된 \\u 이스케이프");
                                code = code * 16 + h;
                            }
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default:
                            throw Error($"잘못된 이스케이프 '\\{e}'");
                    }
                }
            }

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private JsonNumber ParseNumber()
            {
                int start = i;
                if (Peek() == '-') i++;
                if (Peek() == '0') i++;
                else if (IsDigit(Peek())) { while (IsDigit(Peek())) i++; }
                else throw Error("숫자 형식 오류");
                if (Peek() == '.')
                {
                    i++;
                    if (!IsDigit(Peek())) throw Error("숫자 형식 오류");
                    while (IsDigit(Peek())) i++;
                }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    i++;
                    if (Peek() == '+' || Peek() == '-') i++;
                    if (!IsDigit(Peek())) throw Error("숫자 형식 오류");
                    while (IsDigit(Peek())) i++;
                }
                return new JsonNumber(s.Substring(start, i - start));
            }

            private FormatException Error(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++)
                {
                    if (s[k] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new FormatException($"{message} (줄 {line}, 열 {col})");
            }
        }

        // ── Serialize ──

        public static string Serialize(object value, string indent = "  ", string newline = "\n")
        {
            var sb = new StringBuilder();
            Write(sb, value, 0, indent ?? "  ", newline ?? "\n");
            return sb.ToString();
        }

        private static void WriteIndent(StringBuilder sb, string indent, int depth)
        {
            for (int k = 0; k < depth; k++) sb.Append(indent);
        }

        private static void Write(StringBuilder sb, object value, int depth, string indent, string nl)
        {
            switch (value)
            {
                case null:
                case JsonNull _:
                    sb.Append("null");
                    break;
                case string str:
                    sb.Append('"').Append(EscapeString(str)).Append('"');
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case JsonNumber n:
                    sb.Append(n.Raw);
                    break;
                case int iv:
                    sb.Append(iv.ToString(CultureInfo.InvariantCulture));
                    break;
                case JsonObject obj:
                    if (obj.Entries.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{').Append(nl);
                    for (int k = 0; k < obj.Entries.Count; k++)
                    {
                        WriteIndent(sb, indent, depth + 1);
                        sb.Append('"').Append(EscapeString(obj.Entries[k].Key)).Append("\": ");
                        Write(sb, obj.Entries[k].Value, depth + 1, indent, nl);
                        if (k < obj.Entries.Count - 1) sb.Append(',');
                        sb.Append(nl);
                    }
                    WriteIndent(sb, indent, depth);
                    sb.Append('}');
                    break;
                case List<object> list:
                    if (list.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[').Append(nl);
                    for (int k = 0; k < list.Count; k++)
                    {
                        WriteIndent(sb, indent, depth + 1);
                        Write(sb, list[k], depth + 1, indent, nl);
                        if (k < list.Count - 1) sb.Append(',');
                        sb.Append(nl);
                    }
                    WriteIndent(sb, indent, depth);
                    sb.Append(']');
                    break;
                default:
                    sb.Append('"').Append(EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture))).Append('"');
                    break;
            }
        }

        /// <summary>JSON 문자열 본문 이스케이프 (따옴표 제외). 비ASCII 문자는 그대로 둔다.</summary>
        public static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>원문에서 1단계 들여쓰기 단위(공백 n개 또는 탭)를 찾는다.</summary>
        public static string DetectIndent(string text, string fallback = "  ")
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            var m = Regex.Match(text, "\n([ \t]+)\\S");
            return m.Success ? m.Groups[1].Value : fallback;
        }

        // ── package.json ──

        private static readonly string[] CanonicalOrder = { "name", "version", "displayName", "description", "unity", "author" };

        public static PackageFields ReadPackageFields(string json)
        {
            var result = new PackageFields();
            if (!(Parse(json) is JsonObject root)) throw new FormatException("최상위 값이 객체가 아닙니다");
            result.Name = root.Get("name") as string;
            result.Version = root.Get("version") as string;
            result.DisplayName = root.Get("displayName") as string;
            result.Description = root.Get("description") as string;
            result.Unity = root.Get("unity") as string;

            object author = root.Get("author");
            if (author is JsonObject ao) result.Author = ao.Get("name") as string;
            else if (author is string astr) result.Author = AuthorStringName(astr);
            return result;
        }

        // npm 축약형 "Name <email> (url)"에서 이름 부분
        private static string AuthorStringName(string author)
        {
            int cut = author.IndexOfAny(new[] { '<', '(' });
            return (cut >= 0 ? author.Substring(0, cut) : author).Trim();
        }

        private static void SetOrdered(JsonObject root, string key, object value)
        {
            int idx = root.IndexOf(key);
            if (idx >= 0)
            {
                root.Entries[idx] = new KeyValuePair<string, object>(key, value);
                return;
            }
            // 없던 키는 표준 순서상 바로 앞 키 뒤에 넣는다
            int order = Array.IndexOf(CanonicalOrder, key);
            int insertAt = 0;
            for (int k = order - 1; k >= 0; k--)
            {
                int prev = root.IndexOf(CanonicalOrder[k]);
                if (prev >= 0) { insertAt = prev + 1; break; }
            }
            root.Insert(insertAt, key, value);
        }

        private static void SetOptional(JsonObject root, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) root.Remove(key);
            else if (!(root.Get(key) is string cur && cur == value)) SetOrdered(root, key, value);
        }

        private static void UpdateAuthor(JsonObject root, string authorName)
        {
            object author = root.Get("author");
            if (author is JsonObject ao)
            {
                if (string.IsNullOrEmpty(authorName))
                {
                    ao.Remove("name");
                    if (ao.Entries.Count == 0) root.Remove("author");
                }
                else if (!(ao.Get("name") is string cur && cur == authorName))
                {
                    if (ao.Has("name")) ao.Set("name", authorName);
                    else ao.Insert(0, "name", authorName);
                }
                return;
            }
            if (author is string astr)
            {
                if (string.IsNullOrEmpty(authorName)) { root.Remove("author"); return; }
                if (AuthorStringName(astr) == authorName) return;
                int cut = astr.IndexOfAny(new[] { '<', '(' });
                string rest = cut >= 0 ? " " + astr.Substring(cut).Trim() : "";
                SetOrdered(root, "author", authorName + rest);
                return;
            }
            if (string.IsNullOrEmpty(authorName)) return;
            var obj = new JsonObject();
            obj.Set("name", authorName);
            SetOrdered(root, "author", obj);
        }

        /// <summary>
        /// original(null이면 새 파일)에서 UI가 관리하는 키(name, version, displayName, description, unity, author.name)만 바꾼 JSON을 돌려준다.
        /// 나머지 키·순서·숫자 표기는 그대로이며, 들여쓰기·줄바꿈·마지막 줄바꿈은 원문을 따른다. 해석 실패 시 FormatException.
        /// </summary>
        public static string UpdatePackageJson(string original, string name, string version, string displayName,
            string description, string unity, string author)
        {
            JsonObject root;
            string indent = "  ";
            string nl = "\n";
            bool finalNewline = true;

            if (!string.IsNullOrWhiteSpace(original))
            {
                root = Parse(original) as JsonObject;
                if (root == null) throw new FormatException("최상위 값이 객체가 아닙니다");
                indent = DetectIndent(original, "  ");
                nl = original.Contains("\r\n") ? "\r\n" : "\n";
                finalNewline = original.EndsWith("\n", StringComparison.Ordinal);
            }
            else root = new JsonObject();

            if (!(root.Get("name") is string curName && curName == name)) SetOrdered(root, "name", name ?? "");
            if (!(root.Get("version") is string curVer && curVer == version)) SetOrdered(root, "version", version ?? "");
            SetOptional(root, "displayName", displayName);
            SetOptional(root, "description", description);
            SetOptional(root, "unity", unity);
            UpdateAuthor(root, author);

            return Serialize(root, indent, nl) + (finalNewline ? nl : "");
        }
    }
}
