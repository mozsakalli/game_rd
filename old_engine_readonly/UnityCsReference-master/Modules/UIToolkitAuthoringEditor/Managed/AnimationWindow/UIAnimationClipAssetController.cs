// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.UIToolkit.Editor
{
    /// <summary>
    /// Inert Animation Window controller for a <see cref="UIAnimationClipAssetSelectionItem"/>. The dope
    /// sheet writes straight to the clip asset, so the controller only tracks the playhead; with no live
    /// element to drive there is nothing to preview, record, or play. The UI Toolkit analogue of the
    /// built-in <c>DefaultAnimationWindowController</c> that backs <c>AnimationClipSelectionItem</c>.
    /// </summary>
    sealed class UIAnimationClipAssetController : IAnimationWindowController
    {
        readonly UIToolkitAnimationSelectionItemBase m_Selection;
        float m_Time;
        int m_Frame;

        public UIAnimationClipAssetController(UIToolkitAnimationSelectionItemBase selection)
        {
            m_Selection = selection;
        }

        // Resolved on demand, not cached: the same controller is reused while the asset stays open, so
        // the clip's sample rate can change under it (toolbar frame-rate field). Mirrors
        // VisualElementAnimationWindowController.
        float FrameRate
        {
            get
            {
                var rate = m_Selection?.clip?.frameRate ?? 0f;
                return rate > 0f ? rate : 60f;
            }
        }

        public void OnSelectionChanged() { }
        public void Dispose() { }

        public float time
        {
            get => m_Time;
            set
            {
                m_Time = Mathf.Max(0f, value);
                m_Frame = Mathf.RoundToInt(m_Time * FrameRate);
            }
        }

        public int frame
        {
            get => m_Frame;
            set
            {
                m_Frame = Mathf.Max(0, value);
                m_Time = m_Frame / FrameRate;
            }
        }

        public bool canPlay => false;
        public bool playing { get => false; set { } }
        public bool PlaybackUpdate() => false;

        public bool canPreview => false;
        public bool previewing { get => false; set { } }

        public bool canRecord => false;
        public bool recording { get => false; set { } }

        public void ResampleAnimation() { }
        public void ProcessCandidates() { }
        public void ClearCandidates() { }

        public float GetFloatValue(EditorCurveBinding binding) => 0f;
        public int GetIntValue(EditorCurveBinding binding) => 0;
        public Object GetObjectReferenceValue(EditorCurveBinding binding) => null;
    }
}
