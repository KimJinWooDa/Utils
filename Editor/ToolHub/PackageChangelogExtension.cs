#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace TelleR
{
    // Package Manager에서 이 패키지를 선택하면 상세 화면 하단에 CHANGELOG.md 내용을 직접 표시한다.
    // (기본 Package Manager는 changelogUrl 링크만 지원하고 본문은 보여주지 않음)
    [InitializeOnLoad]
    internal class PackageChangelogExtension : IPackageManagerExtension
    {
        private const string PackageName = "com.teller.util";

        private VisualElement root;
        private ScrollView scrollView;

        static PackageChangelogExtension()
        {
            PackageManagerExtensions.RegisterExtension(new PackageChangelogExtension());
        }

        public VisualElement CreateExtensionUI()
        {
            root = new VisualElement();
            root.style.display = DisplayStyle.None;
            root.style.marginTop = 8;
            root.style.marginBottom = 8;

            var title = new Label("변경 이력 (CHANGELOG)");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13;
            title.style.marginBottom = 4;
            root.Add(title);

            scrollView = new ScrollView();
            scrollView.style.maxHeight = 280;
            scrollView.style.backgroundColor = new Color(0f, 0f, 0f, 0.2f);
            scrollView.style.paddingTop = 6;
            scrollView.style.paddingBottom = 6;
            scrollView.style.paddingLeft = 10;
            scrollView.style.paddingRight = 10;
            root.Add(scrollView);

            return root;
        }

        public void OnPackageSelectionChange(PackageInfo packageInfo)
        {
            if (root == null) return;

            bool show = packageInfo != null && packageInfo.name == PackageName && TryLoadChangelog(packageInfo);
            root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool TryLoadChangelog(PackageInfo info)
        {
            try
            {
                string path = Path.Combine(info.resolvedPath, "CHANGELOG.md");
                if (!File.Exists(path)) return false;

                scrollView.Clear();
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.TrimEnd();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("# ")) continue; // 문서 제목은 위 고정 타이틀로 대체

                    var label = new Label();
                    label.style.whiteSpace = WhiteSpace.Normal;

                    if (line.StartsWith("## "))
                    {
                        label.text = line.Substring(3);
                        label.style.unityFontStyleAndWeight = FontStyle.Bold;
                        label.style.fontSize = 12;
                        label.style.marginTop = 8;
                    }
                    else if (line.StartsWith("### "))
                    {
                        label.text = line.Substring(4);
                        label.style.unityFontStyleAndWeight = FontStyle.Bold;
                        label.style.marginTop = 4;
                    }
                    else if (line.StartsWith("- "))
                    {
                        label.text = "•  " + StripMarkdown(line.Substring(2));
                        label.style.marginLeft = 8;
                    }
                    else
                    {
                        label.text = StripMarkdown(line);
                    }

                    scrollView.Add(label);
                }
                return scrollView.childCount > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string StripMarkdown(string s)
        {
            return s.Replace("**", "").Replace("`", "");
        }

        public void OnPackageAddedOrUpdated(PackageInfo packageInfo) { }
        public void OnPackageRemoved(PackageInfo packageInfo) { }
    }
}
#endif
