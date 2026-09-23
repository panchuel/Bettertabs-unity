using UnityEngine;

namespace BetterTabs
{
    // Fixed palette used to tag tabs with a colour. Tabs persist the palette INDEX,
    // so entries may only ever be appended: inserting or reordering one would
    // silently recolour every tab already saved in EditorPrefs.
    internal static class BetterTabColors
    {
        public const int None = 0;

        // Row background: the tagged row takes the full tone, everything under it a
        // darker and fainter one, so the subtree reads as belonging to it.
        public const float RowAlpha = 0.55f;
        public const float ChildRowAlpha = 0.40f;
        public const float ChildDarken = 0.62f;

        const int GradientWidth = 64;

        readonly struct Swatch
        {
            public readonly string Name;
            public readonly Color Color;

            public Swatch(string name, Color color)
            {
                Name = name;
                Color = color;
            }
        }

        static readonly Swatch[] Palette =
        {
            new Swatch("None",   Color.clear),
            new Swatch("Red",    new Color(0.83f, 0.29f, 0.26f)),
            new Swatch("Orange", new Color(0.88f, 0.53f, 0.22f)),
            new Swatch("Yellow", new Color(0.87f, 0.75f, 0.24f)),
            new Swatch("Olive",  new Color(0.68f, 0.73f, 0.22f)),
            new Swatch("Green",  new Color(0.31f, 0.69f, 0.35f)),
            new Swatch("Teal",   new Color(0.23f, 0.71f, 0.58f)),
            new Swatch("Cyan",   new Color(0.27f, 0.68f, 0.79f)),
            new Swatch("Blue",   new Color(0.29f, 0.53f, 0.83f)),
            new Swatch("Purple", new Color(0.61f, 0.42f, 0.84f)),
            new Swatch("Pink",   new Color(0.87f, 0.42f, 0.65f)),
            new Swatch("Grey",   new Color(0.60f, 0.63f, 0.65f))
        };

        public static int Count => Palette.Length;

        // False for None and for any index left over from a larger palette, so a
        // stale value degrades to an uncoloured tab instead of throwing.
        public static bool IsColored(int index) => index > None && index < Palette.Length;

        public static int Normalize(int index) => IsColored(index) ? index : None;

        public static string GetName(int index) =>
            index >= 0 && index < Palette.Length ? Palette[index].Name : Palette[None].Name;

        public static Color GetColor(int index) =>
            IsColored(index) ? Palette[index].Color : Color.clear;

        // Multiplied onto an icon: white for an untagged entry, so a recycled row can
        // be reset by assigning this unconditionally.
        public static Color GetTint(int index) => GetTint(index, Color.white);

        // Same, but an untagged entry falls back to whatever the caller considers its
        // natural colour — the per-type tint, in practice.
        public static Color GetTint(int index, Color fallback) =>
            IsColored(index) ? Palette[index].Color : fallback;

        // Background tint for a tree row. Transparent means the row is not tagged.
        public static Color GetRowTint(int index, bool isTaggedRow)
        {
            if (!IsColored(index)) return Color.clear;

            Color color = Palette[index].Color;
            if (isTaggedRow)
            {
                color.a = RowAlpha;
                return color;
            }
            return new Color(color.r * ChildDarken, color.g * ChildDarken,
                color.b * ChildDarken, ChildRowAlpha);
        }

        // White ramp, opaque on the left and fully transparent on the right. One
        // texture serves the whole palette: each row multiplies it by its own tint.
        public static Texture2D RowGradient
        {
            get
            {
                // Rebuilt after a domain reload, which drops the static.
                if (s_rowGradient != null) return s_rowGradient;

                s_rowGradient = new Texture2D(GradientWidth, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                for (int x = 0; x < GradientWidth; x++)
                {
                    float t = x / (float)(GradientWidth - 1);
                    s_rowGradient.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - t));
                }
                s_rowGradient.Apply();
                return s_rowGradient;
            }
        }

        static Texture2D s_rowGradient;
    }
}
