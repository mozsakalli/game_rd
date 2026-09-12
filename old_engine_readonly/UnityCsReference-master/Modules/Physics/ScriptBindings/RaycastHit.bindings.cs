// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Structure used to get information back from a raycast.</summary>
    ///<seealso cref="Physics.Raycast" />
    ///<seealso cref="Physics.Linecast" />
    ///<seealso cref="Physics.RaycastAll" />
    [NativeHeader("Runtime/Interfaces/IPhysics.h")]
    [NativeHeader("PhysicsScriptingClasses.h")]
    [NativeHeader("Modules/Physics/RaycastHit.h")]
    [UsedByNativeCode]
    public partial struct RaycastHit
    {
        [NativeName("point")] internal Vector3 m_Point;
        [NativeName("normal")] internal Vector3 m_Normal;
        [NativeName("faceID")] internal uint m_FaceID;
        [NativeName("distance")] internal float m_Distance;
        [NativeName("uv")] internal Vector2 m_UV;
        [NativeName("collider")] internal EntityId m_Collider;

        ///<summary>The <see cref="Collider" /> that was hit.</summary>
        ///<remarks>
        ///  <para>This property is null if the ray hit nothing and not-null if it hit a Collider.</para>
        ///  <para />
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    void Update()
        ///    {
        ///        if (Mouse.current.leftButton.wasPressedThisFrame)
        ///        {
        ///            RaycastHit hit;
        ///            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        ///
        ///            if (Physics.Raycast(ray, out hit))
        ///            {
        ///                if (hit.collider != null)
        ///                {
        ///                    hit.collider.enabled = false;
        ///                }
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public Collider collider { get { return Object.FindObjectFromInstanceID(m_Collider) as Collider; } }


        ///<summary>Instance ID of the <see cref="Collider" /> that was hit.</summary>
        ///<remarks>Provides a reference to the collider that was hit in a way that is accessible from jobs.
        ///For more information on creating jobs see [Create Jobs](xref:JobSystemCreatingJobs).</remarks>
        [System.Obsolete("RaycastHit.colliderInstanceID is obsolete. Use RaycastHit.colliderEntityId instead.", true)]
        public int colliderInstanceID { get { return m_Collider; } }
        ///<summary>EntityId of the <see cref="Collider" /> that was hit.</summary>
        ///<remarks>Provides a reference to the collider that was hit in a way that is accessible from jobs.
        ///For more information on creating jobs see [Create Jobs](xref:JobSystemCreatingJobs).</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using Unity.Collections;
        ///using Unity.Jobs;
        ///using UnityEngine;
        ///
        ///public class BatchExample : MonoBehaviour
        ///{
        ///    public struct CollisionJob : IJob
        ///    {
        ///        public EntityId colliderID;
        ///        public NativeArray<RaycastHit> results;
        ///
        ///        public void Execute()
        ///        {
        ///            // This is where we check what we collided with and do any appropriate actions
        ///            // If you tried accessing RaycastHit.collider you would get an error
        ///            if (colliderID == results[0].colliderEntityId)
        ///                Debug.Log("Detected the a hit with the requested collider");
        ///        }
        ///    }
        ///    void Start()
        ///    {
        ///        // We create the raycast command buffer and an array to store the RaycastHits
        ///        NativeArray<RaycastCommand> commands = new NativeArray<RaycastCommand>(1, Allocator.TempJob);
        ///        NativeArray<RaycastHit> results = new NativeArray<RaycastHit>(1, Allocator.TempJob);
        ///
        ///        var boxCollider = new GameObject().AddComponent<BoxCollider>();
        ///
        ///        // Create a new command for the buffer, pointing at the collider we created
        ///        commands[0] = new RaycastCommand(Vector3.up * 2, Vector3.down);
        ///
        ///        // Schedule the commands in the buffer and store results in the 'results' array
        ///        var batchHandle = RaycastCommand.ScheduleBatch(commands, results, 1, 1);
        ///
        ///        // This job is for doing something on the other thread when the collider of interest was hit
        ///        var job = new CollisionJob();
        ///        job.colliderID = boxCollider.GetEntityId();
        ///        job.results = results;
        ///
        ///        //Schedule the job to start after batchHandle has finished
        ///        var jobHandle = job.Schedule(batchHandle);
        ///        jobHandle.Complete();
        ///
        ///        commands.Dispose();
        ///        results.Dispose();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public EntityId colliderEntityId { get { return m_Collider; } }

        ///<summary>The impact point in world space where the ray hit the collider.</summary>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Apply a force to a rigidbody in the Scene at the point
        ///    // where it is clicked.
        ///
        ///    // The force with which the target is "poked" when hit.
        ///    float pokeForce;
        ///
        ///    void Update()
        ///    {
        ///        if (Mouse.current.leftButton.wasPressedThisFrame)
        ///        {
        ///            RaycastHit hit;
        ///            var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        ///
        ///            if (Physics.Raycast(ray, out hit))
        ///            {
        ///                if (hit.rigidbody != null)
        ///                {
        ///                    hit.rigidbody.AddForceAtPosition(ray.direction * pokeForce, hit.point);
        ///                }
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        public Vector3 point { get { return m_Point; } set { m_Point = value; } }
        ///<summary>The normal of the surface the ray hit.</summary>
        ///<remarks>
        ///  <para>The normal of the surface the ray hit on the collider where the ray intersects, which may or may not match the original mesh surface depending on the collider type and settings.
        ///                    
        ///                    For primitive colliders such as BoxCollider or SphereCollider, the normal is calculated based on their simple geometric shape. For MeshCollider, if convex is set to false (non-convex), Unity can return the actual interpolated normal from the mesh surface at the hit point. If convex is true, the normal is instead approximated from the convex hull of the mesh.</para>
        ///  <para />
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Calculate the reflection of a "laser beam" off a clicked object.
        ///
        ///    // The object from which the beam is fired. The incoming beam will
        ///    // not be visible if the camera is used for this!
        ///    Transform gunObj;
        ///
        ///    void Start()
        ///    {
        ///        gunObj = this.GetComponent<Transform>();
        ///    }
        ///    
        ///    void Update()
        ///    {
        ///        if (Mouse.current.leftButton.isPressed)
        ///        {
        ///            RaycastHit hit;
        ///            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        ///
        ///            if (Physics.Raycast(ray, out hit))
        ///            {
        ///                // Find the line from the gun to the point that was clicked.
        ///                Vector3 incomingVec = hit.point - gunObj.position;
        ///
        ///                // Use the point's normal to calculate the reflection vector.
        ///                Vector3 reflectVec = Vector3.Reflect(incomingVec, hit.normal);
        ///
        ///                // Draw lines to show the incoming "beam" and the reflection.
        ///                Debug.DrawLine(gunObj.position, hit.point, Color.red);
        ///                Debug.DrawRay(hit.point, reflectVec, Color.green);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public Vector3 normal { get { return m_Normal; } set { m_Normal = value; } }
        ///<summary>The barycentric coordinate of the triangle we hit.</summary>
        ///<remarks>This lets you interpolate any of the vertex data along the 3 axes.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Attach this script to a camera and it will
        ///    // draw a debug line pointing outward from the normal
        ///    void Update()
        ///    {
        ///        // Only if we hit something, do we continue
        ///        RaycastHit hit;
        ///
        ///
        ///        if (!Physics.Raycast(Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue()), out hit))
        ///        {
        ///            return;
        ///        }
        ///
        ///        // Just in case, also make sure the collider also has a renderer
        ///        // material and texture
        ///        MeshCollider meshCollider = hit.collider as MeshCollider;
        ///        if (meshCollider == null || meshCollider.sharedMesh == null)
        ///        {
        ///            return;
        ///        }
        ///
        ///        Mesh mesh = meshCollider.sharedMesh;
        ///        Vector3[] normals = mesh.normals;
        ///        int[] triangles = mesh.triangles;
        ///
        ///        // Extract local space normals of the triangle we hit
        ///        Vector3 n0 = normals[triangles[hit.triangleIndex * 3 + 0]];
        ///        Vector3 n1 = normals[triangles[hit.triangleIndex * 3 + 1]];
        ///        Vector3 n2 = normals[triangles[hit.triangleIndex * 3 + 2]];
        ///
        ///        // interpolate using the barycentric coordinate of the hitpoint
        ///        Vector3 baryCenter = hit.barycentricCoordinate;
        ///
        ///        // Use barycentric coordinate to interpolate normal
        ///        Vector3 interpolatedNormal = n0 * baryCenter.x + n1 * baryCenter.y + n2 * baryCenter.z;
        ///        // normalize the interpolated normal
        ///        interpolatedNormal = interpolatedNormal.normalized;
        ///
        ///        // Transform local space normals to world space
        ///        Transform hitTransform = hit.collider.transform;
        ///        interpolatedNormal = hitTransform.TransformDirection(interpolatedNormal);
        ///
        ///        // Display with Debug.DrawLine
        ///        Debug.DrawRay(hit.point, interpolatedNormal);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public Vector3 barycentricCoordinate { get { return new Vector3(1.0F - (m_UV.y + m_UV.x), m_UV.x, m_UV.y); } set { m_UV = value; } }
        ///<summary>The distance from the ray's origin to the impact point.</summary>
        ///<remarks>
        ///  <para>In the case of a ray, the distance represents the magnitude of the vector from the ray's origin to the impact point.
        ///
        ///In the case of a swept volume or sphere cast, the distance represents the magnitude of the vector from the origin point to the translated point at which the volume contacts the other collider.
        ///
        ///Note that <see cref="RaycastHit.point" /> represents the point in space where the collision occurs.</para>
        ///  <para />
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Movable, levitating object.
        ///
        ///    // This works by measuring the distance to ground with a
        ///    // raycast then applying a force that decreases as the object
        ///    // reaches the desired levitation height.
        ///
        ///    // Vary the parameters below to
        ///    // get different control effects. For example, reducing the
        ///    // hover damping will tend to make the object bounce if it
        ///    // passes over an object underneath.
        ///
        ///    // Forward movement force.
        ///    float moveForce = 1.0f;
        ///
        ///    // Torque for left/right rotation.
        ///    float rotateTorque = 1.0f;
        ///
        ///    // Desired hovering height.
        ///    float hoverHeight = 4.0f;
        ///
        ///    // The force applied per unit of distance below the desired height.
        ///    float hoverForce = 5.0f;
        ///
        ///    // The amount that the lifting force is reduced per unit of upward speed.
        ///    // This damping tends to stop the object from bouncing after passing over
        ///    // something.
        ///    float hoverDamp = 0.5f;
        ///
        ///    // Rigidbody component.
        ///    Rigidbody rb;
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
        ///        rb = GetComponent<Rigidbody>();
        ///
        ///        // Fairly high drag makes the object easier to control.
        ///        rb.linearDamping = 0.5f;
        ///        rb.angularDamping = 0.5f;
        ///    }
        ///
        ///    void FixedUpdate()
        ///    {
        ///        // Push/turn the object based on arrow key input.
        ///        rb.AddForce(moveAction.action.ReadValue<Vector2>().y * moveForce * transform.forward);
        ///        rb.AddTorque(moveAction.action.ReadValue<Vector2>().x * rotateTorque * Vector3.up);
        ///
        ///        RaycastHit hit;
        ///        Ray downRay = new Ray(transform.position, -Vector3.up);
        ///
        ///        // Cast a ray straight downwards.
        ///        if (Physics.Raycast(downRay, out hit))
        ///        {
        ///            // The "error" in height is the difference between the desired height
        ///            // and the height measured by the raycast distance.
        ///            float hoverError = hoverHeight - hit.distance;
        ///
        ///            // Only apply a lifting force if the object is too low (ie, let
        ///            // gravity pull it downward if it is too high).
        ///            if (hoverError > 0)
        ///            {
        ///                // Subtract the damping from the lifting force and apply it to
        ///                // the rigidbody.
        ///                float upwardSpeed = rb.linearVelocity.y;
        ///                float lift = hoverError * hoverForce - upwardSpeed * hoverDamp;
        ///                rb.AddForce(lift * Vector3.up);
        ///            }
        ///        }
        ///        Debug.DrawRay(transform.position, -Vector3.up * hoverHeight, Color.green);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public float distance { get { return m_Distance; } set { m_Distance = value; } }
        ///<summary>The index of the triangle that was hit.</summary>
        ///<remarks>
        ///  <para>Triangle index is only valid if the collider that was hit is a <see cref="MeshCollider" />.</para>
        ///  <para />
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// // This script draws a debug line around mesh triangles
        /// // as you move the mouse over them.
        ///using UnityEngine;
        ///using System.Collections;
        ///using UnityEngine.InputSystem;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    Camera cam;
        ///
        ///    void Start()
        ///    {
        ///        cam = GetComponent<Camera>();
        ///    }
        ///
        ///    void Update()
        ///    {
        ///        RaycastHit hit;
        ///        if (!Physics.Raycast(cam.ScreenPointToRay(Mouse.current.position.ReadValue()), out hit))
        ///            return;
        ///
        ///        MeshCollider meshCollider = hit.collider as MeshCollider;
        ///        if (meshCollider == null || meshCollider.sharedMesh == null)
        ///            return;
        ///
        ///        Mesh mesh = meshCollider.sharedMesh;
        ///        Vector3[] vertices = mesh.vertices;
        ///        int[] triangles = mesh.triangles;
        ///        Vector3 p0 = vertices[triangles[hit.triangleIndex * 3 + 0]];
        ///        Vector3 p1 = vertices[triangles[hit.triangleIndex * 3 + 1]];
        ///        Vector3 p2 = vertices[triangles[hit.triangleIndex * 3 + 2]];
        ///        Transform hitTransform = hit.collider.transform;
        ///        p0 = hitTransform.TransformPoint(p0);
        ///        p1 = hitTransform.TransformPoint(p1);
        ///        p2 = hitTransform.TransformPoint(p2);
        ///        Debug.DrawLine(p0, p1);
        ///        Debug.DrawLine(p1, p2);
        ///        Debug.DrawLine(p2, p0);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public int triangleIndex { get { return (int)m_FaceID; } }

        [NativeMethod("CalculateRaycastTexCoord", true, true)]
        extern static private Vector2 CalculateRaycastTexCoord(EntityId colliderInstanceID, Vector2 uv, Vector3 pos, uint face, int textcoord);

        ///<summary>The uv texture coordinate at the collision location.</summary>
        ///<remarks>
        ///  <para>A ray is fired into the Scene.  The <c>textureCoord</c> is the location where the ray
        ///                    has hit a collider.  /RaycastHit._textureCoord/ is a texture coordinate when
        ///                    a hit occurs.  A <see cref="Vector2" /> zero is returned if no mesh collider is present in
        ///                    the <c>GameObject</c>. This property can be accessed off the main thread.
        ///                    
        ///                    **Note:** A <see cref="textureCoord" /> requires the collider to be a <see cref="MeshCollider" />.
        ///                    
        ///                    **Note:** In builds, <see cref="textureCoord" /> will return (0, 0) unless the mesh’s import settings have Read/Write Enabled checked.</para>
        ///  <para />
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[
        /// // Write black pixels onto the GameObject that is located
        /// // by the script. The script is attached to the camera.
        /// // Determine where the collider hits and modify the texture at that point.
        /// //
        /// // Note that the MeshCollider on the GameObject must have Convex turned off. This allows
        /// // concave GameObjects to be included in collision in this example.
        /// //
        /// // Also to allow the texture to be updated by mouse button clicks it must have the Read/Write
        /// // Enabled option set to true in its Advanced import settings.
        ///
        ///using UnityEngine;
        ///using System.Collections;
        ///using UnityEngine.InputSystem;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public Camera cam;
        ///
        ///    void Start()
        ///    {
        ///        cam = GetComponent<Camera>();
        ///    }
        ///
        ///    void Update()
        ///    {
        ///        if (!Mouse.current.leftButton.isPressed)
        ///            return;
        ///
        ///        RaycastHit hit;
        ///        if (!Physics.Raycast(cam.ScreenPointToRay(Mouse.current.position.ReadValue()), out hit))
        ///            return;
        ///
        ///        Renderer rend = hit.transform.GetComponent<Renderer>();
        ///        MeshCollider meshCollider = hit.collider as MeshCollider;
        ///
        ///        if (rend == null || rend.sharedMaterial == null || rend.sharedMaterial.mainTexture == null || meshCollider == null)
        ///            return;
        ///
        ///        Texture2D tex = rend.material.mainTexture as Texture2D;
        ///        Vector2 pixelUV = hit.textureCoord;
        ///        pixelUV.x *= tex.width;
        ///        pixelUV.y *= tex.height;
        ///
        ///        tex.SetPixel((int)pixelUV.x, (int)pixelUV.y, Color.black);
        ///        tex.Apply();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public Vector2 textureCoord { get { return CalculateRaycastTexCoord(m_Collider, m_UV, m_Point, m_FaceID, 0); } }
        ///<summary>The secondary uv texture coordinate at the impact point.</summary>
        ///<remarks>This can be used for 3D texture painting or drawing bullet marks.
        ///If the collider is not a mesh collider, <see cref="Vector2.zero" /> will be returned.
        ///If the mesh contains no secondary uv set, the uv of the primary uv set will be returned. This property can be accessed off the main thread.
        ///
        ///**Note:** A <see cref="textureCoord2" /> requires the collider to be a <see cref="MeshCollider" />.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Attach this script to a camera and it will paint black pixels in 3D
        ///    // on whatever the user clicks. Make sure the mesh you want to paint
        ///    // on has a mesh collider attached.
        ///
        ///    Camera cam;
        ///
        ///    void Start()
        ///    {
        ///        cam = GetComponent<Camera>();
        ///    }
        ///
        ///    void Update()
        ///    {
        ///        // Only when we press the mouse
        ///        if (!Mouse.current.leftButton.isPressed)
        ///        {
        ///            return;
        ///        }
        ///
        ///        // Only if we hit something, do we continue
        ///        RaycastHit hit;
        ///        if (!Physics.Raycast(cam.ScreenPointToRay(Mouse.current.position.ReadValue()), out hit))
        ///        {
        ///            return;
        ///        }
        ///
        ///        // Just in case, also make sure the collider also has a renderer
        ///        // material and texture. Also we should ignore primitive colliders.
        ///        Renderer rend = hit.transform.GetComponent<Renderer>();
        ///
        ///        MeshCollider meshCollider = hit.collider as MeshCollider;
        ///
        ///        if (rend == null || rend.sharedMaterial == null ||
        ///            rend.sharedMaterial.mainTexture == null || meshCollider == null)
        ///        {
        ///            return;
        ///        }
        ///
        ///        // Now draw a pixel where we hit the object
        ///        Texture2D tex = rend.material.mainTexture as Texture2D;
        ///        Vector2 pixelUV = hit.textureCoord2;
        ///        pixelUV.x *= tex.width;
        ///        pixelUV.y *= tex.height;
        ///
        ///        tex.SetPixel(Mathf.FloorToInt(pixelUV.x), Mathf.FloorToInt(pixelUV.y), Color.black);
        ///
        ///        tex.Apply();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public Vector2 textureCoord2 { get { return CalculateRaycastTexCoord(m_Collider, m_UV, m_Point, m_FaceID, 1); } }

        ///<summary>The <see cref="Transform" /> of the rigidbody or collider that was hit.</summary>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public Transform transform
        {
            get
            {
                Rigidbody body = rigidbody;
                if (body != null)
                    return body.transform;
                else if (collider != null)
                    return collider.transform;
                else
                    return null;
            }
        }

        ///<summary>The <see cref="Rigidbody" /> of the collider that was hit. If the collider is not attached to a rigidbody then it is <c>null</c>.</summary>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Apply a force to a clicked rigidbody object.
        ///
        ///    // The force applied to an object when hit.
        ///    float hitForce;
        ///
        ///    void Update()
        ///    {
        ///        if (Mouse.current.leftButton.wasPressedThisFrame)
        ///        {
        ///            RaycastHit hit;
        ///            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        ///
        ///            if (Physics.Raycast(ray, out hit))
        ///            {
        ///                if (hit.rigidbody != null)
        ///                {
        ///                    hit.rigidbody.AddForce(ray.direction * hitForce);
        ///                }
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public Rigidbody rigidbody { get { return collider != null ? collider.attachedRigidbody : null; } }
        ///<summary>The <see cref="ArticulationBody" /> of the collider that was hit. If the collider is not attached to an articulation body then it is <c>null</c>.</summary>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.InputSystem;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Apply a force to a clicked articulationBody object.
        ///
        ///    // The force applied to an object when hit.
        ///    float hitForce;
        ///
        ///    void Update()
        ///    {
        ///        if (Mouse.current.leftButton.wasPressedThisFrame)
        ///        {
        ///            RaycastHit hit;
        ///            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        ///
        ///            if (Physics.Raycast(ray, out hit))
        ///            {
        ///                if (hit.articulationBody != null)
        ///                {
        ///                    hit.articulationBody.AddForce(ray.direction * hitForce);
        ///                }
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Physics.Raycast" />
        ///<seealso cref="Physics.Linecast" />
        ///<seealso cref="Physics.RaycastAll" />
        public ArticulationBody articulationBody { get { return collider != null ? collider.attachedArticulationBody : null; } }

        ///<summary>The uv lightmap coordinate at the impact point.</summary>
        ///<remarks>This can be used for sampling the lightmap and setting the sampled color value as the material color of a moving object
        ///to make it roughly match the baked lighting.</remarks>
        public Vector2 lightmapCoord
        {
            get
            {
                Vector2 coord = CalculateRaycastTexCoord(m_Collider, m_UV, m_Point, m_FaceID, 1);
                if (collider.GetComponent<Renderer>() != null)
                {
                    Vector4 st = collider.GetComponent<Renderer>().lightmapScaleOffset;
                    coord.x = coord.x * st.x + st.z;
                    coord.y = coord.y * st.y + st.w;
                }
                return coord;
            }
        }
    }
}
