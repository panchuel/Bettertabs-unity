using System.Text;
using UnityEngine;

namespace BetterTabs
{
    // Transform path helpers shared by the GameObject view. Paths are how a tab
    // remembers its hierarchy selection across domain reloads, since Transform
    // references do not survive them.
    //
    // Siblings may share a name ("holder", "holder", "holder" is common in VFX
    // prefabs), so a segment carries the object's position among its same-named
    // siblings: "holder", "holder[2]", "holder[3]". The first keeps the bare name,
    // which also keeps every path saved before this rule pointing where it did.
    internal static class BetterHierarchyRenderer
    {
        private const string ORDINAL_FORMAT = "{0}[{1}]";
        private const char ORDINAL_OPEN = '[';
        private const char ORDINAL_CLOSE = ']';
        private const char PATH_SEPARATOR = '/';

        public static string GetPath(Transform t)
        {
            if (t == null)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(GetSegment(t));
            Transform parent = t.parent;
            while (parent != null)
            {
                sb.Insert(0, PATH_SEPARATOR);
                sb.Insert(0, GetSegment(parent));
                parent = parent.parent;
            }
            return sb.ToString();
        }

        // ordinal is 1 for the first sibling with this name, 2 for the second, etc.
        public static string FormatSegment(string name, int ordinal)
        {
            return ordinal <= 1 ? name : string.Format(ORDINAL_FORMAT, name, ordinal);
        }

        public static Transform FindByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return root;
            }

            string rootPath = GetPath(root);
            if (path == rootPath)
            {
                return root;
            }
            if (!path.StartsWith(rootPath + PATH_SEPARATOR))
            {
                return null;
            }

            string[] parts = path.Substring(rootPath.Length + 1).Split(PATH_SEPARATOR);
            Transform current = root;
            foreach (string part in parts)
            {
                current = FindChild(current, part);
                if (current == null)
                {
                    return null;
                }
            }
            return current;
        }

        // Scene roots have no parent to count siblings in; the root segment is only
        // ever compared against itself, so the bare name is enough there.
        private static string GetSegment(Transform t)
        {
            Transform parent = t.parent;
            if (parent == null)
            {
                return t.name;
            }

            int ordinal = 1;
            int index = t.GetSiblingIndex();
            for (int i = 0; i < index; i++)
            {
                if (parent.GetChild(i).name == t.name)
                {
                    ordinal++;
                }
            }
            return FormatSegment(t.name, ordinal);
        }

        private static Transform FindChild(Transform parent, string segment)
        {
            string name;
            int ordinal;
            if (TryParseOrdinal(segment, out name, out ordinal))
            {
                int seen = 0;
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child.name == name && ++seen == ordinal)
                    {
                        return child;
                    }
                }
            }

            // A bare name, or an object whose own name happens to end in "[n]".
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == segment)
                {
                    return child;
                }
            }
            return null;
        }

        private static bool TryParseOrdinal(string segment, out string name, out int ordinal)
        {
            name = segment;
            ordinal = 1;

            int close = segment.Length - 1;
            if (close < 0 || segment[close] != ORDINAL_CLOSE)
            {
                return false;
            }

            int open = segment.LastIndexOf(ORDINAL_OPEN);
            if (open <= 0)
            {
                return false;
            }

            int parsed;
            if (!int.TryParse(segment.Substring(open + 1, close - open - 1), out parsed) || parsed < 2)
            {
                return false;
            }

            name = segment.Substring(0, open);
            ordinal = parsed;
            return true;
        }
    }
}
