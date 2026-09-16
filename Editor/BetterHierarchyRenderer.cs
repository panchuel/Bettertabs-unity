using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    // Renders a GameObject hierarchy as a tree (similar to Unity's Hierarchy
    // window). Used by Prefab and SceneObject tabs.
    internal class BetterHierarchyRenderer
    {
        const int RowHeight = 18;
        const int IndentWidth = 14;

        readonly HashSet<string> _expanded = new HashSet<string>();

        Transform _root;
        string _selectedPath;
        System.Action<string> _onSelected;

        public void Setup(Transform root, string selectedPath, System.Action<string> onSelected)
        {
            _root = root;
            _selectedPath = selectedPath;
            _onSelected = onSelected;
        }

        public void ExpandPathChain(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            int i = 0;
            while ((i = path.IndexOf('/', i + 1)) > 0)
                _expanded.Add(path.Substring(0, i));
            _expanded.Add(path);
        }

        public float MeasureHeight()
        {
            if (_root == null) return 0f;
            return MeasureNode(_root, 0);
        }

        float MeasureNode(Transform t, int depth)
        {
            float h = RowHeight;
            string path = GetPath(t);
            if (IsExpanded(path))
            {
                for (int i = 0; i < t.childCount; i++)
                    h += MeasureNode(t.GetChild(i), depth + 1);
            }
            return h;
        }

        public void Draw(float contentWidth)
        {
            if (_root == null) return;
            DrawNode(_root, 0, contentWidth);
        }

        void DrawNode(Transform t, int depth, float contentWidth)
        {
            string path = GetPath(t);
            bool isSelected = path == _selectedPath;
            bool hasChildren = t.childCount > 0;
            bool expanded = IsExpanded(path);

            var rect = GUILayoutUtility.GetRect(contentWidth, RowHeight);
            float indent = depth * IndentWidth + 2;
            var ev = Event.current;

            // Background
            if (isSelected)
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 1f));
            else
            {
                int row = Mathf.RoundToInt(rect.y / RowHeight);
                if (row % 2 == 0)
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.04f));
            }

            // Foldout arrow
            if (hasChildren)
            {
                var foldoutRect = new Rect(rect.x + indent, rect.y + 1, 14, rect.height - 2);
                bool now = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                if (now != expanded)
                {
                    if (now) _expanded.Add(path); else _expanded.Remove(path);
                }
            }

            // Icon
            float iconX = rect.x + indent + 14;
            var icon = PrefabUtility.GetIconForGameObject(t.gameObject) as Texture2D;
            if (icon == null) icon = EditorGUIUtility.IconContent("GameObject Icon").image as Texture2D;
            if (icon != null)
                GUI.DrawTexture(new Rect(iconX, rect.y + 1, 16, 16), icon, ScaleMode.ScaleToFit);

            // Label
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = isSelected ? Color.white : EditorStyles.miniLabel.normal.textColor }
            };
            if (!t.gameObject.activeInHierarchy && !isSelected)
                labelStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(iconX + 18, rect.y, rect.width - iconX - 22, rect.height),
                t.gameObject.name, labelStyle);

            // Click
            if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition) && ev.button == 0)
            {
                _onSelected?.Invoke(path);
                ev.Use();
            }

            // Recurse
            if (expanded)
            {
                for (int i = 0; i < t.childCount; i++)
                    DrawNode(t.GetChild(i), depth + 1, contentWidth);
            }
        }

        bool IsExpanded(string path) => _expanded.Contains(path);

        public static string GetPath(Transform t)
        {
            if (t == null) return "";
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null)
            {
                sb.Insert(0, "/");
                sb.Insert(0, p.name);
                p = p.parent;
            }
            return sb.ToString();
        }

        public static Transform FindByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return root;
            string rootPath = GetPath(root);
            if (path == rootPath) return root;
            if (!path.StartsWith(rootPath + "/")) return null;

            string remaining = path.Substring(rootPath.Length + 1);
            string[] parts = remaining.Split('/');
            Transform current = root;
            foreach (var part in parts)
            {
                Transform next = null;
                for (int i = 0; i < current.childCount; i++)
                {
                    if (current.GetChild(i).name == part)
                    {
                        next = current.GetChild(i);
                        break;
                    }
                }
                if (next == null) return null;
                current = next;
            }
            return current;
        }
    }
}
