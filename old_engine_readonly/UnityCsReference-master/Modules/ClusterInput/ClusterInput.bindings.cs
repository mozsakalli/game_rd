// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;

namespace UnityEngine
{
    ///<summary>Values to determine the type of input value to be expect from one entry of <see cref="ClusterInput" />.</summary>
    [Obsolete("This type is deprecated and will be removed in a future release.", false)]
    public enum ClusterInputType
    {
        ///<summary>Device that return a binary result of pressed or not pressed.</summary>
        ///<seealso cref="ClusterInput.GetButton" />
        Button = 0,
        ///<summary>Device is an analog axis that provides continuous value represented by a float.</summary>
        ///<seealso cref="ClusterInput.GetAxis" />
        Axis = 1,
        ///<summary>Device that provide position and orientation values.</summary>
        ///<seealso cref="ClusterInput.GetTrackerPosition" />
        ///<seealso cref="ClusterInput.GetTrackerRotation" />
        Tracker = 2,
        ///<summary>A user customized input.</summary>
        ///<remarks>See <see cref="ClusterInput" /> for more information.</remarks>
        CustomProvidedInput = 3
    }

    ///<summary>Interface for reading and writing inputs in a Unity Cluster.</summary>
    ///<remarks>ClusterInput provides access to VRPN devices by connecting to a VRPN server. It also provides access to writeable inputs. All inputs managed by ClusterInput will be replicated to the rest of the connected slaves in the cluster. Using ClusterInput is much like using the traditional Input system in Unity.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using System.Collections;
    ///
    ///public class ExampleClass : MonoBehaviour
    ///{
    ///    void Update()
    ///    {
    ///        // Buttons and Axis provide a single value.
    ///        bool buttonValue = ClusterInput.GetButton("button1");
    ///        float axisValue = ClusterInput.GetAxis("axis1");
    ///
    ///        // A tracker provides 2 values, rotation and position.
    ///        Vector3 position = ClusterInput.GetTrackerPosition("tracker1");
    ///        Quaternion rotation = ClusterInput.GetTrackerRotation("tracker1");
    ///
    ///        if (ClusterNetwork.isMasterOfCluster)
    ///        {
    ///            float axisValueCustom = MyCustomDevicePlugin.GetValue("myaxis");
    ///            ClusterInput.SetAxis("customAxis", axisValueCustom);
    ///        }
    ///    }
    ///}
    ///]]></code>
    ///</example>
    [NativeHeader("Modules/ClusterInput/ClusterInput.h")]
    [NativeConditional("ENABLE_CLUSTERINPUT")]
    [Obsolete("This type is deprecated and will be removed in a future release.", false)]
    public class ClusterInput
    {
        ///<summary>Returns the axis value as a continous float.</summary>
        ///<remarks>The range depends on configuration of VRPN server.</remarks>
        ///<param name="name">Name of input to poll.c.</param>
        extern public static float GetAxis(string name);
        ///<summary>Returns the binary value of a button.</summary>
        ///<remarks>True for pressed, false otherwise.</remarks>
        ///<param name="name">Name of input to poll.</param>
        extern public static bool GetButton(string name);
        ///<summary>Return the position of a tracker as a Vector3.</summary>
        ///<param name="name">Name of input to poll.</param>
        [NativeConditional("ENABLE_CLUSTERINPUT", "Vector3f(0.0f, 0.0f, 0.0f)")]
        extern public static Vector3 GetTrackerPosition(string name);
        ///<summary>Returns the rotation of a tracker as a Quaternion.</summary>
        ///<param name="name">Name of input to poll.</param>
        [NativeConditional("ENABLE_CLUSTERINPUT", "Quartenion::identity")]
        extern public static Quaternion GetTrackerRotation(string name);

        ///<summary>Sets the axis value for this input. Only works for input typed Custom.</summary>
        ///<param name="name">Name of input to modify.</param>
        ///<param name="value">Value to set.</param>
        extern public static void SetAxis(string name, float value);
        ///<summary>Sets the button value for this input. Only works for input typed Custom.</summary>
        ///<param name="name">Name of input to modify.</param>
        ///<param name="value">Value to set.</param>
        extern public static void SetButton(string name, bool value);
        ///<summary>Sets the tracker position for this input. Only works for input typed Custom.</summary>
        ///<param name="name">Name of input to modify.</param>
        ///<param name="value">Value to set.</param>
        extern public static void SetTrackerPosition(string name, Vector3 value);
        ///<summary>Sets the tracker rotation for this input. Only works for input typed Custom.</summary>
        ///<param name="name">Name of input to modify.</param>
        ///<param name="value">Value to set.</param>
        extern public static void SetTrackerRotation(string name, Quaternion value);

        ///<summary>Add a new VRPN input entry.</summary>
        ///<remarks>
        ///  <para>The parameters are identical to how you add a input via “Project Setting &gt; Cluster Input”. Input entry added via this method only valid for the lifetime of the application session. The added entry will not persist like those you added via the “Project Setting &gt; Cluster Input”.</para>
        ///  <para />
        ///</remarks>
        ///<param name="name">Name of the input entry. This has to be unique.</param>
        ///<param name="deviceName">Device name registered to VRPN server.</param>
        ///<param name="serverUrl">URL to the vrpn server.</param>
        ///<param name="index">Index of the Input entry, refer to vrpn.cfg if unsure.</param>
        ///<param name="type">Type of the input.</param>
        ///<returns>True if the operation succeed.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    void AddNewClusterInputEntry()
        ///    {
        ///        // Add a new entry named "new_button_1". Which is a mouse connected to VRPN at localhost.
        ///        ClusterInput.AddInput("new_button_1", "mouse_0", "localhost", 0, ClusterInputType.Button);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="ClusterInput.EditInput" />
        extern public static bool AddInput(string name, string deviceName, string serverUrl, int index, ClusterInputType type);
        ///<summary>Edit an input entry which added via <see cref="ClusterInput.AddInput" />.</summary>
        ///<remarks>This function is not able to edit persistent input entry defined at “Project Setting &gt; Cluster Input”.</remarks>
        ///<param name="name">Name of the input entry. This has to be unique.</param>
        ///<param name="deviceName">Device name registered to VRPN server.</param>
        ///<param name="serverUrl">URL to the vrpn server.</param>
        ///<param name="index">Index of the Input entry, refer to vrpn.cfg if unsure.</param>
        ///<param name="type">Type of the ClusterInputType as follow.</param>
        ///<seealso cref="ClusterInput.AddInput" />
        extern public static bool EditInput(string name, string deviceName, string serverUrl, int index, ClusterInputType type);
        ///<summary>Check the connection status of the device to the VRPN server it connected to.</summary>
        ///<param name="name">Name of the input entry.</param>
        extern public static bool CheckConnectionToServer(string name);
    }
}
