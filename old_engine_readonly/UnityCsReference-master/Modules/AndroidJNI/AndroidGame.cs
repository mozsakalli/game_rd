// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Scripting.LifecycleManagement;

namespace UnityEngine.Android
{
    ///<summary>Options for the available &lt;a href="https://developer.android.com/reference/android/app/GameManager#constants_1"&gt;game modes&lt;/a&gt; that <see cref="AndroidGame.GameMode" /> can return.</summary>
    public enum AndroidGameMode
    {
        ///<summary>Game mode is &lt;a href="https://developer.android.com/reference/android/app/GameManager#GAME_MODE_UNSUPPORTED"&gt;not supported&lt;/a&gt;. For more information, refer to &lt;a href="https://developer.android.com/reference/kotlin/android/app/GameManager#getgamemode"&gt;getGameMode&lt;/a&gt;.</summary>
        Unsupported = 0x00000000,

        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameManager#GAME_MODE_STANDARD"&gt;Standard&lt;/a&gt; game mode, which indicates that your application should use its default performance characteristics.</summary>
        Standard = 0x00000001,

        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameManager#GAME_MODE_PERFORMANCE"&gt;Performance&lt;/a&gt; game mode, which indicates that you application should maximize performance.</summary>
        Performance = 0x00000002,

        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameManager#GAME_MODE_BATTERY"&gt;Battery&lt;/a&gt; game mode, which indicates that your application should use optimizations that save battery and give a longer gameplay time.</summary>
        Battery = 0x00000003
    }

    public static partial class AndroidGame
    {
        [NoAutoStaticsCleanup]
        private static AndroidJavaObject m_UnityGameManager = null;
        [NoAutoStaticsCleanup]
        private static AndroidJavaObject m_UnityGameState = null;

        private static AndroidJavaObject GetUnityGameManager()
        {
            if (m_UnityGameManager != null)
            {
                return m_UnityGameManager;
            }

            m_UnityGameManager = new AndroidJavaClass("com.unity3d.player.UnityGameManager");

            return m_UnityGameManager;
        }

        private static AndroidJavaObject GetUnityGameState()
        {
            if (m_UnityGameState != null)
            {
                return m_UnityGameState;
            }

            m_UnityGameState = new AndroidJavaClass("com.unity3d.player.UnityGameState");

            return m_UnityGameState;
        }

        ///<summary>Calls &lt;a href="https://developer.android.com/reference/android/app/GameManager#getGameMode()"&gt;getGameMode()&lt;/a&gt; in the Android application to retrieve the user selected game mode for the application and returns <see cref="AndroidGameMode" />. Requires API level 31 (Android 12). (RO)</summary>
        ///<remarks>When target device does not support the required API level, <see cref="AndroidGameMode.Unsupported" /> is returned (value of 0).</remarks>
        public static AndroidGameMode GameMode
        {
            get
            {
                return AndroidGameMode.Unsupported;
            }
        }

        ///<summary>Calls &lt;a href="https://developer.android.com/reference/android/app/GameManager#setGameState(android.app.GameState)"&gt;setGameState()&lt;/a&gt; in the Android application to specify the loading status. Requires API level 33 (Android 13).</summary>
        ///<remarks>When target device does not support the required API level, no action is taken.</remarks>
        ///<param name="isLoading">Whether the game is in the loading state.</param>
        ///<param name="gameState">
        ///  <see cref="AndroidGameState" /> state.</param>
        public static void SetGameState(bool isLoading, AndroidGameState gameState)
        {
        }

        ///<summary>Calls &lt;a href="https://developer.android.com/reference/android/app/GameManager#setGameState(android.app.GameState)"&gt;setGameState()&lt;/a&gt; in the Android application to specify the loading status. Requires API level 33 (Android 13).</summary>
        ///<remarks>When target device does not support the required API level, no action is taken.</remarks>
        ///<param name="isLoading">Whether the game is in the loading state.</param>
        ///<param name="gameState">
        ///  <see cref="AndroidGameState" /> state.</param>
        ///<param name="label">Developer supplied custom value, e.g. for the current level.</param>
        ///<param name="quality">Developer supplied custom value, e.g. for the current quality level.</param>
        public static void SetGameState(bool isLoading, AndroidGameState gameState, int label, int quality)
        {
        }
    }
}
