// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEditor.Overlays;
using System;
using System.Reflection;
using UnityEngine;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace UnityEditor.Toolbars
{
    public struct MainToolbarContent : IEquatable<MainToolbarContent>
    {
        public string text;
        public Texture2D image;
        public string tooltip;

        public MainToolbarContent() : this(string.Empty, null, string.Empty) { }
        public MainToolbarContent(string text) : this(text, null, string.Empty) { }
        public MainToolbarContent(string text, string tooltip) : this(text, null, tooltip) { }
        public MainToolbarContent(Texture2D image) : this(string.Empty, image, string.Empty) { }
        public MainToolbarContent(Texture2D image, string tooltip) : this(string.Empty, image, tooltip) { }
        public MainToolbarContent(string text, Texture2D image, string tooltip)
        {
            this.text = text;
            this.image = image;
            this.tooltip = tooltip;
        }

        // antoinebr: Remove when the toolbar feature goes public. Used to not break branches possibly in flight and as a backup until package are cleaned up.
        public static implicit operator MainToolbarContent(GUIContent content)
        {
            return new MainToolbarContent(content.text, content.image as Texture2D, content.tooltip);
        }

        public static bool operator ==(MainToolbarContent a, MainToolbarContent b)
        {
            if (a == null || b == null)
                return false;

            return a.Equals(b);
        }

        public static bool operator !=(MainToolbarContent a, MainToolbarContent b)
        {
            if (a == null || b == null)
                return true;

            return !a.Equals(b);
        }

        public override bool Equals(object obj)
        {
            return obj is MainToolbarContent content && Equals(content);
        }

        public bool Equals(MainToolbarContent other)
        {
            return text == other.text &&
                   image == other.image &&
                   tooltip == other.tooltip;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(text, image, tooltip);
        }
    }

    public static partial class MainToolbar
    {
        internal struct ElementDefinition
        {
            public MainToolbarElementAttribute attr;
            public MethodInfo method;
            public MethodInfo availabilityMethod;
        }

        static MainToolbarWindow window => (MainToolbarWindow)Toolbar.instance.actualView;
        static bool windowExists => Toolbar.instance != null && Toolbar.instance.actualView is MainToolbarWindow;

        public static void Refresh(string path)
        {
            if (TryGetOverlay(path, out var overlay))
                overlay.RebuildContent();
        }

        internal static void ShowAll(string path)
        {
            SetDisplayedAll(path, true);
        }

        internal static void HideAll(string path)
        {
            SetDisplayedAll(path, false);
        }

        static void SetDisplayedAll(string startsWith, bool displayed)
        {
            if (!windowExists)
                return;

            foreach (var overlay in window.overlayCanvas.overlays)
            {
                if (overlay.id.StartsWith(startsWith, StringComparison.Ordinal))
                {
                    overlay.displayed = displayed;
                }
            }
        }

        internal static bool TryGetOverlay(string path, out Overlay overlay)
        {
            if (!windowExists)
            {
                overlay = null;
                return false;
            }

            return window.TryGetOverlay(path, out overlay);
        }

        [AutoStaticsCleanupOnCodeReload]
        static Dictionary<string, MethodInfo> s_PathToAvailabilityMethods;
        internal static List<ElementDefinition> GetAllElementDefinitions()
        {
            List<ElementDefinition> m_Definitions = new List<ElementDefinition>();
            var mainToolbarElementDataMethods = TypeCache.GetMethodsWithAttribute<MainToolbarElementAttribute>();
            
            s_PathToAvailabilityMethods ??= new();
            s_PathToAvailabilityMethods.Clear();
            var mainToolbarElementAvailabilityMethods = TypeCache.GetMethodsWithAttribute<MainToolbarElementAvailabilityAttribute>();

            foreach (var method in mainToolbarElementAvailabilityMethods)
            {
                MainToolbarElementAvailabilityAttribute mteAttrib = method.GetCustomAttribute<MainToolbarElementAvailabilityAttribute>(false);

                if (mteAttrib == null)
                    continue;
                
                if (method.GetParameters().Length > 0)
                {
                    Debug.LogWarning("Methods with MainToolbarElementAvailability attribute should take zero parameters.");
                    continue;
                }
                if (method.IsStatic == false)
                {
                    Debug.LogWarning("Methods with MainToolbarElementAvailability attribute must be static.");
                    continue;
                }
                if (method.ReturnType != typeof(bool))
                {
                    Debug.LogWarning("Methods with MainToolbarElementAvailability attribute must return bool value.");
                    continue;
                }
                
                s_PathToAvailabilityMethods.Add(mteAttrib.path, method);
            }
            
            foreach (var method in mainToolbarElementDataMethods)
            {
                MainToolbarElementAttribute mteAttrib = method.GetCustomAttribute<MainToolbarElementAttribute>(false);

                if (mteAttrib == null)
                    continue;

                if (method.GetParameters().Length > 0)
                {
                    Debug.LogWarning("Methods with MainToolbarElement attribute should take zero parameters.");
                    continue;
                }
                if (method.IsStatic == false)
                {
                    Debug.LogWarning("Methods with MainToolbarElement attribute must be static.");
                    continue;
                }
                if (typeof(MainToolbarElement).IsAssignableFrom(method.ReturnType) == false
                    && method.ReturnType != typeof(IEnumerable<MainToolbarElement>))
                {
                    Debug.LogWarning("Methods with MainToolbarElement attribute must return MainToolbarElementData.");
                    continue;
                }

                s_PathToAvailabilityMethods.TryGetValue(mteAttrib.path, out var methodAvailability);

                m_Definitions.Add(new ElementDefinition()
                {
                    attr = mteAttrib,
                    method = method,
                    availabilityMethod = methodAvailability
                });
            }

            return m_Definitions;
        }

        internal static void ResetToUnityDefaultLayout()
        {
            window.overlayCanvas.ApplyPreset(new UnityOnlyToolbarPreset());
        }

        // Indirection so Modules/EditorToolbar can supply the button without EditorModule referencing it.
        [NoAutoStaticsCleanup] // Registered once via [InitializeOnLoadMethod].
        internal static Func<string, MainToolbarElement> menuItemButtonFactory { get; set; }

        internal const string menuItemOverlayIdPrefix = "Menu Items/";

        // Returns null when menuPath no longer resolves to a real menu item, so no overlay is created at all.
        internal static MainToolbarOverlay CreateMenuItemOverlay(string menuPath)
        {
            if (!Menu.MenuItemExists(menuPath))
                return null;

            var defaultOverlayAttrib = new OverlayAttribute();
            var overlay = new MainToolbarOverlay();
            overlay.createElementDelegate = () => menuItemButtonFactory?.Invoke(menuPath);
            overlay.Initialize($"{menuItemOverlayIdPrefix}{menuPath}", menuPath.Replace(" ", ""), LastPathSegment(menuPath),
                defaultOverlayAttrib.defaultSize, defaultOverlayAttrib.minSize, defaultOverlayAttrib.maxSize,
                defaultOverlayAttrib.priority, defaultOverlayAttrib.group);

            // Forces RestoreOverlay's displayed flip to actually happen (fresh overlays already default to true).
            overlay.displayed = false;
            return overlay;
        }

        static string LastPathSegment(string path)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments[^1];
        }

        internal static void PinMenuItem(string menuPath)
        {
            if (!OverlayCanvasesData.instance.AddPinnedMenuItem(menuPath))
                return; // already pinned - silent no-op

            if (!windowExists)
                return;

            var overlay = CreateMenuItemOverlay(menuPath);
            if (overlay != null)
                window.overlayCanvas.Add(overlay);
        }

        internal static void UnpinMenuItem(string menuPath)
        {
            OverlayCanvasesData.instance.RemovePinnedMenuItem(menuPath);

            if (windowExists && TryGetOverlay($"{menuItemOverlayIdPrefix}{menuPath}", out var overlay))
                window.overlayCanvas.Remove(overlay);
        }

        // Used by MainToolbarOverlay's "Hide" action, which only has the overlay id on hand.
        internal static void UnpinMenuItemByOverlayId(string overlayId)
        {
            if (overlayId.StartsWith(menuItemOverlayIdPrefix, StringComparison.Ordinal))
                UnpinMenuItem(overlayId.Substring(menuItemOverlayIdPrefix.Length));
        }

        internal static bool editModeEnabled
        {
            get => MainToolbarWindow.instance.editModeActive;
            set => MainToolbarWindow.instance.editModeActive = value;
        }
    }

    sealed class UnityOnlyToolbarPreset : IOverlayPreset
    {
        public const string presetName = "Unity Default";

        [NoAutoStaticsCleanup] // Shared zero-length empty array (Array.Empty); holds no references, safe to persist.
        readonly static SaveData[] m_EmptySave = Array.Empty<SaveData>();
        [NoAutoStaticsCleanup] // Shared zero-length empty array (Array.Empty); holds no references, safe to persist.
        readonly static DynamicPanelContainerData[] m_EmptyDynamicPanelContainerData = Array.Empty<DynamicPanelContainerData>();
        [NoAutoStaticsCleanup] // Shared zero-length empty array (Array.Empty); holds no references, safe to persist.
        readonly static string[] m_EmptyMenuItemPaths = Array.Empty<string>();

        public SaveData[] saveData => m_EmptySave;
        public DynamicPanelContainerData[] dynamicPanelContainerData => m_EmptyDynamicPanelContainerData;
        // Unity Default never includes any pinned menu items.
        public string[] menuItemPaths => m_EmptyMenuItemPaths;
        public Type targetWindowType => typeof(MainToolbarWindow);

        public bool CanApplyToWindow(Type windowType) => windowType == typeof(MainToolbarWindow);

        public void ApplyCustomData(OverlayCanvas canvas)
        {
            // Show only the unity defined clean subset of elements without any of the package defaults
            foreach (var overlay in canvas.overlays)
            {
                var mtOverlay = overlay as MainToolbarOverlay;
                if (mtOverlay != null && mtOverlay.createElementMethod == null)
                    continue; // Defensive: ApplyPreset already removes all pins before this runs, since menuItemPaths is empty.

                bool shouldShow = false;
                if (mtOverlay != null && mtOverlay.createElementMethod.GetCustomAttribute<UnityOnlyMainToolbarPresetAttribute>() != null)
                    shouldShow = true; // Every element tagged with UnityOnlyMainToolbarPreset should always start visible

                overlay.displayed = shouldShow;
            }
        }

        public string name => presetName;
    }
}
