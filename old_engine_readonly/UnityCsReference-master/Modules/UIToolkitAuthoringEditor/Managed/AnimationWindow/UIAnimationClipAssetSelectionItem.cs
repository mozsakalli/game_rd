// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEditor;
using UnityEditor.AnimationWindowBuiltin;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Editor
{
    /// <summary>
    /// Animation Window selection item for a bare <see cref="UIAnimationClip"/> asset selected in the
    /// Project window. The UI Toolkit analogue of <c>AnimationClipSelectionItem</c>: the dope sheet edits
    /// the wrapped inner <see cref="AnimationClip"/> directly (edits persist to the asset), but with no
    /// live element or <see cref="UIAnimationBinder"/> there are no animatable properties to add, no clip
    /// to switch to or create, and nothing to preview/record (the inert controller). Clip-state plumbing
    /// is inherited from <see cref="UIToolkitAnimationSelectionItemBase"/>.
    /// </summary>
    internal sealed class UIAnimationClipAssetSelectionItem : UIToolkitAnimationSelectionItemBase
    {
        UIAnimationClipAssetSelectionItem(AnimationWindow window, UIAnimationClip uiClip)
            : base(window)
        {
            SetUIClip(uiClip);
        }

        internal static UIAnimationClipAssetSelectionItem Create(AnimationWindow window, UIAnimationClip uiClip)
        {
            var item = new UIAnimationClipAssetSelectionItem(window, uiClip);
            item.m_Controller = new UIAnimationClipAssetController(item);
            return item;
        }

        // A Project-window asset has no scene element to bind to and its clip is fixed to the selection,
        // so it mirrors the classic bare-AnimationClip surface: no create, no switch, no Add Property.
        public override bool canCreateClips => false;
        public override bool canChangeClip => false;
        public override bool canAddCurves => false;

        // Never reached: canCreateClips is false, so the base create-clip pipeline short-circuits.
        protected override string DialogSubjectName => null;
        protected override void AssignClipToTarget(UIAnimationClip newClip) { }

        public override bool IsCompatibleWith(UnityEngine.Object selectedObject)
            => selectedObject is UIAnimationClip uiClip && ReferenceEquals(uiClip, m_UIClip);

        // Drop the clip if the asset was deleted out from under us; the responder resolves a fresh
        // selection on the next selection-change event.
        public override void Synchronize()
        {
            if (m_UIClip == null)
                ClearClip();
        }

        public override int GetRefreshHash()
        {
            var animClip = m_Clip?.animationClip;
            uint dirtyCount = animClip != null ? (uint)EditorUtility.GetDirtyCount(animClip) : 0;
            return new Hash128(
                (uint)(m_UIClip != null ? m_UIClip.GetHashCode() : 0),
                (uint)(animClip != null ? animClip.GetHashCode() : 0),
                dirtyCount,
                0u).GetHashCode();
        }
    }
}
