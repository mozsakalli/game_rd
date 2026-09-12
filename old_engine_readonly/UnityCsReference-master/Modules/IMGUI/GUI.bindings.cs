// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;

namespace UnityEngine
{
    ///<summary>The GUI class is the interface for Unity's GUI with manual positioning.</summary>
    ///<remarks>.</remarks>
    ///<seealso href="xref:comp:GUI Scripting Guide">GUI tutorial</seealso>
    [NativeHeader("Modules/IMGUI/GUI.bindings.h"),
     NativeHeader("Modules/IMGUI/GUISkin.bindings.h")]
    public partial class GUI
    {
        ///<summary>Applies a global tint to the GUI. The tint affects backgrounds and text colors.</summary>
        ///<remarks>The tint is applied when Unity draws the content. It multiplies this property by the current color, and uses the resulting color to draw the content.
        ///**Note:** Because GUI.Color is a multiplier for the current text color, it has no effect on UI labels when you use the light Unity theme. In the light theme, the default color for label text is black, which has an RGB value of 0. Multiplying any GUI.Color value by 0 yields 0, so the label text color does not change. In the dark theme, the default label text color is white, which has an RGB value of 1, so whatever color you specify in GUI.color becomes the new label text color.</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// // Tints all GUI drawn elements with yellow.
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    void OnGUI()
        ///    {
        ///        GUI.color = Color.yellow;
        ///        GUI.Label(new Rect(10, 10, 100, 20), "Hello World!");
        ///        GUI.Box(new Rect(10, 50, 50, 50), "A BOX");
        ///        GUI.Button(new Rect(10, 110, 70, 30), "A button");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static extern Color color { get; set; }
        ///<summary>Global tinting color for all background elements rendered by the GUI.</summary>
        ///<remarks>This gets multiplied by <see cref="color" />.
        ///
        ///<img src="GUIBackgroundColor.png" />
        ///
        ///Yellow Background color applied to a button.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    void OnGUI()
        ///    {
        ///        GUI.backgroundColor = Color.yellow;
        ///        GUI.Button(new Rect(10, 10, 70, 30), "A button");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="contentColor" />
        ///<seealso cref="color" />
        public static extern Color backgroundColor { get; set; }
        ///<summary>Tinting color for all text rendered by the GUI.</summary>
        ///<remarks>This gets multiplied by <see cref="color" />.
        ///
        ///<img src="GUIContentColor.png" />
        ///
        ///Yellow content color (font) in a button.</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// // Tints with yellow the letters of the button.
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    void OnGUI()
        ///    {
        ///        GUI.contentColor = Color.yellow;
        ///        GUI.Button(new Rect(10, 10, 70, 30), "A button");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="backgroundColor" />
        ///<seealso cref="color" />
        public static extern Color contentColor { get; set; }
        ///<summary>Returns true if any controls changed the value of the input data.</summary>
        ///<example>
        ///  <code><![CDATA[
        /// // Draws a text field and when it gets modified prints a message
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public string stringToEdit = "Modify me.";
        ///
        ///    void OnGUI()
        ///    {
        ///        // Make a text field that modifies stringToEdit.
        ///        stringToEdit = GUI.TextField(new Rect(10, 10, 200, 20), stringToEdit, 25);
        ///
        ///        if (GUI.changed)
        ///        {
        ///            Debug.Log("Text field has changed.");
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static extern bool changed { get; set; }
        ///<summary>Is the GUI enabled?</summary>
        ///<remarks>Set this value to false to disable all GUI interactions. All controls will be drawn semi-transparently, and will not respond to user input.
        ///
        ///<img src="GUIEnabled.png" />
        ///
        ///Enabled / Disabled GUI controls.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    // The value tracking whether or not the extended options can be toggled.
        ///    public bool allOptions = true;
        ///
        ///    // The 2 extended options.
        ///    public bool extended1 = true;
        ///    public bool extended2 = true;
        ///
        ///    void OnGUI()
        ///    {
        ///        // Make a toggle control that allows the user to edit some extended options.
        ///        allOptions = GUI.Toggle(new Rect(0, 0, 150, 20), allOptions, "Edit All Options");
        ///
        ///        // Assign the value of it to the GUI.enabled - if the checkbox above
        ///        // is disabled, so will these GUI elements be
        ///        GUI.enabled = allOptions;
        ///
        ///        // These two controls will only be enabled if the button above is on.
        ///        extended1 = GUI.Toggle(new Rect(20, 20, 130, 20), extended1, "Extended Option 1");
        ///        extended2 = GUI.Toggle(new Rect(20, 40, 130, 20), extended2, "Extended Option 2");
        ///
        ///        // We're done with the conditional block, so make GUI code be enabled again.
        ///        GUI.enabled = true;
        ///
        ///        // Make an Ok button
        ///        if (GUI.Button(new Rect(0, 60, 150, 20), "OK"))
        ///        {
        ///            print("user clicked ok");
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static extern bool enabled { get; set; }

        ///<summary>The sorting depth of the currently executing GUI behaviour.</summary>
        ///<remarks>
        ///  <para>Set this to determine ordering when you have different scripts running simultaneously.
        ///GUI elements drawn with lower depth values will appear on top of elements with higher values (ie, you can think of the depth as "distance" from the camera).
        ///
        ///**Note:**To see this example working, you will need to create 2
        ///scripts. Remember to name the scripts with the same name as the class
        ///names, else it will not work.
        ///
        ///<img src="GUIDepth.png" />
        ///
        ///One Button behind the other.</para>
        ///  <para>And copy this other example to another script:</para>
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        /// // Makes this button go back in depth
        ///
        ///public class Example1 : MonoBehaviour
        ///{
        ///    public int guiDepth = 0;
        ///    public Example2 example2;
        ///
        ///    private float buttonX, buttonY;
        ///
        ///    void Start()
        ///    {
        ///        buttonX = (Screen.width / 2) - 100;
        ///        buttonY = (Screen.height / 2) - 100;
        ///    }
        ///
        ///    void OnGUI()
        ///    {
        ///        GUI.depth = guiDepth;
        ///        GUI.color = Color.yellow;
        ///
        ///        GUIStyle size = new GUIStyle("button");
        ///        size.fontSize = 16;
        ///
        ///        if (GUI.RepeatButton(new Rect(buttonX, buttonY, 200, 100), "Go Backwards", size))
        ///        {
        ///            guiDepth = 1;
        ///            example2.guiDepth = 0;
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        /// // Makes this button go back in depth
        ///
        ///public class Example2 : MonoBehaviour
        ///{
        ///    public int guiDepth = 1;
        ///    public Example1 example1;
        ///
        ///    private float buttonX, buttonY;
        ///
        ///    void Start()
        ///    {
        ///        buttonX = (Screen.width / 2)  - 50;
        ///        buttonY = (Screen.height / 2) - 50;
        ///    }
        ///
        ///    void OnGUI()
        ///    {
        ///        GUI.depth = guiDepth;
        ///        GUI.color = Color.green;
        ///
        ///        GUIStyle size = new GUIStyle("button");
        ///        size.fontSize = 16;
        ///
        ///        if (GUI.RepeatButton(new Rect(buttonX, buttonY, 200, 100), "Go Backwards", size))
        ///        {
        ///            guiDepth = 1;
        ///            example1.guiDepth = 0;
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static extern int depth { get; set; }

        internal static extern bool usePageScrollbars { get; }
        internal static extern bool isInsideList { get; set; }
        internal static extern Material blendMaterial {[FreeFunction("GetGUIBlendMaterial")] get; }
        internal static extern Material blitMaterial {[FreeFunction("GetGUIBlitMaterial")] get; }
        internal static extern Material roundedRectMaterial {[FreeFunction("GetGUIRoundedRectMaterial")] get; }
        internal static extern Material roundedRectWithColorPerBorderMaterial { [FreeFunction("GetGUIRoundedRectWithColorPerBorderMaterial")] get; }

        internal static extern void GrabMouseControl(int id);
        internal static extern bool HasMouseControl(int id);
        internal static extern void ReleaseMouseControl();

        ///<summary>Set the name of the next control.</summary>
        ///<remarks>This makes the following control be registered with a given name.</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// // Sets the login textfield with "user".  If it is selected and the user
        /// // presses enter, it prints Login
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public string login = "username";
        ///    public string login2 = "no action here";
        ///
        ///    void OnGUI()
        ///    {
        ///        GUI.SetNextControlName("user");
        ///        login = GUI.TextField(new Rect(10, 10, 130, 20), login);
        ///
        ///        login2 = GUI.TextField(new Rect(10, 40, 130, 20), login2);
        ///        if (Event.current.Equals(Event.KeyboardEvent("return")) && GUI.GetNameOfFocusedControl() == "user")
        ///            Debug.Log("Login");
        ///
        ///        if (GUI.Button(new Rect(150, 10, 50, 20), "Login"))
        ///            Debug.Log("Login");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="GetNameOfFocusedControl" />
        ///<seealso cref="FocusControl" />
        [FreeFunction("GetGUIState().SetNameOfNextControl")]
        public static extern void SetNextControlName(string name);

        ///<summary>Get the name of named control that has focus.</summary>
        ///<remarks>
        ///  <para>Control names are set up by using <see cref="SetNextControlName" />. When a named control has focus, this function will return its name. If no control has focus or the focused control has no name set, an empty string will be returned instead.</para>
        ///  <para />
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public string login = "username";
        ///    public string login2 = "no action here";
        ///
        ///    void OnGUI()
        ///    {
        ///        GUI.SetNextControlName("user");
        ///        login = GUI.TextField(new Rect(10, 10, 130, 20), login);
        ///
        ///        login2 = GUI.TextField(new Rect(10, 40, 130, 20), login2);
        ///        if (Event.current.isKey && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "user")
        ///            Debug.Log("Login");
        ///
        ///        if (GUI.Button(new Rect(150, 10, 50, 20), "Login"))
        ///            Debug.Log("Login");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="SetNextControlName" />
        ///<seealso cref="FocusControl" />
        [FreeFunction("GetGUIState().GetNameOfFocusedControl")]
        public static extern string GetNameOfFocusedControl();

        ///<summary>Move keyboard focus to a named control.</summary>
        ///<remarks>
        ///
        ///For focusing text in Editor GUI text fields, see <see cref="M:UnityEditor.EditorGUI.FocusTextInControl" />.</remarks>
        ///<param name="name">Name set using <see cref="SetNextControlName" />.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    // When pressed the button, selects the "username" Textfield.
        ///    public string username = "username";
        ///    public string pwd = "a pwd";
        ///
        ///    void OnGUI()
        ///    {
        ///        // Set the internal name of the textfield
        ///        GUI.SetNextControlName("MyTextField");
        ///
        ///        // Make the actual text field.
        ///        username = GUI.TextField(new Rect(10, 10, 100, 20), username);
        ///        pwd = GUI.TextField(new Rect(10, 40, 100, 20), pwd);
        ///
        ///        // If the user presses this button, keyboard focus will move.
        ///        if (GUI.Button(new Rect(10, 70, 80, 20), "Move Focus"))
        ///            GUI.FocusControl("MyTextField");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="SetNextControlName" />
        ///<seealso cref="GetNameOfFocusedControl" />
        [FreeFunction("GetGUIState().FocusKeyboardControl")]
        public static extern void FocusControl(string name);

        internal static extern void InternalRepaintEditorWindow();
        private static extern string Internal_GetTooltip();
        private static extern void Internal_SetTooltip(string value);
        private static extern string Internal_GetMouseTooltip();
        private static extern Rect Internal_DoModalWindow(int id, EntityId entityId, Rect clientRect, WindowFunction func, GUIContent content, [UnityMarshalAs(NativeType.ScriptingObjectPtr)] GUIStyle style, System.Object skin);
        private static extern Rect Internal_DoWindow(int id, EntityId entityId, Rect clientRect, WindowFunction func, GUIContent title, [UnityMarshalAs(NativeType.ScriptingObjectPtr)] GUIStyle style, System.Object skin, bool forceRectOnLayout);

        ///<summary>Make a window draggable.</summary>
        ///<remarks>Insert a call to this function inside your window code to make a window draggable.</remarks>
        ///<param name="position">The part of the window that can be dragged. This is clipped to the actual window.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public Rect windowRect = new Rect(20, 20, 120, 50);
        ///
        ///    void OnGUI()
        ///    {
        ///        // Register the window.
        ///        windowRect = GUI.Window(0, windowRect, DoMyWindow, "My Window");
        ///    }
        ///
        ///    // Make the contents of the window
        ///    void DoMyWindow(int windowID)
        ///    {
        ///        // Make a very long rect that is 20 pixels tall.
        ///        // This will make the window be resizable by the top
        ///        // title bar - no matter how wide it gets.
        ///        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static extern void DragWindow(Rect position);
        ///<summary>Bring a specific window to front of the floating windows.</summary>
        ///<param name="windowID">The identifier used when you created the window in the <see cref="Window" /> call.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Draws 2 overlapped windows and when clicked on 1 window's button
        /// // Brings the other window to the front.
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    private Rect windowRect = new Rect(20, 20, 120, 50);
        ///    private Rect windowRect2 = new Rect(80, 20, 120, 50);
        ///
        ///    void OnGUI()
        ///    {
        ///        windowRect = GUI.Window(0, windowRect, DoMyFirstWindow, "First");
        ///        windowRect2 = GUI.Window(1, windowRect2, DoMySecondWindow, "Second");
        ///    }
        ///
        ///    void DoMyFirstWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "Bring to front"))
        ///            GUI.BringWindowToFront(1); // Bring the 2nd window to front
        ///
        ///        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        ///    }
        ///
        ///    void DoMySecondWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "Bring to front"))
        ///            GUI.BringWindowToFront(0); // Bring the 1rst window to front
        ///
        ///        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static extern void BringWindowToFront(int windowID);
        ///<summary>Bring a specific window to back of the floating windows.</summary>
        ///<param name="windowID">The identifier used when you created the window in the <see cref="Window" /> call.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Draws 2 overlapped windows and when clicked on 1 window's button
        /// // Brings the window to the back.
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    private Rect windowRect = new Rect(20, 20, 120, 50);
        ///    private Rect windowRect2 = new Rect(80, 20, 120, 50);
        ///    void OnGUI()
        ///    {
        ///        windowRect = GUI.Window(0, windowRect, DoMyFirstWindow, "First");
        ///        windowRect2 = GUI.Window(1, windowRect2, DoMySecondWindow, "Second");
        ///    }
        ///
        ///    void DoMyFirstWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "Put Back"))
        ///            GUI.BringWindowToBack(0);
        ///
        ///        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        ///    }
        ///
        ///    void DoMySecondWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "Put Back"))
        ///            GUI.BringWindowToBack(1);
        ///
        ///        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static extern void BringWindowToBack(int windowID);
        ///<summary>Make a window become the active window.</summary>
        ///<param name="windowID">The identifier used when you created the window in the <see cref="Window" /> call.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Draw 2 windows.
        /// // When the first window is button-clicked focus on the other window.
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    private Rect windowRect = new Rect(20, 20, 120, 50);
        ///    private Rect windowRect2 = new Rect(20, 80, 120, 50);
        ///
        ///    void OnGUI()
        ///    {
        ///        windowRect = GUI.Window(0, windowRect, DoMyFirstWindow, "First");
        ///        windowRect2 = GUI.Window(1, windowRect2, DoMySecondWindow, "Second");
        ///    }
        ///
        ///    void DoMyFirstWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "Focus other"))
        ///            GUI.FocusWindow(1);
        ///    }
        ///
        ///    void DoMySecondWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "Focus other"))
        ///            GUI.FocusWindow(0);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="GUI.UnfocusWindow" />
        public static extern void FocusWindow(int windowID);
        ///<summary>Remove focus from all windows.</summary>
        ///<example>
        ///  <code><![CDATA[
        /// // Draws 2 windows. When one button is clicked unfocus the window.
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    private Rect windowRect = new Rect(20, 20, 120, 50);
        ///    private Rect windowRect2 = new Rect(20, 80, 120, 50);
        ///
        ///    void OnGUI()
        ///    {
        ///        windowRect = GUI.Window(0, windowRect, DoMyFirstWindow, "First");
        ///        windowRect2 = GUI.Window(1, windowRect2, DoMySecondWindow, "Second");
        ///    }
        ///
        ///    void DoMyFirstWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "UnFocus"))
        ///        {
        ///            GUI.UnfocusWindow();
        ///        }
        ///    }
        ///
        ///    void DoMySecondWindow(int windowID)
        ///    {
        ///        if (GUI.Button(new Rect(10, 20, 100, 20), "UnFocus"))
        ///        {
        ///            GUI.UnfocusWindow();
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="GUI.FocusWindow" />
        public static extern void UnfocusWindow();
        private static extern void Internal_BeginWindows();
        private static extern void Internal_EndWindows();

        internal static extern string Internal_Concatenate(GUIContent first, GUIContent second);
    }
}
