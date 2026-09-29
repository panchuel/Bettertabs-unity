using UnityEngine.UIElements;

namespace BetterTabs
{
    // The Settings page, shown inside the BetterTabs window (the gear at the bottom
    // right) rather than as a window of its own. Every option applies at once.
    internal class BetterTabsSettingsView : VisualElement
    {
        public BetterTabsSettingsView()
        {
            AddToClassList("bt-page");

            Section("Navigation");
            // Shift + scroll up goes to the next tab (right) by default, so enabling the
            // option is what sends it to the previous one.
            Option("Invert Scroll Direction",
                "When enabled, Shift + scroll up moves to the previous tab (left) and Shift + scroll down to the next one (right).",
                BetterTabsSettings.InvertScroll,
                value => BetterTabsSettings.InvertScroll = value);

            Section("Drag and Drop");
            Option("Show Drag Preview",
                "Shows what is being dragged next to the cursor, in every editor window.",
                BetterTabsSettings.ShowDragPreview,
                value => BetterTabsSettings.ShowDragPreview = value);
        }

        void Section(string title)
        {
            Label label = new Label(title.ToUpperInvariant());
            label.AddToClassList("bt-page__section");
            Add(label);
        }

        void Option(string label, string hint, bool value, System.Action<bool> apply)
        {
            Toggle toggle = new Toggle(label);
            toggle.AddToClassList("bt-page__toggle");
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(change => apply(change.newValue));
            Add(toggle);

            Label hintLabel = new Label(hint);
            hintLabel.AddToClassList("bt-page__hint");
            Add(hintLabel);
        }
    }
}
