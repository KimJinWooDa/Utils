#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace TelleR
{
    // Package Manager에서 이 패키지를 선택하면 Version History 탭 안(버전 목록 아래)에
    // CHANGELOG.md 내용을 직접 표시한다. 기본 확장 슬롯은 탭 위쪽이라 보기 흉하므로
    // 내부 요소 "versionsTab"(Unity 6000.3 기준)으로 이동시키고, 못 찾으면 접힌 Foldout으로 폴백한다.
    [InitializeOnLoad]
    internal class PackageChangelogExtension : IPackageManagerExtension
    {
        private const string PackageName = "com.teller.util";
        private const string VersionsTabElementName = "versionsTab";

        private VisualElement placeholder;   // Unity가 배치하는 기본 슬롯 — 폴백일 때만 표시
        private VisualElement contentRoot;   // 실제 체인지로그 패널
        private ScrollView scrollView;
        private Foldout fallbackFoldout;
        private bool showForCurrentSelection;

        static PackageChangelogExtension()
        {
            PackageManagerExtensions.RegisterExtension(new PackageChangelogExtension());
        }

        public VisualElement CreateExtensionUI()
        {
            placeholder = new VisualElement();
            placeholder.style.display = DisplayStyle.None;

            contentRoot = new VisualElement();
            contentRoot.style.display = DisplayStyle.None;
            contentRoot.style.marginTop = 8;
            contentRoot.style.marginBottom = 8;

            var title = new Label("변경 이력 (CHANGELOG)");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13;
            title.style.marginBottom = 4;
            contentRoot.Add(title);

            scrollView = new ScrollView();
            scrollView.style.maxHeight = 320;
            scrollView.style.backgroundColor = new Color(0f, 0f, 0f, 0.2f);
            scrollView.style.paddingTop = 6;
            scrollView.style.paddingBottom = 6;
            scrollView.style.paddingLeft = 10;
            scrollView.style.paddingRight = 10;
            contentRoot.Add(scrollView);

            return placeholder;
        }

        public void OnPackageSelectionChange(PackageInfo packageInfo)
        {
            showForCurrentSelection = packageInfo != null && packageInfo.name == PackageName && TryLoadChangelog(packageInfo);
            // 선택 직후에는 상세 UI가 아직 조립 전일 수 있어 한 프레임 뒤에 배치한다
            EditorApplication.delayCall += ApplyPlacement;
        }

        private void ApplyPlacement()
        {
            if (contentRoot == null || placeholder == null) return;

            if (!showForCurrentSelection)
            {
                contentRoot.style.display = DisplayStyle.None;
                placeholder.style.display = DisplayStyle.None;
                return;
            }

            VisualElement versionsTab = null;
            if (placeholder.panel != null)
                versionsTab = placeholder.panel.visualTree.Q<VisualElement>(VersionsTabElementName);

            if (versionsTab != null)
            {
                // Version History 탭 안, 버전 목록 아래에 표시 (탭이 숨겨지면 함께 숨겨짐)
                if (contentRoot.parent != versionsTab)
                {
                    contentRoot.RemoveFromHierarchy();
                    versionsTab.Add(contentRoot);
                }
                contentRoot.style.display = DisplayStyle.Flex;
                placeholder.style.display = DisplayStyle.None;
            }
            else
            {
                // 내부 이름이 다른 Unity 버전 폴백: 기본 슬롯에 접힌 Foldout으로 조용히 표시
                if (fallbackFoldout == null)
                {
                    fallbackFoldout = new Foldout { text = "변경 이력 (CHANGELOG)", value = false };
                    placeholder.Add(fallbackFoldout);
                }
                if (contentRoot.parent != fallbackFoldout)
                {
                    contentRoot.RemoveFromHierarchy();
                    fallbackFoldout.Add(contentRoot);
                }
                contentRoot.style.display = DisplayStyle.Flex;
                placeholder.style.display = DisplayStyle.Flex;
            }
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
                    if (line.StartsWith("# ")) continue; // 문서 제목은 고정 타이틀로 대체

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
