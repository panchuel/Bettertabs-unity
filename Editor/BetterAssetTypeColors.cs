using System;
using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    // Per-type icon colours from the redesign spec. Unity's own icons are kept and
    // tinted rather than replaced: USS cannot draw the spec's line art without the
    // Vector Graphics package, and a tint still gives the quick type read the spec
    // asks for while keeping every icon the user already recognises.
    internal static class BetterAssetTypeColors
    {
        static readonly Color Folder = new Color(0.851f, 0.706f, 0.361f);   // #d9b45c
        static readonly Color Scene = new Color(0.937f, 0.553f, 0.361f);    // #ef8d5c
        static readonly Color Script = new Color(0.545f, 0.718f, 0.867f);   // #8bb7dd
        static readonly Color GameObject = new Color(0.651f, 0.447f, 0.851f); // #a672d9
        static readonly Color Data = new Color(0.482f, 0.851f, 0.769f);     // #7bd9c4

        // Shared with the hierarchy rows and the inspector badge.
        public static Color GameObjectTint => GameObject;
        // For folders outside the asset database (dragged in from the OS).
        public static Color FolderTint => Folder;

        // Folders the project keeps but no longer builds against. Name-based because
        // nothing in the asset database marks them; extend the list per project.
        static readonly string[] DeprecatedMarkers = { "(old)", "(deprecated)", "(obsolete)", "_old" };

        // White leaves Unity's own icon colours untouched, which is what an asset
        // with no type of its own in the spec should do.
        public static Color TintFor(BetterTabEntry tab)
        {
            if (tab == null) return Color.white;
            if (tab.kind == BetterTabKind.SceneObject || tab.kind == BetterTabKind.Prefab)
                return GameObject;
            return TintForPath(tab.path);
        }

        public static Color TintForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return Color.white;
            if (AssetDatabase.IsValidFolder(path)) return Folder;

            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".unity": return Scene;
                case ".cs": return Script;
                case ".prefab": return GameObject;
                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".aiff":
                case ".aif":
                case ".mixer": return Data;
                case ".asset": return Data;
                default: return Color.white;
            }
        }

        public static bool IsDeprecated(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            for (int i = 0; i < DeprecatedMarkers.Length; i++)
            {
                if (name.EndsWith(DeprecatedMarkers[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
