// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    ///<summary>The collision detection mode constants used for <see cref="Rigidbody.collisionDetectionMode" />.</summary>
    ///<example>
    ///  <code><![CDATA[
    /// //This script allows you to switch collision detection mode at the press of the space key, and move your GameObject. It also outputs collisions that occur to the console.
    /// //Attach this script to a GameObject and make sure it has a Rigidbody component
    /// //If it doesn't have a Rigidbody component, click the GameObject, go to its Inspector and click the __Add Component__ button. Then, go to __Physics__>__Rigidbody__.
    /// //Create another GameObject. Make sure it has a Collider, so you can test collisions between them.
    ///
    ///using UnityEngine;
    ///using UnityEngine.InputSystem;
    ///
    ///public class Example : MonoBehaviour
    ///{
    ///    private Rigidbody rb;
    ///    private float moveSpeed = 5f;
    ///
    ///    [Header("Input Actions")]
    ///    public InputActionReference moveAction;
    ///
    ///    private void OnEnable()
    ///    {
    ///        moveAction.action.Enable();
    ///    }
    ///
    ///    private void OnDisable()
    ///    {
    ///        moveAction.action.Disable();
    ///    }
    ///    
    ///    void Start()
    ///    {
    ///        //Fetch the Rigidbody of the GameObject (make sure this is attached in the Inspector window)
    ///        rb = GetComponent<Rigidbody>();
    ///        //Make sure the Rigidbody can't rotate or move in the z axis for this example
    ///        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
    ///    }
    ///
    ///    void Update()
    ///    {
    ///        //Change the GameObject's movement in the X axis
    ///        float translationX = moveAction.action.ReadValue<Vector2>().x * moveSpeed;
    ///        //Change the GameObject's movement in the Y axis
    ///        float translationY = moveAction.action.ReadValue<Vector2>().y * moveSpeed;
    ///
    ///        //Move the GameObject
    ///        transform.Translate(new Vector3(translationX, translationY, 0));
    ///
    ///        //Press the space key to switch the collision detection mode
    ///        if (Keyboard.current.spaceKey.wasPressedThisFrame)
    ///            SwitchCollisionDetectionMode();
    ///    }
    ///
    ///    //Detect when there is a collision starting
    ///    void OnCollisionEnter(Collision collision)
    ///    {
    ///        //Ouput the Collision to the console
    ///        Debug.Log("Collision : " + collision.gameObject.name);
    ///    }
    ///
    ///    //Detect when there is are ongoing Collisions
    ///    void OnCollisionStay(Collision collision)
    ///    {
    ///        //Output the Collision to the console
    ///        Debug.Log("Stay : " + collision.gameObject.name);
    ///    }
    ///
    ///    //Switch between the different Collision Detection Modes
    ///    void SwitchCollisionDetectionMode()
    ///    {
    ///        switch (rb.collisionDetectionMode)
    ///        {
    ///            //If the current mode is continuous, switch it to continuous dynamic mode
    ///            case CollisionDetectionMode.Continuous:
    ///                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    ///                break;
    ///            //If the current mode is continuous dynamic, switch it to continuous speculative
    ///            case CollisionDetectionMode.ContinuousDynamic:
    ///                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    ///                break;
    ///
    ///            // If the current mode is continuous speculative, switch it to discrete mode
    ///            case CollisionDetectionMode.ContinuousSpeculative:
    ///                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
    ///                break;
    ///
    ///            //If the current mode is discrete, switch it to continuous mode
    ///            case CollisionDetectionMode.Discrete:
    ///                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    ///                break;
    ///        }
    ///    }
    ///}
    ///]]></code>
    ///</example>
    public enum CollisionDetectionMode
    {
        ///<summary>Continuous collision detection is off for this Rigidbody.</summary>
        ///<remarks>This is the default collision detection mode, and it is the fastest mode. Collisions for this collider will
        ///only be checked at the content's <see cref="Time.fixedDeltaTime" />.</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// //This script allows you to switch collision detection mode at the press of the space key
        /// //Attach this script to a GameObject
        /// //Click the GameObject, go to its Inspector and click the __Add Component__ Button. Then, go to __Physics__>__Rigidbody__.
        ///
        ///using UnityEngine;
        ///using UnityEngine.UI;
        ///using UnityEngine.InputSystem;
        ///                        
        ///public class Example : MonoBehaviour
        ///{
        ///    Rigidbody m_Rigidbody;
        ///
        ///    void Start()
        ///    {
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///    }
        ///
        ///    public void Update()
        ///    {
        ///        //Press the space key to switch the collision detection mode
        ///        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        ///            SwitchCollisionDetectionMode();
        ///    }
        ///
        ///    //Switch between the different Collision Detection Modes
        ///    void SwitchCollisionDetectionMode()
        ///    {
        ///        switch (m_Rigidbody.collisionDetectionMode)
        ///        {
        ///            //If the current mode is continuous, switch it to continuous dynamic mode
        ///            case CollisionDetectionMode.Continuous:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ///                break;
        ///            //If the current mode is continuous dynamic, switch it to continuous speculative
        ///            case CollisionDetectionMode.ContinuousDynamic:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ///                break;
        ///
        ///            // If the current mode is continuous speculative, switch it to discrete mode
        ///            case CollisionDetectionMode.ContinuousSpeculative:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        ///                break;
        ///
        ///            //If the current mode is discrete, switch it to continuous mode
        ///            case CollisionDetectionMode.Discrete:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        ///                break;
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        Discrete = 0,
        ///<summary>Continuous collision detection is on for colliding with static mesh geometry.</summary>
        ///<remarks>Collisions will be detected for any static mesh geometry in the path of this Rigidbody, even if the collision occurs
        ///between two FixedUpdate steps. Static mesh geometry is any MeshCollider which does not have a Rigidbody attached.
        ///This also prevent Rigidbodies set to ContinuousDynamic mode from passing through this Rigidbody.</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// //This script allows you to switch collision detection mode at the press of the space key
        /// //Attach this script to a GameObject
        /// //Click the GameObject, go to its Inspector and click the __Add Component__ Button. Then, go to __Physics__>__Rigidbody__.
        ///
        ///using UnityEngine;
        ///using UnityEngine.UI;
        ///using UnityEngine.InputSystem;
        ///                        
        ///public class Example : MonoBehaviour
        ///{
        ///    Rigidbody m_Rigidbody;
        ///
        ///    void Start()
        ///    {
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///    }
        ///
        ///    public void Update()
        ///    {
        ///        //Press the space key to switch the collision detection mode
        ///        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        ///            SwitchCollisionDetectionMode();
        ///    }
        ///
        ///    //Switch between the different Collision Detection Modes
        ///    void SwitchCollisionDetectionMode()
        ///    {
        ///        switch (m_Rigidbody.collisionDetectionMode)
        ///        {
        ///            //If the current mode is continuous, switch it to continuous dynamic mode
        ///            case CollisionDetectionMode.Continuous:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ///                break;
        ///            //If the current mode is continuous dynamic, switch it to discrete mode
        ///            case CollisionDetectionMode.ContinuousDynamic:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ///                break;
        ///
        ///            // If the curren mode is continuous speculative, switch it to discrete mode
        ///            case CollisionDetectionMode.ContinuousSpeculative:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        ///                break;
        ///
        ///            //If the current mode is discrete, switch it to continuous mode
        ///            case CollisionDetectionMode.Discrete:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        ///                break;
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        Continuous = 1,
        ///<summary>Continuous collision detection is on for colliding with static and dynamic geometry.</summary>
        ///<remarks>Prevent this Rigidbody from passing through static mesh geometry, and through other
        ///Rigidbodies which have continuous collision detection enabled, when it is moving fast.
        ///This is the slowest collision detection mode, and should only be used for selected fast moving objects.</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// //This script allows you to switch collision detection mode at the press of the space key
        /// //Attach this script to a GameObject
        /// //Click the GameObject, go to its Inspector and click the __Add Component__ Button. Then, go to __Physics__>__Rigidbody__.
        ///
        ///using UnityEngine;
        ///using UnityEngine.UI;
        ///using UnityEngine.InputSystem;
        ///                        
        ///public class Example : MonoBehaviour
        ///{
        ///    Rigidbody m_Rigidbody;
        ///
        ///    void Start()
        ///    {
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///    }
        ///
        ///    public void Update()
        ///    {
        ///        //Press the space key to switch the collision detection mode
        ///        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        ///            SwitchCollisionDetectionMode();
        ///    }
        ///
        ///    //Switch between the different Collision Detection Modes
        ///    void SwitchCollisionDetectionMode()
        ///    {
        ///        switch (m_Rigidbody.collisionDetectionMode)
        ///        {
        ///            //If the current mode is continuous, switch it to continuous dynamic mode
        ///            case CollisionDetectionMode.Continuous:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ///                break;
        ///            //If the current mode is continuous dynamic, switch it to continuous speculative
        ///            case CollisionDetectionMode.ContinuousDynamic:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ///                break;
        ///
        ///            // If the curren mode is continuous speculative, switch it to discrete mode
        ///            case CollisionDetectionMode.ContinuousSpeculative:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        ///                break;
        ///
        ///            //If the current mode is discrete, switch it to continuous mode
        ///            case CollisionDetectionMode.Discrete:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        ///                break;
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ContinuousDynamic = 2,
        ///<summary>Speculative continuous collision detection is on for static and dynamic geometries</summary>
        ///<remarks>This is a collision detection mode that can be used on both dynamic and kinematic objects. It is generally cheaper than other CCD mode. It also handles angular motion much better. However, in some cases, high speed objects may still tunneling through other geometries.</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// //This script allows you to switch collision detection mode at the press of the space key, and move your GameObject. It also outputs collisions that occur to the console.
        /// //Attach this script to a GameObject and make sure it has a Rigidbody component
        /// //If it doesn't have a Rigidbody component, click the GameObject, go to its Inspector and click the __Add Component__ button. Then, go to __Physics__>__Rigidbody__.
        /// //Create another GameObject. Make sure it has a Collider, so you can test collisions between them.
        ///
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///                        
        ///public class Example : MonoBehaviour
        ///{
        ///    Rigidbody m_Rigidbody;
        ///
        ///    void Start()
        ///    {
        ///        //Attach this script to a GameObject. Ensure that it has a Rigidbody component.
        ///        m_Rigidbody = GetComponent<Rigidbody>();
        ///    }
        ///
        ///    void Update()
        ///    {
        ///        //Press the space key to switch the collision detection mode
        ///        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        ///            SwitchCollisionDetectionMode();
        ///    }
        ///
        ///    //Switch between the different Collision Detection Modes
        ///    void SwitchCollisionDetectionMode()
        ///    {
        ///        switch (m_Rigidbody.collisionDetectionMode)
        ///        {
        ///            //If the current mode is continuous, switch it to continuous dynamic mode
        ///            case CollisionDetectionMode.Continuous:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ///                break;
        ///            //If the current mode is continuous dynamic, switch it to continuous speculative
        ///            case CollisionDetectionMode.ContinuousDynamic:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ///                break;
        ///
        ///            // If the current mode is continuous speculative, switch it to discrete mode
        ///            case CollisionDetectionMode.ContinuousSpeculative:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        ///                break;
        ///
        ///            //If the current mode is discrete, switch it to continuous mode
        ///            case CollisionDetectionMode.Discrete:
        ///                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        ///                break;
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ContinuousSpeculative = 3
    }
}
