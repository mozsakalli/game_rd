// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    ///<summary>Use ForceMode to specify how to apply a force using <see cref="Rigidbody.AddForce" /> or <see cref="ArticulationBody.AddForce" />.</summary>
    ///<remarks>The AddForce function impacts how your GameObject moves by allowing you to define your own force vector, as well as choosing how to apply this force to the GameObject (this GameObject must have a Rigidbody component attached).
    ///
    ///ForceMode allows you to choose from four different ways to affect the GameObject using this Force: Acceleration, Force, Impulse, and VelocityChange.
    ///
    ///For more information on how ForceMode affects velocity, see <see cref="Rigidbody.AddForce" />.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///
    ///public class ForceModeExample : MonoBehaviour
    ///{
    ///    //Use to switch between Force Modes
    ///    enum ModeSwitching { Start, Impulse, Acceleration, Force, VelocityChange };
    ///    ModeSwitching m_ModeSwitching;
    ///
    ///    Vector3 m_StartPos, m_StartForce;
    ///    Vector3 m_NewForce;
    ///    Rigidbody m_Rigidbody;
    ///
    ///    string m_ForceXString = string.Empty;
    ///    string m_ForceYString = string.Empty;
    ///
    ///    float m_ForceX, m_ForceY;
    ///    float m_Result;
    ///
    ///    void Start()
    ///    {
    ///        //You get the Rigidbody component you attach to the GameObject
    ///        m_Rigidbody = GetComponent<Rigidbody>();
    ///
    ///        //This starts at first mode (nothing happening yet)
    ///        m_ModeSwitching = ModeSwitching.Start;
    ///
    ///        //Initialising the force which is used on GameObject in various ways
    ///        m_NewForce = new Vector3(-5.0f, 1.0f, 0.0f);
    ///
    ///        //Initialising floats
    ///        m_ForceX = 0;
    ///        m_ForceY = 0;
    ///
    ///        //The forces typed in from the text fields (the ones you can manipulate in Game view)
    ///        m_ForceXString = "0";
    ///        m_ForceYString = "0";
    ///
    ///        //The GameObject's starting position and Rigidbody position
    ///        m_StartPos = transform.position;
    ///        m_StartForce = m_Rigidbody.transform.position;
    ///    }
    ///
    ///    void FixedUpdate()
    ///    {
    ///        //If the current mode is not the starting mode (or the GameObject is not reset), the force can change
    ///        if (m_ModeSwitching != ModeSwitching.Start)
    ///        {
    ///            //The force changes depending what you input into the text fields
    ///            m_NewForce = new Vector3(m_ForceX, m_ForceY, 0);
    ///        }
    ///
    ///        //Here, switching modes depend on button presses in the Game mode
    ///        switch (m_ModeSwitching)
    ///        {
    ///            //This is the starting mode which resets the GameObject
    ///            case ModeSwitching.Start:
    ///                //This resets the GameObject and Rigidbody to their starting positions
    ///                transform.position = m_StartPos;
    ///                m_Rigidbody.transform.position = m_StartForce;
    ///
    ///                //This resets the velocity of the Rigidbody
    ///                m_Rigidbody.velocity = new Vector3(0f, 0f, 0f);
    ///                break;
    ///
    ///            //These are the modes ForceMode can force on a Rigidbody
    ///            //This is Acceleration mode
    ///            case ModeSwitching.Acceleration:
    ///                //The function converts the text fields into floats and updates the Rigidbody’s force
    ///                MakeCustomForce();
    ///
    ///                //Use Acceleration as the force on the Rigidbody
    ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Acceleration);
    ///                break;
    ///
    ///            //This is Force Mode, using a continuous force on the Rigidbody considering its mass
    ///            case ModeSwitching.Force:
    ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
    ///                MakeCustomForce();
    ///
    ///                //Use Force as the force on GameObject’s Rigidbody
    ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Force);
    ///                break;
    ///
    ///            //This is Impulse Mode, which involves using the Rigidbody’s mass to apply an instant impulse force.
    ///            case ModeSwitching.Impulse:
    ///                //The function converts the text fields into floats and updates the force applied to the Rigidbody
    ///                MakeCustomForce();
    ///
    ///                //Use Impulse as the force on GameObject
    ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Impulse);
    ///                break;
    ///
    ///            //This is VelocityChange which involves ignoring the mass of the GameObject and impacting it with a sudden speed change in a direction
    ///            case ModeSwitching.VelocityChange:
    ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
    ///                MakeCustomForce();
    ///
    ///                //Make a Velocity change on the Rigidbody
    ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.VelocityChange);
    ///                break;
    ///        }
    ///    }
    ///
    ///    //The function outputs buttons, text fields, and other interactable UI elements to the Scene in Game view
    ///    void OnGUI()
    ///    {
    ///        //Getting the inputs from each text field and storing them as strings
    ///        m_ForceXString = GUI.TextField(new Rect(300, 10, 200, 20), m_ForceXString, 25);
    ///        m_ForceYString = GUI.TextField(new Rect(300, 30, 200, 20), m_ForceYString, 25);
    ///
    ///        //Press the button to reset the GameObject and Rigidbody
    ///        if (GUI.Button(new Rect(100, 0, 150, 30), "Reset"))
    ///        {
    ///            //This switches to the start/reset case
    ///            m_ModeSwitching = ModeSwitching.Start;
    ///        }
    ///
    ///        //When you press the Acceleration button, switch to Acceleration mode
    ///        if (GUI.Button(new Rect(100, 30, 150, 30), "Apply Acceleration"))
    ///        {
    ///            //Switch to Acceleration (apply acceleration force to GameObject)
    ///            m_ModeSwitching = ModeSwitching.Acceleration;
    ///        }
    ///
    ///        //If you press the Impulse button
    ///        if (GUI.Button(new Rect(100, 60, 150, 30), "Apply Impulse"))
    ///        {
    ///            //Switch to impulse (apply impulse forces to GameObject)
    ///            m_ModeSwitching = ModeSwitching.Impulse;
    ///        }
    ///
    ///        //If you press the Force Button, switch to Force state
    ///        if (GUI.Button(new Rect(100, 90, 150, 30), "Apply Force"))
    ///        {
    ///            //Switch to Force (apply force to GameObject)
    ///            m_ModeSwitching = ModeSwitching.Force;
    ///        }
    ///
    ///        //Press the button to switch to VelocityChange state
    ///        if (GUI.Button(new Rect(100, 120, 150, 30), "Apply Velocity Change"))
    ///        {
    ///            //Switch to velocity changing
    ///            m_ModeSwitching = ModeSwitching.VelocityChange;
    ///        }
    ///    }
    ///
    ///    //Changing strings to floats for the forces
    ///    float ConvertToFloat(string Name)
    ///    {
    ///        float.TryParse(Name, out m_Result);
    ///        return m_Result;
    ///    }
    ///
    ///    //Set the converted float from the text fields as the forces to apply to the Rigidbody
    ///    void MakeCustomForce()
    ///    {
    ///        //This converts the strings to floats
    ///        m_ForceX = ConvertToFloat(m_ForceXString);
    ///        m_ForceY = ConvertToFloat(m_ForceYString);
    ///    }
    ///}
    ///]]></code>
    ///</example>
    public enum ForceMode
    {
        ///<summary>Add a continuous force to the rigidbody, using its mass.</summary>
        ///<remarks>Apply the force in each FixedUpdate over a duration of time. This mode depends on the mass of rigidbody so more force must be applied to push or twist higher-mass objects the same amount as lower-mass objects.
        ///This mode is useful for setting up realistic physics where it takes more force to move heavier objects.
        ///In this mode, the unit of the force parameter is applied to the rigidbody as mass*distance/time^2.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class ForceModeExample : MonoBehaviour
        ///{
        ///    //Use to switch between Force Modes
        ///    enum ModeSwitching { Start, Impulse, Acceleration, Force, VelocityChange };
        ///    ModeSwitching m_ModeSwitching;
        ///
        ///    Vector3 m_StartPos, m_StartForce;
        ///    Vector3 m_NewForce;
        ///    Rigidbody m_Rigidbody;
        ///
        ///    string m_ForceXString = string.Empty;
        ///    string m_ForceYString = string.Empty;
        ///
        ///    float m_ForceX, m_ForceY;
        ///    float m_Result;
        ///
        ///
        ///    void Start()
        ///    {
        ///        //You get the Rigidbody component you attach to the GameObject
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///
        ///        //This starts at first mode (nothing happening yet)
        ///        m_ModeSwitching = ModeSwitching.Start;
        ///
        ///        //Initialising the force which is used on GameObject in various ways
        ///        m_NewForce = new Vector3(-5.0f, 1.0f, 0.0f);
        ///
        ///        //Initialising floats
        ///        m_ForceX = 0;
        ///        m_ForceY = 0;
        ///
        ///        //The forces typed in from the text fields (the ones you can manipulate in Game view)
        ///        m_ForceXString = "0";
        ///        m_ForceYString = "0";
        ///
        ///        //The GameObject's starting position and Rigidbody position
        ///        m_StartPos = transform.position;
        ///        m_StartForce = m_Rigidbody.transform.position;
        ///    }
        ///
        ///    void FixedUpdate()
        ///    {
        ///        //If the current mode is not the starting mode (or the GameObject is not reset), the force can change
        ///        if (m_ModeSwitching != ModeSwitching.Start)
        ///        {
        ///            //The force changes depending what you input into the text fields
        ///            m_NewForce = new Vector3(m_ForceX, m_ForceY, 0);
        ///        }
        ///
        ///        //Here, switching modes depend on button presses in the Game mode
        ///        switch (m_ModeSwitching)
        ///        {
        ///            //This is the starting mode which resets the GameObject
        ///            case ModeSwitching.Start:
        ///                //This resets the GameObject and Rigidbody to their starting positions
        ///                transform.position = m_StartPos;
        ///                m_Rigidbody.transform.position = m_StartForce;
        ///
        ///                //This resets the velocity of the Rigidbody
        ///                m_Rigidbody.velocity = new Vector3(0f, 0f, 0f);
        ///                break;
        ///
        ///            //These are the modes ForceMode can force on a Rigidbody
        ///            //This is Acceleration mode
        ///            case ModeSwitching.Acceleration:
        ///                //The function converts the text fields into floats and updates the Rigidbody’s force
        ///                MakeCustomForce();
        ///
        ///                //Use Acceleration as the force on the Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Acceleration);
        ///                break;
        ///
        ///            //This is Force Mode, using a continuous force on the Rigidbody considering its mass
        ///            case ModeSwitching.Force:
        ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Use Force as the force on GameObject’s Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Force);
        ///                break;
        ///
        ///            //This is Impulse Mode, which involves using the Rigidbody’s mass to apply an instant impulse force.
        ///            case ModeSwitching.Impulse:
        ///                //The function converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Use Impulse as the force on GameObject
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Impulse);
        ///                break;
        ///
        ///
        ///            //This is VelocityChange which involves ignoring the mass of the GameObject and impacting it with a sudden speed change in a direction
        ///            case ModeSwitching.VelocityChange:
        ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Make a Velocity change on the Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.VelocityChange);
        ///                break;
        ///        }
        ///    }
        ///
        ///    //The function outputs buttons, text fields, and other interactable UI elements to the Scene in Game view
        ///    void OnGUI()
        ///    {
        ///        //Getting the inputs from each text field and storing them as strings
        ///        m_ForceXString = GUI.TextField(new Rect(300, 10, 200, 20), m_ForceXString, 25);
        ///        m_ForceYString = GUI.TextField(new Rect(300, 30, 200, 20), m_ForceYString, 25);
        ///
        ///        //Press the button to reset the GameObject and Rigidbody
        ///        if (GUI.Button(new Rect(100, 0, 150, 30), "Reset"))
        ///        {
        ///            //This switches to the start/reset case
        ///            m_ModeSwitching = ModeSwitching.Start;
        ///        }
        ///
        ///        //When you press the Acceleration button, switch to Acceleration mode
        ///        if (GUI.Button(new Rect(100, 30, 150, 30), "Apply Acceleration"))
        ///        {
        ///            //Switch to Acceleration (apply acceleration force to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Acceleration;
        ///        }
        ///
        ///        //If you press the Impulse button
        ///        if (GUI.Button(new Rect(100, 60, 150, 30), "Apply Impulse"))
        ///        {
        ///            //Switch to impulse (apply impulse forces to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Impulse;
        ///        }
        ///
        ///        //If you press the Force Button, switch to Force state
        ///        if (GUI.Button(new Rect(100, 90, 150, 30), "Apply Force"))
        ///        {
        ///            //Switch to Force (apply force to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Force;
        ///        }
        ///
        ///        //Press the button to switch to VelocityChange state
        ///        if (GUI.Button(new Rect(100, 120, 150, 30), "Apply Velocity Change"))
        ///        {
        ///            //Switch to velocity changing
        ///            m_ModeSwitching = ModeSwitching.VelocityChange;
        ///        }
        ///    }
        ///
        ///    //Changing strings to floats for the forces
        ///    float ConvertToFloat(string Name)
        ///    {
        ///        float.TryParse(Name, out m_Result);
        ///        return m_Result;
        ///    }
        ///
        ///    //Set the converted float from the text fields as the forces to apply to the Rigidbody
        ///    void MakeCustomForce()
        ///    {
        ///        //This converts the strings to floats
        ///        m_ForceX = ConvertToFloat(m_ForceXString);
        ///        m_ForceY = ConvertToFloat(m_ForceYString);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        Force = 0,
        ///<summary>Add a continuous acceleration to the rigidbody, ignoring its mass.</summary>
        ///<remarks>Apply the acceleration in each FixedUpdate over a duration of time. In contrast to <see cref="ForceMode.Force" />, Acceleration will move every rigidbody the same way regardless of differences in mass.
        ///This mode is useful if you just want to control the acceleration of an object directly.
        ///In this mode, the unit of the force parameter is applied to the rigidbody as distance/time^2.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class ForceModeExample : MonoBehaviour
        ///{
        ///    //Use to switch between Force Modes
        ///    enum ModeSwitching { Start, Acceleration };
        ///    ModeSwitching m_ModeSwitching;
        ///
        ///    Vector3 m_StartPos, m_StartForce;
        ///    Vector3 m_NewForce;
        ///    Rigidbody m_Rigidbody;
        ///
        ///    string m_ForceXString = string.Empty;
        ///    string m_ForceYString = string.Empty;
        ///
        ///    float m_ForceX, m_ForceY;
        ///    float m_Result;
        ///
        ///
        ///    void Start()
        ///    {
        ///        //You get the Rigidbody component you attach to the GameObject
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///
        ///        //This starts at first mode (nothing happening yet)
        ///        m_ModeSwitching = ModeSwitching.Start;
        ///
        ///        //Initialising the force which is used on GameObject in various ways
        ///        m_NewForce = new Vector3(-5.0f, 1.0f, 0.0f);
        ///
        ///        //Initialising floats
        ///        m_ForceX = 0;
        ///        m_ForceY = 0;
        ///
        ///        //The forces typed in from the text fields (the ones you can manipulate in Game view)
        ///        m_ForceXString = "0";
        ///        m_ForceYString = "0";
        ///
        ///        //The GameObject's starting position and Rigidbody position
        ///        m_StartPos = transform.position;
        ///        m_StartForce = m_Rigidbody.transform.position;
        ///    }
        ///
        ///    void FixedUpdate()
        ///    {
        ///        //If the current mode is not the starting mode (or the GameObject is not reset), the force can change
        ///        if (m_ModeSwitching != ModeSwitching.Start)
        ///        {
        ///            //The force changes depending what you input into the text fields
        ///            m_NewForce = new Vector3(m_ForceX, m_ForceY, 0);
        ///        }
        ///
        ///        //Here, switching modes depend on button presses in the Game mode
        ///        switch (m_ModeSwitching)
        ///        {
        ///            //This is the starting mode which resets the GameObject
        ///            case ModeSwitching.Start:
        ///                //This resets the GameObject and Rigidbody to their starting positions
        ///                transform.position = m_StartPos;
        ///                m_Rigidbody.transform.position = m_StartForce;
        ///
        ///                //This resets the velocity of the Rigidbody
        ///                m_Rigidbody.velocity = new Vector3(0f, 0f, 0f);
        ///                break;
        ///
        ///            //These are the modes ForceMode can force on a Rigidbody
        ///            //This is Acceleration mode
        ///            case ModeSwitching.Acceleration:
        ///                //The function converts the text fields into floats and updates the Rigidbody’s force
        ///                MakeCustomForce();
        ///
        ///                //Use Acceleration as the force on the Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Acceleration);
        ///                break;
        ///        }
        ///    }
        ///
        ///    //The function outputs buttons, text fields, and other interactable UI elements to the Scene in Game view
        ///    void OnGUI()
        ///    {
        ///        //Getting the inputs from each text field and storing them as strings
        ///        m_ForceXString = GUI.TextField(new Rect(300, 10, 200, 20), m_ForceXString, 25);
        ///        m_ForceYString = GUI.TextField(new Rect(300, 30, 200, 20), m_ForceYString, 25);
        ///
        ///        //Press the button to reset the GameObject and Rigidbody
        ///        if (GUI.Button(new Rect(100, 0, 150, 30), "Reset"))
        ///        {
        ///            //This switches to the start/reset case
        ///            m_ModeSwitching = ModeSwitching.Start;
        ///        }
        ///
        ///        //When you press the Acceleration button, switch to Acceleration mode
        ///        if (GUI.Button(new Rect(100, 30, 150, 30), "Apply Acceleration"))
        ///        {
        ///            //Switch to Acceleration (apply acceleration force to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Acceleration;
        ///        }
        ///    }
        ///
        ///    //Changing strings to floats for the forces
        ///    float ConvertToFloat(string Name)
        ///    {
        ///        float.TryParse(Name, out m_Result);
        ///        return m_Result;
        ///    }
        ///
        ///    //Set the converted float from the text fields as the forces to apply to the Rigidbody
        ///    void MakeCustomForce()
        ///    {
        ///        //This converts the strings to floats
        ///        m_ForceX = ConvertToFloat(m_ForceXString);
        ///        m_ForceY = ConvertToFloat(m_ForceYString);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        Acceleration = 5,
        ///<summary>Add an instant force impulse to the rigidbody, using its mass.</summary>
        ///<remarks>Apply the impulse force instantly with a single function call. This mode depends on the mass of rigidbody so more force must be applied to push or twist higher-mass objects the same amount as lower-mass objects.
        ///This mode is useful for applying forces that happen instantly, such as forces from explosions or collisions.
        ///In this mode, the unit of the force parameter is applied to the rigidbody as mass*distance/time.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class ForceModeExample : MonoBehaviour
        ///{
        ///    //Use to switch between Force Modes
        ///    enum ModeSwitching { Start, Impulse, Acceleration, Force, VelocityChange };
        ///    ModeSwitching m_ModeSwitching;
        ///
        ///    Vector3 m_StartPos, m_StartForce;
        ///    Vector3 m_NewForce;
        ///    Rigidbody m_Rigidbody;
        ///
        ///    string m_ForceXString = string.Empty;
        ///    string m_ForceYString = string.Empty;
        ///
        ///    float m_ForceX, m_ForceY;
        ///    float m_Result;
        ///
        ///
        ///    void Start()
        ///    {
        ///        //You get the Rigidbody component you attach to the GameObject
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///
        ///        //This starts at first mode (nothing happening yet)
        ///        m_ModeSwitching = ModeSwitching.Start;
        ///
        ///        //Initialising the force which is used on GameObject in various ways
        ///        m_NewForce = new Vector3(-5.0f, 1.0f, 0.0f);
        ///
        ///        //Initialising floats
        ///        m_ForceX = 0;
        ///        m_ForceY = 0;
        ///
        ///        //The forces typed in from the text fields (the ones you can manipulate in Game view)
        ///        m_ForceXString = "0";
        ///        m_ForceYString = "0";
        ///
        ///        //The GameObject's starting position and Rigidbody position
        ///        m_StartPos = transform.position;
        ///        m_StartForce = m_Rigidbody.transform.position;
        ///    }
        ///
        ///    void FixedUpdate()
        ///    {
        ///        //If the current mode is not the starting mode (or the GameObject is not reset), the force can change
        ///        if (m_ModeSwitching != ModeSwitching.Start)
        ///        {
        ///            //The force changes depending what you input into the text fields
        ///            m_NewForce = new Vector3(m_ForceX, m_ForceY, 0);
        ///        }
        ///
        ///        //Here, switching modes depend on button presses in the Game mode
        ///        switch (m_ModeSwitching)
        ///        {
        ///            //This is the starting mode which resets the GameObject
        ///            case ModeSwitching.Start:
        ///                //This resets the GameObject and Rigidbody to their starting positions
        ///                transform.position = m_StartPos;
        ///                m_Rigidbody.transform.position = m_StartForce;
        ///
        ///                //This resets the velocity of the Rigidbody
        ///                m_Rigidbody.velocity = new Vector3(0f, 0f, 0f);
        ///                break;
        ///
        ///            //These are the modes ForceMode can force on a Rigidbody
        ///            //This is Acceleration mode
        ///            case ModeSwitching.Acceleration:
        ///                //The function converts the text fields into floats and updates the Rigidbody’s force
        ///                MakeCustomForce();
        ///
        ///                //Use Acceleration as the force on the Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Acceleration);
        ///                break;
        ///
        ///            //This is Force Mode, using a continuous force on the Rigidbody considering its mass
        ///            case ModeSwitching.Force:
        ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Use Force as the force on GameObject’s Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Force);
        ///                break;
        ///
        ///            //This is Impulse Mode, which involves using the Rigidbody’s mass to apply an instant impulse force.
        ///            case ModeSwitching.Impulse:
        ///                //The function converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Use Impulse as the force on GameObject
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Impulse);
        ///                break;
        ///
        ///
        ///            //This is VelocityChange which involves ignoring the mass of the GameObject and impacting it with a sudden speed change in a direction
        ///            case ModeSwitching.VelocityChange:
        ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Make a Velocity change on the Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.VelocityChange);
        ///                break;
        ///        }
        ///    }
        ///
        ///    //The function outputs buttons, text fields, and other interactable UI elements to the Scene in Game view
        ///    void OnGUI()
        ///    {
        ///        //Getting the inputs from each text field and storing them as strings
        ///        m_ForceXString = GUI.TextField(new Rect(300, 10, 200, 20), m_ForceXString, 25);
        ///        m_ForceYString = GUI.TextField(new Rect(300, 30, 200, 20), m_ForceYString, 25);
        ///
        ///        //Press the button to reset the GameObject and Rigidbody
        ///        if (GUI.Button(new Rect(100, 0, 150, 30), "Reset"))
        ///        {
        ///            //This switches to the start/reset case
        ///            m_ModeSwitching = ModeSwitching.Start;
        ///        }
        ///
        ///        //When you press the Acceleration button, switch to Acceleration mode
        ///        if (GUI.Button(new Rect(100, 30, 150, 30), "Apply Acceleration"))
        ///        {
        ///            //Switch to Acceleration (apply acceleration force to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Acceleration;
        ///        }
        ///
        ///        //If you press the Impulse button
        ///        if (GUI.Button(new Rect(100, 60, 150, 30), "Apply Impulse"))
        ///        {
        ///            //Switch to impulse (apply impulse forces to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Impulse;
        ///        }
        ///
        ///        //If you press the Force Button, switch to Force state
        ///        if (GUI.Button(new Rect(100, 90, 150, 30), "Apply Force"))
        ///        {
        ///            //Switch to Force (apply force to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Force;
        ///        }
        ///
        ///        //Press the button to switch to VelocityChange state
        ///        if (GUI.Button(new Rect(100, 120, 150, 30), "Apply Velocity Change"))
        ///        {
        ///            //Switch to velocity changing
        ///            m_ModeSwitching = ModeSwitching.VelocityChange;
        ///        }
        ///    }
        ///
        ///    //Changing strings to floats for the forces
        ///    float ConvertToFloat(string Name)
        ///    {
        ///        float.TryParse(Name, out m_Result);
        ///        return m_Result;
        ///    }
        ///
        ///    //Set the converted float from the text fields as the forces to apply to the Rigidbody
        ///    void MakeCustomForce()
        ///    {
        ///        //This converts the strings to floats
        ///        m_ForceX = ConvertToFloat(m_ForceXString);
        ///        m_ForceY = ConvertToFloat(m_ForceYString);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        Impulse = 1,
        ///<summary>Add an instant velocity change to the rigidbody, ignoring its mass.</summary>
        ///<remarks>Apply the velocity change instantly with a single function call. In contrast to <see cref="ForceMode.Impulse" />, VelocityChange will change the velocity of every rigidbody the same way regardless of differences in mass.
        ///This mode is useful for something like a fleet of differently-sized space ships that you want to control without accounting for differences in mass.
        ///In this mode, the unit of the force parameter is applied to the rigidbody as distance/time.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class ForceModeExample : MonoBehaviour
        ///{
        ///    //Use to switch between Force Modes
        ///    enum ModeSwitching { Start, Impulse, Acceleration, Force, VelocityChange };
        ///    ModeSwitching m_ModeSwitching;
        ///
        ///    Vector3 m_StartPos, m_StartForce;
        ///    Vector3 m_NewForce;
        ///    Rigidbody m_Rigidbody;
        ///
        ///    string m_ForceXString = string.Empty;
        ///    string m_ForceYString = string.Empty;
        ///
        ///    float m_ForceX, m_ForceY;
        ///    float m_Result;
        ///
        ///
        ///    void Start()
        ///    {
        ///        //You get the Rigidbody component you attach to the GameObject
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///
        ///        //This starts at first mode (nothing happening yet)
        ///        m_ModeSwitching = ModeSwitching.Start;
        ///
        ///        //Initialising the force which is used on GameObject in various ways
        ///        m_NewForce = new Vector3(-5.0f, 1.0f, 0.0f);
        ///
        ///        //Initialising floats
        ///        m_ForceX = 0;
        ///        m_ForceY = 0;
        ///
        ///        //The forces typed in from the text fields (the ones you can manipulate in Game view)
        ///        m_ForceXString = "0";
        ///        m_ForceYString = "0";
        ///
        ///        //The GameObject's starting position and Rigidbody position
        ///        m_StartPos = transform.position;
        ///        m_StartForce = m_Rigidbody.transform.position;
        ///    }
        ///
        ///    void FixedUpdate()
        ///    {
        ///        //If the current mode is not the starting mode (or the GameObject is not reset), the force can change
        ///        if (m_ModeSwitching != ModeSwitching.Start)
        ///        {
        ///            //The force changes depending what you input into the text fields
        ///            m_NewForce = new Vector3(m_ForceX, m_ForceY, 0);
        ///        }
        ///
        ///        //Here, switching modes depend on button presses in the Game mode
        ///        switch (m_ModeSwitching)
        ///        {
        ///            //This is the starting mode which resets the GameObject
        ///            case ModeSwitching.Start:
        ///                //This resets the GameObject and Rigidbody to their starting positions
        ///                transform.position = m_StartPos;
        ///                m_Rigidbody.transform.position = m_StartForce;
        ///
        ///                //This resets the velocity of the Rigidbody
        ///                m_Rigidbody.velocity = new Vector3(0f, 0f, 0f);
        ///                break;
        ///
        ///            //These are the modes ForceMode can force on a Rigidbody
        ///            //This is Acceleration mode
        ///            case ModeSwitching.Acceleration:
        ///                //The function converts the text fields into floats and updates the Rigidbody’s force
        ///                MakeCustomForce();
        ///
        ///                //Use Acceleration as the force on the Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Acceleration);
        ///                break;
        ///
        ///            //This is Force Mode, using a continuous force on the Rigidbody considering its mass
        ///            case ModeSwitching.Force:
        ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Use Force as the force on GameObject’s Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Force);
        ///                break;
        ///
        ///            //This is Impulse Mode, which involves using the Rigidbody’s mass to apply an instant impulse force.
        ///            case ModeSwitching.Impulse:
        ///                //The function converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Use Impulse as the force on GameObject
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.Impulse);
        ///                break;
        ///
        ///
        ///            //This is VelocityChange which involves ignoring the mass of the GameObject and impacting it with a sudden speed change in a direction
        ///            case ModeSwitching.VelocityChange:
        ///                //Converts the text fields into floats and updates the force applied to the Rigidbody
        ///                MakeCustomForce();
        ///
        ///                //Make a Velocity change on the Rigidbody
        ///                m_Rigidbody.AddForce(m_NewForce, ForceMode.VelocityChange);
        ///                break;
        ///        }
        ///    }
        ///
        ///    //The function outputs buttons, text fields, and other interactable UI elements to the Scene in Game view
        ///    void OnGUI()
        ///    {
        ///        //Getting the inputs from each text field and storing them as strings
        ///        m_ForceXString = GUI.TextField(new Rect(300, 10, 200, 20), m_ForceXString, 25);
        ///        m_ForceYString = GUI.TextField(new Rect(300, 30, 200, 20), m_ForceYString, 25);
        ///
        ///        //Press the button to reset the GameObject and Rigidbody
        ///        if (GUI.Button(new Rect(100, 0, 150, 30), "Reset"))
        ///        {
        ///            //This switches to the start/reset case
        ///            m_ModeSwitching = ModeSwitching.Start;
        ///        }
        ///
        ///        //When you press the Acceleration button, switch to Acceleration mode
        ///        if (GUI.Button(new Rect(100, 30, 150, 30), "Apply Acceleration"))
        ///        {
        ///            //Switch to Acceleration (apply acceleration force to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Acceleration;
        ///        }
        ///
        ///        //If you press the Impulse button
        ///        if (GUI.Button(new Rect(100, 60, 150, 30), "Apply Impulse"))
        ///        {
        ///            //Switch to impulse (apply impulse forces to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Impulse;
        ///        }
        ///
        ///        //If you press the Force Button, switch to Force state
        ///        if (GUI.Button(new Rect(100, 90, 150, 30), "Apply Force"))
        ///        {
        ///            //Switch to Force (apply force to GameObject)
        ///            m_ModeSwitching = ModeSwitching.Force;
        ///        }
        ///
        ///        //Press the button to switch to VelocityChange state
        ///        if (GUI.Button(new Rect(100, 120, 150, 30), "Apply Velocity Change"))
        ///        {
        ///            //Switch to velocity changing
        ///            m_ModeSwitching = ModeSwitching.VelocityChange;
        ///        }
        ///    }
        ///
        ///    //Changing strings to floats for the forces
        ///    float ConvertToFloat(string Name)
        ///    {
        ///        float.TryParse(Name, out m_Result);
        ///        return m_Result;
        ///    }
        ///
        ///    //Set the converted float from the text fields as the forces to apply to the Rigidbody
        ///    void MakeCustomForce()
        ///    {
        ///        //This converts the strings to floats
        ///        m_ForceX = ConvertToFloat(m_ForceXString);
        ///        m_ForceY = ConvertToFloat(m_ForceYString);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        VelocityChange = 2,
    }
}
