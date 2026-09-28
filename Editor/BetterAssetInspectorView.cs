using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // Embedded inspector for an asset tab. The inspector body is an InspectorElement,
    // which renders both UI Toolkit and legacy IMGUI inspectors. The preview has no
    // UI Toolkit equivalent (Editor.DrawPreview is IMGUI-only), so it stays in an
    // IMGUIContainer inside a resizable, collapsible block.
    internal class BetterAssetInspectorView : VisualElement
    {
        const float PreviewMinH = 60f;
        const float PreviewMaxH = 400f;
        const float DragThreshold = 3f;

        readonly VisualElement _badge;
        readonly Image _icon;
        readonly Label _title;
        readonly Label _subtitle;
        readonly ScrollView _inspectorScroll;
        readonly VisualElement _previewBlock;
        readonly Label _previewTitle;
        readonly IMGUIContainer _previewSettings;
        readonly IMGUIContainer _previewBody;

        Editor _editor;
        string _path;
        float _previewHeight = 180f;
        bool _previewCollapsed;

        // Preview header drag state
        bool _pressed;
        bool _dragging;
        float _pressY;
        float _pressHeight;

        public event Action<string> OpenRequested;

        // An object reference was clicked; the owner reveals it in the left panel.
        public event Action<string> AssetReferenceClicked;
        public event Action<float> PreviewHeightChanged;
        public event Action<bool> PreviewCollapsedChanged;

        public BetterAssetInspectorView()
        {
            style.flexGrow = 1;

            // ── Header ────────────────────────────────────────────────────────
            // Badge, name and full path, instead of a 22px strip with a bold label:
            // an asset tab is a destination, so it gets a destination's header.
            VisualElement assetHeader = new VisualElement();
            assetHeader.AddToClassList("bt-inspector__header");

            _badge = new VisualElement();
            _badge.AddToClassList("bt-inspector__badge");

            _icon = new Image { scaleMode = ScaleMode.ScaleToFit };
            _icon.AddToClassList("bt-inspector__icon");
            _badge.Add(_icon);
            assetHeader.Add(_badge);

            VisualElement titles = new VisualElement();
            titles.AddToClassList("bt-inspector__titles");

            _title = new Label();
            _title.AddToClassList("bt-inspector__title");
            titles.Add(_title);

            _subtitle = new Label();
            _subtitle.AddToClassList("bt-inspector__subtitle");
            titles.Add(_subtitle);
            assetHeader.Add(titles);

            Button open = new Button(() => OpenRequested?.Invoke(_path)) { text = "Open" };
            open.AddToClassList("bt-inspector__open");
            assetHeader.Add(open);
            Add(assetHeader);

            // ── Inspector body ────────────────────────────────────────────────
            _inspectorScroll = new ScrollView(ScrollViewMode.Vertical);
            _inspectorScroll.style.flexGrow = 1;
            Add(_inspectorScroll);

            // ── Preview block ─────────────────────────────────────────────────
            _previewBlock = new VisualElement();
            _previewBlock.AddToClassList("bt-preview");

            VisualElement header = new VisualElement();
            header.AddToClassList("bt-preview__header");
            header.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            header.RegisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
            header.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp);

            _previewTitle = new Label();
            _previewTitle.AddToClassList("bt-preview__title");
            header.Add(_previewTitle);

            _previewSettings = new IMGUIContainer(DrawPreviewSettings);
            _previewSettings.AddToClassList("bt-preview__settings");
            header.Add(_previewSettings);
            _previewBlock.Add(header);

            _previewBody = new IMGUIContainer(DrawPreviewBody);
            _previewBody.style.flexGrow = 1;
            _previewBlock.Add(_previewBody);

            Add(_previewBlock);

            BetterInspectorLinks.MirrorObjectFieldClicks(this,
                path => AssetReferenceClicked?.Invoke(path));
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void SetPreviewState(float height, bool collapsed)
        {
            _previewHeight = Mathf.Clamp(height, PreviewMinH, PreviewMaxH);
            _previewCollapsed = collapsed;
            ApplyPreviewLayout();
        }

        // The owner mirrors these through the change events, but it persists them on
        // shutdown; reading them back from here means a mirror that ever goes stale
        // cannot save an old size over a good one.
        public float CurrentPreviewHeight => _previewHeight;

        public bool IsPreviewCollapsed => _previewCollapsed;

        public void SetAsset(string path)
        {
            if (path == _path) return;
            _path = path;

            DestroyEditor();
            _inspectorScroll.Clear();

            _editor = CreateInspectorFor(path);

            Color tint = BetterAssetTypeColors.TintForPath(path);
            _icon.image = AssetDatabase.GetCachedIcon(path);
            _icon.tintColor = tint;
            _badge.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.14f);

            _title.text = Path.GetFileNameWithoutExtension(path);
            _subtitle.text = path;
            _previewTitle.text = "PREVIEW";

            if (_editor == null)
            {
                Label fallback = new Label("Cannot inspect this asset.");
                fallback.AddToClassList("bt-inspector__empty");
                _inspectorScroll.Add(fallback);
            }
            else
            {
                // Renders custom UI Toolkit inspectors natively and falls back to
                // OnInspectorGUI for legacy ones.
                InspectorElement inspector = new InspectorElement(_editor);
                inspector.style.flexGrow = 1;
                _inspectorScroll.Add(inspector);
            }

            ApplyPreviewLayout();
        }

        // Unity inspects an asset in one of two ways, and picking the wrong one is why
        // an .fbx showed nothing here while a .mat showed everything.
        //
        // A source file Unity imports (.fbx, .png, .cs, .shader, .wav…) is inspected
        // through its AssetImporter: ModelImporter gives the Model/Rig/Animation/
        // Materials UI, TextureImporter the texture settings, and so on. A native asset
        // (.mat, .asset, .unity) has only the base AssetImporter, whose editor is an
        // empty GenericInspector, and is inspected through the asset itself.
        //
        // Asking for the importer's editor and looking at what comes back is how the
        // choice gets made without hard-coding a list of extensions that would go stale
        // the moment a project adds a ScriptedImporter of its own.
        static Editor CreateInspectorFor(string path)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                Editor importerEditor = Editor.CreateEditor(importer);
                if (importerEditor is AssetImporterEditor) return importerEditor;
                if (importerEditor != null) UnityEngine.Object.DestroyImmediate(importerEditor);
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null) return null;

            // MaterialEditor — and any editor honouring this flag — draws nothing at all
            // while Unity thinks the object's inspector foldout is collapsed, which is
            // how a material came up as a 4px empty strip. The native Inspector always
            // shows a single asset expanded, so match it.
            InternalEditorUtility.SetIsInspectorExpanded(asset, true);
            return Editor.CreateEditor(asset);
        }

        public void DestroyEditor()
        {
            if (_editor == null) return;
            UnityEngine.Object.DestroyImmediate(_editor);
            _editor = null;
        }

        // ── Preview ───────────────────────────────────────────────────────────

        bool HasPreview => _editor != null && _editor.HasPreviewGUI();

        void ApplyPreviewLayout()
        {
            bool show = HasPreview;
            _previewBlock.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            _previewTitle.text = (_previewCollapsed ? "▶  " : "▼  ")
                + Path.GetFileNameWithoutExtension(_path);
            _previewBody.style.display = _previewCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _previewSettings.style.display = _previewCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _previewBlock.style.height = _previewCollapsed ? StyleKeyword.Auto : _previewHeight;
        }

        void DrawPreviewSettings()
        {
            if (_editor == null || _previewCollapsed) return;
            GUILayout.BeginHorizontal();
            _editor.OnPreviewSettings();
            GUILayout.EndHorizontal();
        }

        void DrawPreviewBody()
        {
            if (_editor == null || _previewCollapsed) return;
            Rect r = _previewBody.contentRect;
            if (r.width <= 0f || r.height <= 0f || float.IsNaN(r.width)) return;
            _editor.DrawPreview(r);
        }

        // Header doubles as collapse toggle (click) and resize handle (drag).
        void OnHeaderPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            if (evt.target is IMGUIContainer) return; // preview settings own their clicks

            _pressed = true;
            _dragging = false;
            _pressY = evt.position.y;
            _pressHeight = _previewHeight;
            (evt.currentTarget as VisualElement)?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnHeaderPointerMove(PointerMoveEvent evt)
        {
            if (!_pressed) return;

            float delta = _pressY - evt.position.y;
            if (!_dragging)
            {
                if (Mathf.Abs(delta) < DragThreshold) return;
                _dragging = true;
                if (_previewCollapsed)
                {
                    _previewCollapsed = false;
                    PreviewCollapsedChanged?.Invoke(false);
                }
            }

            _previewHeight = Mathf.Clamp(_pressHeight + delta, PreviewMinH, PreviewMaxH);
            ApplyPreviewLayout();
            evt.StopPropagation();
        }

        void OnHeaderPointerUp(PointerUpEvent evt)
        {
            if (!_pressed) return;
            (evt.currentTarget as VisualElement)?.ReleasePointer(evt.pointerId);

            if (_dragging)
            {
                PreviewHeightChanged?.Invoke(_previewHeight);
            }
            else
            {
                _previewCollapsed = !_previewCollapsed;
                PreviewCollapsedChanged?.Invoke(_previewCollapsed);
                ApplyPreviewLayout();
            }

            _pressed = false;
            _dragging = false;
            evt.StopPropagation();
        }
    }
}
