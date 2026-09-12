// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityEngine.Android
{
    public static partial class AndroidApplication
    {
        [NoAutoStaticsCleanup]
        static AndroidJavaObject s_JavaFoldingFeaturesWrapper = null;
        [NoAutoStaticsCleanup]
        static bool s_WindowManagerApiMissing = false;
        [NoAutoStaticsCleanup]
        static AndroidFoldingFeatures s_AndroidFoldingFeatures = null;
        [NoAutoStaticsCleanup]
        static bool s_FoldingFeaturesInitialized = false;

        [Serializable]
        class AndroidFoldingFeatures
        {
            [SerializeField] private AndroidFoldingFeature[] m_FoldingFeatures = null;
            public AndroidFoldingFeature[] foldingFeatures => m_FoldingFeatures;
        }

        class FoldingFeaturesUpdatedCallback : AndroidJavaProxy
        {
            public FoldingFeaturesUpdatedCallback()
                : base("com.unity3d.player.IFoldingFeaturesUpdatedCallback")
            {
            }

            private void onFoldingFeaturesUpdate(string foldingFeaturesJson)
            {
                s_AndroidFoldingFeatures = JsonUtility.FromJson<AndroidFoldingFeatures>(foldingFeaturesJson);
                AndroidApplication.onFoldingFeaturesUpdatedInternal?.Invoke(s_AndroidFoldingFeatures.foldingFeatures);
            }
        }

        static AndroidJavaObject GetFoldingFeaturesWrapper()
        {
            if (s_JavaFoldingFeaturesWrapper == null)
            {
                using (var javaClass = new AndroidJavaClass("com.unity3d.player.UnityFoldingFeaturesWrapper"))
                {
                    s_JavaFoldingFeaturesWrapper = javaClass.CallStatic<AndroidJavaObject>("getInstance");
                    s_WindowManagerApiMissing = s_JavaFoldingFeaturesWrapper.Call<bool>("windowManagerApiMissing");
                }
            }
            if (s_WindowManagerApiMissing)
            {
                throw new InvalidOperationException("WindowManager API is not available! Make sure your gradle project includes \"androidx.window:window\" and \"androidx.window:window-java\" dependencies.");
            }
            return s_JavaFoldingFeaturesWrapper;
        }

        static void EnsureFoldingFeaturesInitialized()
        {
            if (s_FoldingFeaturesInitialized)
            {
                return;
            }
            GetFoldingFeaturesWrapper().Call("registerFoldingFeaturesUpdatedListener", new FoldingFeaturesUpdatedCallback());
            var javaFoldingFeaturesJson = GetFoldingFeaturesWrapper().Call<String>("currentFoldingFeaturesJson");
            s_AndroidFoldingFeatures = JsonUtility.FromJson<AndroidFoldingFeatures>(javaFoldingFeaturesJson);
            s_FoldingFeaturesInitialized = true;
        }

        ///<summary>Provides an array of <see cref="AndroidFoldingFeature" /> data for the current display.</summary>
        ///<remarks>Returns an empty array if the current display doesn't support folding features. Use this information immediately after requesting as the system updates it automatically.
        ///
        ///The following code example demonstrates how to update the UI for full-screen or split-screen modes based on the current folding features data.</remarks>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class MyApplication : MonoBehaviour
        ///{
        ///    public void Start()
        ///    {
        ///        OnFoldingFeaturesUpdated(AndroidApplication.currentFoldingFeatures);
        ///        AndroidApplication.onFoldingFeaturesUpdated += OnFoldingFeaturesUpdated;
        ///    }
        ///
        ///    public void OnDisable()
        ///    {
        ///        AndroidApplication.onFoldingFeaturesUpdated -= OnFoldingFeaturesUpdated;
        ///    }
        ///
        ///    private void OnFoldingFeaturesUpdated(AndroidFoldingFeature[] foldInfo)
        ///    {
        ///        if (foldInfo.Length == 0 || !foldInfo[0].isSeparating)
        ///        {
        ///            // update UI for full screen
        ///        }
        ///        else // foldInfo[0].isSeparating
        ///        {
        ///            // update UI for split screen using foldInfo[0].bounds and foldInfo[0].occlusionType
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        public static AndroidFoldingFeature[] currentFoldingFeatures
        {
            get
            {
                EnsureFoldingFeaturesInitialized();
                return s_AndroidFoldingFeatures.foldingFeatures;
            }
        }

        [AutoStaticsCleanupOnCodeReload]
        internal static event Action<AndroidFoldingFeature[]> onFoldingFeaturesUpdatedInternal;

        ///<summary>A callback to detect the folding features changes when the application is running.</summary>
        ///<remarks>Unity passes an updated array of <see cref="AndroidFoldingFeature" /> data to the callback.
        ///
        ///The following code example demonstrates how to update the UI for full-screen or split-screen modes based on the current folding features data.</remarks>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class MyApplication : MonoBehaviour
        ///{
        ///    public void Start()
        ///    {
        ///        OnFoldingFeaturesUpdated(AndroidApplication.currentFoldingFeatures);
        ///        AndroidApplication.onFoldingFeaturesUpdated += OnFoldingFeaturesUpdated;
        ///    }
        ///
        ///    public void OnDisable()
        ///    {
        ///        AndroidApplication.onFoldingFeaturesUpdated -= OnFoldingFeaturesUpdated;
        ///    }
        ///
        ///    private void OnFoldingFeaturesUpdated(AndroidFoldingFeature[] foldInfo)
        ///    {
        ///        if (foldInfo.Length == 0 || !foldInfo[0].isSeparating)
        ///        {
        ///            // update UI for full screen
        ///        }
        ///        else // foldInfo[0].isSeparating
        ///        {
        ///            // update UI for split screen using foldInfo[0].bounds and foldInfo[0].occlusionType
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        public static event Action<AndroidFoldingFeature[]> onFoldingFeaturesUpdated
        {
            add
            {
                EnsureFoldingFeaturesInitialized();
                onFoldingFeaturesUpdatedInternal += value;
            }
            remove
            {
                onFoldingFeaturesUpdatedInternal -= value;
            }
        }
    }

    ///<summary>Options to represent the hinge occlusion behavior.</summary>
    ///<remarks>This enum directly wraps the &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.OcclusionType"&gt;FoldingFeature.OcclusionType&lt;/a&gt; values in the AndroidX API.</remarks>
    public enum AndroidFoldableOcclusionType
    {
        ///<summary>Wraps the Android property value <c>FULL</c>.</summary>
        ///<remarks>For information about this value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.OcclusionType#FULL()"&gt;FoldingFeature.OcclusionType FULL&lt;/a&gt;</remarks>
        Full = 0,

        ///<summary>Wraps the Android property value <c>NONE</c>.</summary>
        ///<remarks>For information about this value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.OcclusionType#NONE()"&gt;FoldingFeature.OcclusionType NONE&lt;/a&gt;</remarks>
        None
    }

    ///<summary>Options to indicate the orientation of a fold or hinge.</summary>
    ///<remarks>This enum directly wraps the &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.Orientation"&gt;FoldingFeature.Orientation&lt;/a&gt; values in the AndroidX API.</remarks>
    public enum AndroidFoldableOrientation
    {
        ///<summary>Wraps the Android property value <c>HORIZONTAL</c>.</summary>
        ///<remarks>For information about this value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.Orientation#HORIZONTAL()"&gt;FoldingFeature.Orientation HORIZONTAL&lt;/a&gt;</remarks>
        Horizontal = 0,

        ///<summary>Wraps the Android property value <c>VERTICAL</c>.</summary>
        ///<remarks>For information about this value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.Orientation#VERTICAL()"&gt;FoldingFeature.Orientation VERTICAL&lt;/a&gt;</remarks>
        Vertical
    }

    ///<summary>Options to indicate the state of a fold or hinge.</summary>
    ///<remarks>This enum directly wraps the &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.State"&gt;FoldingFeature.State&lt;/a&gt; values in the AndroidX API.</remarks>
    public enum AndroidFoldableState
    {
        ///<summary>Wraps the Android property value <c>FLAT</c>.</summary>
        ///<remarks>For information about this value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.State#FLAT()"&gt;FoldingFeature.State FLAT&lt;/a&gt;</remarks>
        Flat = 0,

        ///<summary>Wraps the Android property value <c>HALF_OPENED</c>.</summary>
        ///<remarks>For information about this value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature.State#HALF_OPENED()"&gt;FoldingFeature.State HALF_OPENED&lt;/a&gt;</remarks>
        HalfOpened
    }

    ///<summary>Provides information about a fold in a flexible display or a hinge between separate physical displays.</summary>
    ///<remarks>This class wraps the &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature"&gt;FoldingFeature&lt;/a&gt; API.</remarks>
    ///<example>
    ///  <code><![CDATA[using UnityEngine;
    ///using UnityEngine.Android;
    ///
    ///public class MyApplication : MonoBehaviour
    ///{
    ///    public void Start()
    ///    {
    ///        var foldInfo = AndroidApplication.currentFoldingFeatures;
    ///        if (foldInfo.Length > 0)
    ///        {
    ///            Debug.Log("Folding features detected:");
    ///            Debug.Log($"* bounds: {foldInfo[0].bounds}");
    ///            Debug.Log($"* occlusion: {foldInfo[0].occlusionType}");
    ///            Debug.Log($"* orientation: {foldInfo[0].orientation}");
    ///            Debug.Log($"* state: {foldInfo[0].state}");
    ///            Debug.Log($"* isSeparating: {foldInfo[0].isSeparating}");
    ///        }
    ///        else
    ///        {
    ///            Debug.Log("Folding features are not detected");
    ///        }
    ///    }
    ///}]]></code>
    ///</example>
    [Serializable]
    public class AndroidFoldingFeature
    {
        [SerializeField] private int m_X = 0;
        [SerializeField] private int m_Y = 0;
        [SerializeField] private int m_Width = 0;
        [SerializeField] private int m_Height = 0;
        [SerializeField] private int m_OcclusionType = 0;
        [SerializeField] private int m_Orientation = 0;
        [SerializeField] private int m_State = 0;
        [SerializeField] private bool m_IsSeparating = false;
        private RectInt? m_Bounds = null;

        private AndroidFoldingFeature() {}

        ///<summary>Wraps the Android method <c>DisplayFeature.getBounds()</c>. Read-only.</summary>
        ///<remarks>For more information, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/DisplayFeature#getBounds()"&gt;DisplayFeature.getBounds()&lt;/a&gt;</remarks>
        ///<seealso cref="RectInt" />
        public RectInt bounds
        {
            get
            {
                if (!m_Bounds.HasValue)
                {
                    m_Bounds = new RectInt(m_X, m_Y, m_Width, m_Height);
                }
                return m_Bounds.Value;
            }
        }

        ///<summary>Wraps the Android method <c>FoldingFeature.getOcclusionType()</c>. Read-only.</summary>
        ///<remarks>For more information, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature#getOcclusionType()"&gt;FoldingFeature.getOcclusionType()&lt;/a&gt;</remarks>
        ///<seealso cref="AndroidFoldableOcclusionType" />
        public AndroidFoldableOcclusionType occlusionType => (AndroidFoldableOcclusionType)m_OcclusionType;

        ///<summary>Wraps the Android method <c>FoldingFeature.getOrientation()</c>. Read-only.</summary>
        ///<remarks>For more information, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature#getOrientation()"&gt;FoldingFeature.getOrientation()&lt;/a&gt;</remarks>
        ///<seealso cref="AndroidFoldableOrientation" />
        public AndroidFoldableOrientation orientation => (AndroidFoldableOrientation)m_Orientation;

        ///<summary>Wraps the Android method <c>FoldingFeature.getState()</c>. Read-only.</summary>
        ///<remarks>For more information, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature#getState()"&gt;FoldingFeature.getState()&lt;/a&gt;</remarks>
        ///<seealso cref="AndroidFoldableState" />
        public AndroidFoldableState state => (AndroidFoldableState)m_State;

        ///<summary>Wraps the Android method <c>FoldingFeature.getIsSeparating()</c>. Read-only.</summary>
        ///<remarks>True if the <c>AndroidFoldingFeature</c> splits the display into two areas, false otherwise.
        ///
        ///For more information, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/androidx/window/layout/FoldingFeature#getIsSeparating()"&gt;FoldingFeature.getIsSeparating()&lt;/a&gt;</remarks>
        public bool isSeparating => m_IsSeparating;
    }
}
