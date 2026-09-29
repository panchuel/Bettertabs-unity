namespace BetterTabs
{
    using System.IO;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;
    using UnityEngine.UIElements;

    // The chip drawn next to the cursor while something is dragged: the first item's
    // icon on a badge washed with its type colour, its name, and a count plus a card
    // stacked behind once more than one item moves.
    internal class BetterDragGhostView : VisualElement
    {
        private const string USS_CLASS = "bt-drag-ghost";
        private const string MULTI_CLASS = USS_CLASS + "--multi";
        private const string FOLDER_ICON = "Folder Icon";
        // Same wash the asset inspector puts behind its type badge.
        private const float BADGE_WASH_ALPHA = 0.14f;

        private static readonly char[] PATH_SEPARATORS = { '/', '\\' };

        private readonly VisualElement badge;
        private readonly Image icon;
        private readonly Label label;
        private readonly Label count;

        public BetterDragGhostView(StyleSheet sheet)
        {
            AddToClassList(USS_CLASS);
            pickingMode = PickingMode.Ignore;
            if (sheet != null)
            {
                styleSheets.Add(sheet);
            }

            Add(CreatePart<VisualElement>("__stack"));

            VisualElement chip = CreatePart<VisualElement>("__chip");
            Add(chip);

            badge = CreatePart<VisualElement>("__badge");
            chip.Add(badge);

            icon = CreatePart<Image>("__icon");
            icon.scaleMode = ScaleMode.ScaleToFit;
            badge.Add(icon);

            label = CreatePart<Label>("__label");
            chip.Add(label);

            count = CreatePart<Label>("__count");
            chip.Add(count);
        }

        public void SetPayload(Object[] objects, string[] paths)
        {
            Texture image = null;
            Color tint = Color.white;
            string text = string.Empty;

            if (objects.Length > 0 && objects[0] != null)
            {
                DescribeObject(objects[0], out image, out tint, out text);
            }
            else if (paths.Length > 0)
            {
                DescribePath(paths[0], out image, out tint, out text);
            }

            icon.image = image;
            icon.tintColor = tint;
            badge.style.backgroundColor = new Color(tint.r, tint.g, tint.b, BADGE_WASH_ALPHA);
            label.text = text;

            int total = Mathf.Max(objects.Length, paths.Length);
            count.text = total.ToString();
            EnableInClassList(MULTI_CLASS, total > 1);
        }

        private static TElement CreatePart<TElement>(string suffix) where TElement : VisualElement, new()
        {
            TElement part = new TElement();
            part.AddToClassList(USS_CLASS + suffix);
            part.pickingMode = PickingMode.Ignore;
            return part;
        }

        private static void DescribeObject(Object target, out Texture image, out Color tint, out string text)
        {
            text = target.name;

            string path = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(path))
            {
                image = AssetDatabase.IsMainAsset(target)
                    ? AssetDatabase.GetCachedIcon(path)
                    : AssetPreview.GetMiniThumbnail(target);
                tint = BetterAssetTypeColors.TintForPath(path);
                return;
            }

            // A scene object: coloured the way BetterTabs colours its hierarchy rows.
            image = AssetPreview.GetMiniThumbnail(target);
            tint = target is GameObject ? BetterAssetTypeColors.GameObjectTint : Color.white;
        }

        // Paths with no object behind them are files dragged in from the OS.
        private static void DescribePath(string path, out Texture image, out Color tint, out string text)
        {
            text = Path.GetFileName(path.TrimEnd(PATH_SEPARATORS));

            if (AssetDatabase.IsValidFolder(path) || Directory.Exists(path))
            {
                image = EditorGUIUtility.IconContent(FOLDER_ICON).image;
                tint = BetterAssetTypeColors.FolderTint;
                return;
            }

            image = InternalEditorUtility.GetIconForFile(path);
            tint = BetterAssetTypeColors.TintForPath(path);
        }
    }
}
