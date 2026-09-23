using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;

namespace TelleR.Util.FastClone
{
    public class FastCloneWindow : EditorWindow
    {
        private ScrollView listContainer;
        private Label countLabel;
        private Button createBtn;

        [MenuItem("Tools/TelleR/Fast Clone", false, 140)]
        public static void ShowWindow()
        {
            var wnd = GetWindow<FastCloneWindow>();
            wnd.titleContent = new GUIContent("Fast Clone");
            wnd.minSize = new Vector2(420, 300);
        }

        private void OnEnable()
        {
            FastCloneCore.StateChanged += RefreshCloneList;
        }

        private void OnDisable()
        {
            FastCloneCore.StateChanged -= RefreshCloneList;
        }

        // 다른 창에서 폴더를 지우거나 클론 에디터를 닫고 돌아왔을 때 상태를 다시 읽는다.
        private void OnFocus()
        {
            RefreshCloneList();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingTop = 15;
            root.style.paddingLeft = 15;
            root.style.paddingRight = 15;
            root.style.paddingBottom = 10;

            bool isClone = FastCloneCore.IsClone();
            var title = new Label(isClone ? "CLONE PROJECT" : "ORIGINAL PROJECT");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            title.style.alignSelf = Align.Center;

            if (isClone) title.style.color = TelleRGUI.Danger;
            root.Add(title);

            if (isClone)
            {
                var help = new HelpBox(
                    "이 클론의 Assets / Packages / ProjectSettings는 원본 프로젝트와 정션(심볼릭 링크)으로 공유됩니다.\n" +
                    "여기서 에셋을 삭제·수정하면 원본 프로젝트에 즉시 반영됩니다 — 읽기 전용 보호는 없습니다.\n" +
                    "클론 관리는 원본 프로젝트에서 하세요.", HelpBoxMessageType.Warning);
                root.Add(help);

                var original = new Label("원본 프로젝트: " + FastCloneCore.GetOriginalProjectPath());
                original.style.marginTop = 6;
                original.style.color = TelleRGUI.HintText;
                original.style.whiteSpace = WhiteSpace.Normal;
                root.Add(original);
                return;
            }

            var hint = new Label("클론은 원본과 Assets · Packages · ProjectSettings를 공유합니다. 클론에서 바꾼 에셋은 원본에도 바로 반영됩니다.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color = TelleRGUI.HintText;
            hint.style.fontSize = 11;
            hint.style.marginBottom = 8;
            root.Add(hint);

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.marginBottom = 6;
            countLabel = new Label();
            countLabel.style.flexGrow = 1;
            countLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolbar.Add(countLabel);
            var refreshBtn = new Button(RefreshCloneList) { text = "Refresh", tooltip = "클론 폴더 상태를 다시 읽습니다." };
            toolbar.Add(refreshBtn);
            root.Add(toolbar);

            listContainer = new ScrollView();
            listContainer.style.flexGrow = 1;
            root.Add(listContainer);

            var createContainer = new VisualElement();
            createContainer.style.paddingTop = 10;
            createContainer.style.borderTopWidth = 1;
            createContainer.style.borderTopColor = TelleRGUI.Separator;

            createBtn = new Button(() => FastCloneCore.CreateNextClone(RefreshCloneList));
            createBtn.text = "+ Create New Clone";
            createBtn.style.height = 35;
            createBtn.style.fontSize = 14;

            createContainer.Add(createBtn);
            root.Add(createContainer);

            RefreshCloneList();
        }

        private void RefreshCloneList()
        {
            if (listContainer == null) return;
            listContainer.Clear();
            var entries = FastCloneCore.GetCloneEntries();

            if (countLabel != null) countLabel.text = $"Clones: {entries.Count} / {FastCloneCore.MaxCloneCount}";
            if (createBtn != null)
            {
                bool canCreate = !FastCloneCore.IsCopying && entries.Count < FastCloneCore.MaxCloneCount;
                createBtn.SetEnabled(canCreate);
                createBtn.tooltip = FastCloneCore.IsCopying ? "클론 복사가 진행 중입니다."
                    : canCreate ? "Library를 복사하고 나머지는 링크로 공유하는 클론을 만듭니다."
                    : "클론 슬롯이 가득 찼습니다. 쓰지 않는 클론을 삭제하세요.";
            }

            if (entries.Count == 0)
            {
                var emptyLabel = new Label("아직 만든 클론이 없습니다.");
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.marginTop = 20;
                emptyLabel.style.color = TelleRGUI.HintText;
                listContainer.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < entries.Count; i++)
                listContainer.Add(BuildRow(entries[i], i));
        }

        private VisualElement BuildRow(CloneEntry entry, int index)
        {
            string path = entry.Path;
            string folderName = Path.GetFileName(path);

            var row = new VisualElement();
            row.style.marginBottom = 5;
            row.style.backgroundColor = index % 2 == 0 ? TelleRGUI.RowBg : TelleRGUI.RowBgAlt;
            row.style.borderTopLeftRadius = 5;
            row.style.borderTopRightRadius = 5;
            row.style.borderBottomLeftRadius = 5;
            row.style.borderBottomRightRadius = 5;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 5;

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            row.Add(line);

            var label = new Label(folderName) { tooltip = path };
            label.style.flexGrow = 1;
            label.style.fontSize = 13;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            line.Add(label);

            string note = null;
            switch (entry.State)
            {
                case CloneState.Ready:
                    if (FastCloneCore.IsProjectOpen(path, out bool certain))
                        line.Add(MakeBadge(certain ? "열림" : "열림?", TelleRGUI.Success,
                            certain ? "다른 Unity 에디터에서 열려 있습니다." : "잠금 파일이 있습니다. 열려 있거나 비정상 종료 뒤 남은 잠금입니다."));
                    break;
                case CloneState.Copying:
                    line.Add(MakeBadge("복사 중", TelleRGUI.Accent, "Library를 복사하고 있습니다."));
                    note = "복사 중입니다. 진행 표시줄의 취소 버튼으로 중단할 수 있습니다.";
                    break;
                case CloneState.Incomplete:
                    line.Add(MakeBadge("불완전", TelleRGUI.Warning, "생성이 끝나지 않은 폴더입니다."));
                    note = entry.HasCloneTrace
                        ? "생성이 끝나지 않은 클론입니다(중단·실패). 삭제한 뒤 다시 만드세요."
                        : "Fast Clone 표식이 없는 폴더가 클론 슬롯을 차지하고 있습니다. 직접 만든 폴더인지 확인하세요.";
                    break;
            }

            var openBtn = new Button(() =>
            {
                FastCloneCore.OpenCloneProject(path);
                RefreshCloneList();
            });
            openBtn.text = "Open";
            openBtn.style.width = 60;
            openBtn.SetEnabled(entry.State == CloneState.Ready);
            line.Add(openBtn);

            var delBtn = new Button(() => ConfirmAndDelete(entry));
            delBtn.text = "Delete";
            delBtn.style.width = 60;
            delBtn.style.backgroundColor = TelleRGUI.Danger;
            delBtn.style.color = ContrastText(TelleRGUI.Danger);
            delBtn.SetEnabled(entry.State != CloneState.Copying);
            line.Add(delBtn);

            if (note != null)
            {
                var noteLabel = new Label(note);
                noteLabel.style.whiteSpace = WhiteSpace.Normal;
                noteLabel.style.fontSize = 11;
                noteLabel.style.color = TelleRGUI.HintText;
                noteLabel.style.marginTop = 2;
                row.Add(noteLabel);
            }
            return row;
        }

        private static Label MakeBadge(string text, Color background, string tooltip)
        {
            var badge = new Label(text) { tooltip = tooltip };
            badge.style.backgroundColor = background;
            badge.style.color = ContrastText(background);
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.fontSize = 10;
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.marginRight = 6;
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;
            return badge;
        }

        // 스킨마다 배경 밝기가 달라지므로(Dark: 밝은 연어색, Light: 짙은 빨강) 글자색을 배경 밝기로 고른다.
        private static Color ContrastText(Color background)
        {
            return background.grayscale > 0.5f ? Color.black : Color.white;
        }

        private void ConfirmAndDelete(CloneEntry entry)
        {
            string folderName = Path.GetFileName(entry.Path);
            string message = entry.HasCloneTrace
                ? $"'{folderName}' 폴더를 삭제합니다.\n\n" +
                  "삭제되는 것:\n" +
                  "• 클론 전용 Library 캐시 (수 GB일 수 있음)\n" +
                  "• 클론 전용 UserSettings · Logs · Temp\n\n" +
                  "유지되는 것:\n" +
                  "• 원본 프로젝트의 Assets · Packages · ProjectSettings (링크만 해제)\n\n" +
                  entry.Path
                // 표식도 링크된 Assets도 없는 폴더: 링크가 아닌 실제 파일이므로 모두 지워진다.
                : $"'{folderName}' 폴더를 삭제합니다.\n\n" +
                  "주의: 이 폴더에는 Fast Clone 표식이 없습니다.\n" +
                  "이 폴더와 안의 모든 파일(Assets 포함)이 삭제되며 되돌릴 수 없습니다.\n\n" +
                  entry.Path;

            if (TelleRGUI.Confirm("클론 삭제", message, "삭제", "취소"))
            {
                FastCloneCore.DeleteClone(entry.Path);
                RefreshCloneList();
            }
        }
    }
}
