using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;

namespace TelleR.Util.FastClone
{
    public class FastCloneWindow : EditorWindow
    {
        [MenuItem("Tools/TelleR/Tool/Clones Manager")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<FastCloneWindow>();
            wnd.titleContent = new GUIContent("Clones Manager");
            wnd.minSize = new Vector2(400, 300);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingTop = 15;
            root.style.paddingLeft = 15;
            root.style.paddingRight = 15;

            bool isClone = FastCloneCore.IsClone();
            var title = new Label(isClone ? "CLONE PROJECT (Read-Only)" : "ORIGINAL PROJECT");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 15;
            title.style.alignSelf = Align.Center;

            if (isClone) title.style.color = new Color(1f, 0.4f, 0.4f);
            root.Add(title);

            if (isClone)
            {
                var help = new HelpBox("This is a clone project.\nPlease manage clones from the original project.", HelpBoxMessageType.Warning);
                root.Add(help);
                return;
            }

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            RefreshCloneList(scroll);

            var createContainer = new VisualElement();
            createContainer.style.paddingTop = 10;
            createContainer.style.borderTopWidth = 1;
            createContainer.style.borderTopColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

            var createBtn = new Button(() =>
            {
                FastCloneCore.CreateNextClone(() =>
                {
                    RefreshCloneList(scroll);
                });
            });
            createBtn.text = "+ Create New Clone";
            createBtn.style.height = 35;
            createBtn.style.fontSize = 14;

            createContainer.Add(createBtn);
            root.Add(createContainer);
        }

        private void RefreshCloneList(VisualElement container)
        {
            container.Clear();
            var clonePaths = FastCloneCore.GetAllClonePaths();

            if (clonePaths.Count == 0)
            {
                var emptyLabel = new Label("No clones created.");
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.marginTop = 20;
                emptyLabel.style.color = Color.gray;
                container.Add(emptyLabel);
                return;
            }

            foreach (var path in clonePaths)
            {
                string folderName = Path.GetFileName(path);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 5;
                row.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);

                row.style.borderTopLeftRadius = 5;
                row.style.borderTopRightRadius = 5;
                row.style.borderBottomLeftRadius = 5;
                row.style.borderBottomRightRadius = 5;

                row.style.paddingTop = 5;
                row.style.paddingBottom = 5;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 5;
                row.style.alignItems = Align.Center;

                var label = new Label(folderName);
                label.style.flexGrow = 1;
                label.style.fontSize = 13;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(label);

                var openBtn = new Button(() => FastCloneCore.OpenCloneProject(path));
                openBtn.text = "Open";
                openBtn.style.width = 60;
                row.Add(openBtn);

                var delBtn = new Button(() =>
                {
                    if (EditorUtility.DisplayDialog("Delete Clone", $"Delete '{folderName}'?", "Delete", "Cancel"))
                    {
                        FastCloneCore.DeleteClone(path);
                        RefreshCloneList(container);
                    }
                });
                delBtn.text = "X";
                delBtn.style.width = 30;
                delBtn.style.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
                row.Add(delBtn);

                container.Add(row);
            }
        }
    }
}
