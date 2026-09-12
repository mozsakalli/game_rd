// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;

namespace UnityEngine.Android
{
    ///<summary>Options for the available &lt;a href="https://developer.android.com/reference/android/app/GameState#constants_1"&gt;game states&lt;/a&gt; that you can pass to <see cref="AndroidGame.SetGameState" /> or you can set as a current game state mode to be used for [Automated game state hinting in Unity](xref:android-game-state-hinting) using <see cref="AndroidGame.Automatic.SetGameState" /> method.</summary>
    [NativeHeader("Modules/AndroidJNI/Public/GameStateHelper.h")]
    public enum AndroidGameState
    {
        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameState#MODE_UNKNOWN"&gt;Unknown&lt;/a&gt; refers to the default game state.</summary>
        Unknown = 0x00000000,

        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameState#MODE_NONE"&gt;None&lt;/a&gt; indicates that the game is not in active play.</summary>
        None = 0x00000001,

        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameState#MODE_GAMEPLAY_INTERRUPTIBLE"&gt;Interruptible&lt;/a&gt; game state, which indicates that the game is in active, but interruptible, gameplay.</summary>
        GamePlayInterruptible = 0x00000002,

        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameState#MODE_GAMEPLAY_UNINTERRUPTIBLE"&gt;Uninterruptible&lt;/a&gt; game state, which indicates that the game is in active user play mode, which is real-time and cannot be interrupted</summary>
        GamePlayUninterruptible = 0x00000003,

        ///<summary>&lt;a href="https://developer.android.com/reference/android/app/GameState#MODE_CONTENT"&gt;Content&lt;/a&gt; game state, which indicates that the current content shown is not gameplay related.</summary>
        Content = 0x00000004
    }

    [NativeHeader("Modules/AndroidJNI/Public/GameStateHelper.h")]
    internal enum GameStateLabel
    {
        Default = -1,
        InitialLoading = -2,
        AssetPacksLoading = -3,
        WebRequest = -4
    }

    ///<summary>Provides methods and properties for accessing different Android game APIs.</summary>
    [NativeHeader("Modules/AndroidJNI/Public/GameStateHelper.h")]
    [StaticAccessor("GameStateHelper::Get()", StaticAccessorType.Dot)]
    public static partial class AndroidGame
    {
        ///<summary>Provides methods for [Automated game state hinting in Unity](xref:android-game-state-hinting).</summary>
        [StaticAccessor("GameStateHelper::Get()", StaticAccessorType.Dot)]
        public static partial class Automatic
        {
            ///<summary>Sets the current <see cref="AndroidGameState" /> to be used for [Automated game state hinting in Unity](xref:android-game-state-hinting). Requires API level 33 (Android 13).</summary>
            ///<remarks>You can set the mode parameter based on the current game state. For example, you can use <see cref="AndroidGameState.None" /> for displaying the game menu and <see cref="AndroidGameState.GamePlayInterruptible" /> or <see cref="AndroidGameState.GamePlayUninterruptible" /> during the gameplay. 
            ///
            ///Once set, the mode remains unchanged until you call this method again. However, if the game is interrupted by a full-screen video or a full-screen ad, the mode automatically changes to <see cref="AndroidGameState.Content" />.
            ///
            ///When target device does not support the required API level, no action is taken.</remarks>
            ///<param name="mode">
            ///  <see cref="AndroidGameState" /> value.</param>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///using UnityEngine.Android;
            ///
            ///public class MainMenu : MonoBehaviour
            ///{
            ///    void Start()
            ///    {
            ///        AndroidGame.Automatic.SetGameState(AndroidGameState.None);
            ///    }
            ///}
            ///]]></code>
            ///</example>
            [NativeMethod("SetGameStateMode")]
            public static extern void SetGameState(AndroidGameState mode);
        }
        // Required for automated SetGameState calls, indicates to the operating system when the application is in loading state, level is the type of loading
        internal static extern void StartLoading(int label);
        // Required for automated SetGameState calls, indicates to the operating system when loading state is ended, level is the type of loading
        internal static extern void StopLoading(int label);
    }
}
