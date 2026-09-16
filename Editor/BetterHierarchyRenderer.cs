using System.Text;
using UnityEngine;

namespace BetterTabs
{
    // Transform path helpers shared by the GameObject view. Paths are how a tab
    // remembers its hierarchy selection across domain reloads, since Transform
    // references do not survive them.
    internal static class BetterHierarchyRenderer
    {
        public static string GetPath(Transform t)
        {
            if (t == null) return "";

            StringBuilder sb = new StringBuilder(t.name);
            Transform parent = t.parent;
            while (parent != null)
            {
                sb.Insert(0, "/");
                sb.Insert(0, parent.name);
                parent = parent.parent;
            }
            return sb.ToString();
        }

        public static Transform FindByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return root;

            string rootPath = GetPath(root);
            if (path == rootPath) return root;
            if (!path.StartsWith(rootPath + "/")) return null;

            string[] parts = path.Substring(rootPath.Length + 1).Split('/');
            Transform current = root;
            foreach (string part in parts)
            {
                Transform next = null;
                for (int i = 0; i < current.childCount; i++)
                {
                    if (current.GetChild(i).name != part) continue;
                    next = current.GetChild(i);
                    break;
                }
                if (next == null) return null;
                current = next;
            }
            return current;
        }
    }
}
