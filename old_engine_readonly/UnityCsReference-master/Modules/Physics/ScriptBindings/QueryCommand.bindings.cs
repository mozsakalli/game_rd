// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Internal;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace UnityEngine
{
    ///<summary>Creates a struct to set up parameters for batch queries: <see cref="RaycastCommand" />, <see cref="BoxcastCommand" />, <see cref="CapsulecastCommand" />, <see cref="SpherecastCommand" />.</summary>
    ///<remarks>Use this struct to configure hit flags and layer mask. This supports hit triggers, hit backfaces and hit multiple Mesh faces.
    ///
    ///Note: Only RaycastCommand supports hitting multiple Mesh faces.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct QueryParameters
    {
        ///<summary>A <see cref="LayerMask" /> that is used to selectively filter which colliders are considered when casting a ray.</summary>
        public int layerMask;
        ///<summary>Whether raycast batch query should hit multiple faces.</summary>
        ///<remarks>Can be used in conjuction with <see cref="QueryParameters.hitBackfaces" /> to get backface hits as well.</remarks>
        public bool hitMultipleFaces;
        ///<summary>Whether queries hit Triggers by default.</summary>
        public QueryTriggerInteraction hitTriggers;
        ///<summary>Whether physics queries should hit back-face triangles.</summary>
        public bool hitBackfaces;

        ///<summary>Struct used to set up parameters for queries: <see cref="RaycastCommand" />, <see cref="BoxcastCommand" />, <see cref="CapsulecastCommand" />, <see cref="SpherecastCommand" />.</summary>
        ///<param name="layerMask">A <see cref="LayerMask" /> that is used to selectively filter which colliders are considered when casting a ray.</param>
        ///<param name="hitMultipleFaces">Whether raycast batch query should hit multiple faces.</param>
        ///<param name="hitTriggers">Whether queries hit Triggers by default.</param>
        ///<param name="hitBackfaces">Whether physics queries should hit back-face triangles.</param>
        public QueryParameters(int layerMask = Physics.DefaultRaycastLayers, bool hitMultipleFaces = false, QueryTriggerInteraction hitTriggers = QueryTriggerInteraction.UseGlobal, bool hitBackfaces = false)
        {
            this.layerMask = layerMask;
            this.hitMultipleFaces = hitMultipleFaces;
            this.hitTriggers = hitTriggers;
            this.hitBackfaces = hitBackfaces;
        }

        ///<summary>Create a default QueryParameters struct.</summary>
        ///<remarks>
        ///  <see cref="LayerMask" /> is set to default raycasting mask, hitMultipleFaces and hitBackfaces are set to false, hitTriggers is set to use global parameter <see cref="Physics.queriesHitTriggers" />.</remarks>
        public static QueryParameters Default => new QueryParameters(Physics.DefaultRaycastLayers, false, QueryTriggerInteraction.UseGlobal, false);
    }

    ///<summary>Struct used to retrieve information from an Overlap batch query.</summary>
    ///<seealso cref="OverlapBoxCommand" />
    ///<seealso cref="OverlapCapsuleCommand" />
    ///<seealso cref="OverlapSphereCommand" />
    [StructLayout(LayoutKind.Sequential)]
    public struct ColliderHit
    {
        private EntityId m_ColliderEntityId;

        ///<summary>The EntityId of the Collider that was hit.</summary>
        public EntityId entityId => m_ColliderEntityId;

        ///<summary>Obsolete. Use entityId instead.</summary>
        ///<remarks>The instance ID of the Collider that was hit.</remarks>
        [System.Obsolete("instanceID is deprecated, use entityId instead.", true)]
        public int instanceID => m_ColliderEntityId;

        // note this is a main-thread only API
        ///<summary>The Collider that was hit.</summary>
        ///<remarks>Can only be called from the main thread.</remarks>
        public Collider collider => Object.FindObjectFromInstanceID(entityId) as Collider;
    }

    ///<summary>Struct used to set up a raycast command to be performed asynchronously during a job.</summary>
    ///<remarks>When you use this struct to schedule a batch of raycasts, they will be performed asynchronously and in parallel to each other. The results of the raycasts are written to the results buffer. Since the results are written asynchronously the results buffer cannot be accessed until the job has been completed.
    ///
    ///The results for a command at index N in the command buffer are stored at index N * maxHits in the results buffer.
    ///
    ///If maxHits is larger than the actual number of results for the command the result buffer will contain some invalid results which did not hit anything. The first invalid result is identified by the collider being null. The second and later invalid results are not written to by the raycast command so their colliders are not guaranteed to be null. When iterating over the results the loop should stop when the first invalid result is found.
    ///
    ///Raycast command also controls whether or not Trigger colliders and back-face triangles generate a hit. If hitMultipleFaces is set to true, Raycast command returns multiple hits per Mesh. You should adjust maxHits and result array size accordingly to store all hits. For solid objects (Sphere, Capsule, Box, Convex), this returns a maximum of one result. Use <see cref="QueryParameters" /> to control hit flags.
    ///
    ///Note: Only BatchQuery.ExecuteRaycastJob is logged into the profiler. Query count information is not logged.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using Unity.Jobs;
    ///using UnityEngine;
    ///
    ///public class RaycastExample : MonoBehaviour
    ///{
    ///    [SerializeField] private int rayCount = 5;
    ///    [SerializeField] private float spacing = 2.0f;
    ///    [SerializeField] private float rayLength = 100f;
    ///
    ///    private void Start()
    ///    {
    ///        // Allocate raycast commands and results
    ///        var commands = new NativeArray<RaycastCommand>(rayCount, Allocator.TempJob);
    ///        var results = new NativeArray<RaycastHit>(rayCount, Allocator.TempJob);
    ///
    ///        // Set up raycast commands
    ///        for (int i = 0; i < rayCount; i++)
    ///        {
    ///            Vector3 origin = new Vector3(i * spacing, 0, -10);
    ///            Vector3 direction = Vector3.forward;
    ///            commands[i] = new RaycastCommand(origin, direction, QueryParameters.Default);
    ///        }
    ///
    ///        // Schedule and complete batch
    ///        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 1, default);
    ///        handle.Complete();
    ///
    ///        // Process results
    ///        for (int i = 0; i < rayCount; i++)
    ///        {
    ///            if (results[i].collider != null)
    ///            {
    ///                Debug.DrawLine(commands[i].from, results[i].point, Color.green, 5f);
    ///                Debug.Log($"Ray {i} hit {results[i].collider.name} at {results[i].point}");
    ///            }
    ///            else
    ///            {
    ///                Debug.DrawRay(commands[i].from, commands[i].direction * rayLength, Color.red, 5f);
    ///                Debug.Log($"Ray {i} missed.");
    ///            }
    ///        }
    ///
    ///        // Dispose memory
    ///        results.Dispose();
    ///        commands.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.Raycast" />
    ///<seealso cref="Physics.RaycastAll" />
    [NativeHeader("Modules/Physics/BatchCommands/RaycastCommand.h")]
    [NativeHeader("ManagedKernel/Jobs/ScriptBindings/JobsBindingsTypes.h")]
    public partial struct RaycastCommand
    {
        ///<summary>Create a RaycastCommand.</summary>
        ///<remarks>The query is run in the default physics scene.</remarks>
        ///<param name="from">The starting point of the ray in world coordinates.</param>
        ///<param name="direction">The direction of the ray.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit multiple mesh faces, hit triggers and hit backfaces.</param>
        ///<param name="distance">The maximum distance the ray should check for collisions.</param>
        public RaycastCommand(Vector3 from, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.from = from;
            this.direction = direction;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.distance = distance;
            this.queryParameters = queryParameters;

        }
        ///<summary>Create a RaycastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the raycast query in.</param>
        ///<param name="from">The starting point of the ray in world coordinates.</param>
        ///<param name="direction">The direction of the ray.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit multiple mesh faces, hit triggers and hit backfaces.</param>
        ///<param name="distance">The maximum distance the ray should check for collisions.</param>
        public RaycastCommand(PhysicsScene physicsScene, Vector3 from, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.from = from;
            this.direction = direction;
            this.physicsScene = physicsScene;
            this.distance = distance;
            this.queryParameters = queryParameters;
        }

        ///<summary>The starting point of the ray in world coordinates.</summary>
        public Vector3 from { get; set; }
        ///<summary>The direction of the ray.</summary>
        public Vector3 direction {get; set; }
        ///<summary>The physics scene this command is run in.</summary>
        public PhysicsScene physicsScene { get; set; }
        ///<summary>The maximum distance the ray should check for collisions.</summary>
        public float distance { get; set; }
        ///<summary>Structure for specifying additional parameters for a batch query such as layer mask, hit multiple mesh faces, hit triggers and hit backfaces.</summary>
        public QueryParameters queryParameters;

        ///<summary>Schedule a batch of raycasts to perform in a job.</summary>
        ///<param name="commands">A NativeArray of the RaycastCommands to perform.</param>
        ///<param name="results">A NativeArray of the RaycastHits where the results of the commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="dependsOn">A JobHandle of a job which must be completed before the raycast starts.</param>
        ///<param name="maxHits">The maximum number of Colliders the ray can hit.</param>
        ///<returns>The JobHandle of the job which will perform the raycasts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = new JobHandle())
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return new JobHandle();
            }
            else if (results.Length < maxHits * commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return new JobHandle();
            }

            var jobData = new BatchQueryJob<RaycastCommand, RaycastHit>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<RaycastCommand, RaycastHit>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleRaycastBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob, maxHits);
        }

        ///<summary>Schedule a batch of raycasts to perform in a job.</summary>
        ///<remarks>By default maxHits in this variant is set to 1.</remarks>
        ///<param name="commands">A NativeArray of the RaycastCommands to perform.</param>
        ///<param name="results">A NativeArray of the RaycastHits where the results of the commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="dependsOn">A JobHandle of a job which must be completed before the raycast starts.</param>
        ///<returns>The JobHandle of the job which will perform the raycasts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = new JobHandle())
        {
            return ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
        }

        [FreeFunction("ScheduleRaycastCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleRaycastBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob, int maxHits);

    }

    ///<summary>Use this struct to set up a sphere cast command that is performed asynchronously during a job.</summary>
    ///<remarks>When you use this struct to schedule a batch of sphere casts, the sphere casts are performed asynchronously and in parallel. The results of each sphere casts are written to the results buffer. Since the results are written asynchronously, you cannot access the results buffer until the job is completed.
    ///
    ///The results for a command at index N in the command buffer are stored at index N * maxHits in the results buffer.
    ///
    ///Spherecast command also allows you to control whether or not Trigger colliders and back-face triangles generate a hit. Use <see cref="QueryParameters" /> to control hit flags.
    ///
    ///Note: Only BatchQuery.ExecuteSpherecastJob is logged into the profiler. Query count information is not logged.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using Unity.Jobs;
    ///using UnityEngine;
    ///
    ///public class SpherecastExample : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        // Perform a single sphere cast using SpherecastCommand and wait for it to complete
    ///        // Set up the command and result buffers
    ///        var results = new NativeArray<RaycastHit>(2, Allocator.TempJob);
    ///        var commands = new NativeArray<SpherecastCommand>(1, Allocator.TempJob);
    ///
    ///        // Set the data of the first command
    ///        Vector3 origin = Vector3.forward * -10;
    ///        Vector3 direction = Vector3.forward;
    ///        float radius = 0.5f;
    ///
    ///        commands[0] = new SpherecastCommand(origin, radius, direction, QueryParameters.Default);
    ///
    ///        // Schedule the batch of sphere casts
    ///        var handle = SpherecastCommand.ScheduleBatch(commands, results, 1, 2, default(JobHandle));
    ///
    ///        // Wait for the batch processing job to complete
    ///        handle.Complete();
    ///
    ///        // If batchedHit.collider is not null there was a hit
    ///        foreach (var hit in results)
    ///        {
    ///            if (hit.collider != null)
    ///            {
    ///                // Do something with results
    ///            }
    ///        }
    ///
    ///        // Dispose the buffers
    ///        results.Dispose();
    ///        commands.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.SphereCast" />
    [NativeHeader("Modules/Physics/BatchCommands/SpherecastCommand.h")]
    [NativeHeader("ManagedKernel/Jobs/ScriptBindings/JobsBindingsTypes.h")]
    public partial struct SpherecastCommand
    {
        ///<summary>Creates a SpherecastCommand.</summary>
        ///<remarks>The command is run in the default physics scene.</remarks>
        ///<param name="origin">The starting point of the sphere cast.</param>
        ///<param name="radius">The radius of the casting sphere.</param>
        ///<param name="direction">The direction of the sphere cast.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</param>
        ///<param name="distance">The maximum distance the cast should check for collisions.</param>
        public SpherecastCommand(Vector3 origin, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.origin = origin;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.queryParameters = queryParameters;
        }

        ///<summary>Creates a SpherecastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the command in.</param>
        ///<param name="origin">The starting point of the sphere cast.</param>
        ///<param name="radius">The radius of the casting sphere.</param>
        ///<param name="direction">The direction of the sphere cast.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</param>
        ///<param name="distance">The maximum distance the cast should check for collisions.</param>
        public SpherecastCommand(PhysicsScene physicsScene, Vector3 origin, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.origin = origin;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = physicsScene;
            this.queryParameters = queryParameters;
        }

        ///<summary>The starting point of the sphere cast in world coordinates.</summary>
        public Vector3 origin { get; set; }
        ///<summary>The radius of the casting sphere.</summary>
        public float radius { get; set; }
        ///<summary>The direction of the sphere cast.</summary>
        public Vector3 direction { get; set; }
        ///<summary>The maximum distance the sphere should check for collisions.</summary>
        public float distance { get; set; }
        ///<summary>The physics scene this command is run in.</summary>
        public PhysicsScene physicsScene { get; set; }
        ///<summary>Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</summary>
        public QueryParameters queryParameters;

        ///<summary>Schedules a batch of sphere casts to perform in a job.</summary>
        ///<param name="commands">A NativeArray of SpherecastCommands to perform.</param>
        ///<param name="results">A NattiveArray of RaycastHit where the result of commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a job.</param>
        ///<param name="dependsOn">A JobHandle of the job that must be completed before performing the sphere casts.</param>
        ///<param name="maxHits">The maximum number of Colliders the SphereCast can hit.</param>
        ///<returns>Returns a JobHandle of the job that will perform the sphere casts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<SpherecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = new JobHandle())
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return new JobHandle();
            }
            else if (results.Length < maxHits * commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return new JobHandle();
            }

            var jobData = new BatchQueryJob<SpherecastCommand, RaycastHit>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<SpherecastCommand, RaycastHit>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleSpherecastBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob, maxHits);
        }

        ///<summary>Schedules a batch of sphere casts to perform in a job.</summary>
        ///<remarks>By default maxHits in this variant is set to 1.</remarks>
        ///<param name="commands">A NaviveArray of SpherecastCommands to perform.</param>
        ///<param name="results">A NavtiveArray of RaycastHit where the result of commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a job.</param>
        ///<param name="dependsOn">A JobHandle of the job that must be completed before performing the sphere casts.</param>
        ///<returns>Returns a JobHandle of the job that will perform the sphere casts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<SpherecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = new JobHandle())
        {
            return ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
        }

        [FreeFunction("ScheduleSpherecastCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleSpherecastBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob, int maxHits);
    }

    ///<summary>Use this struct to set up a capsule cast command that is performed asynchronously during a job.</summary>
    ///<remarks>When you use this struct to schedule a batch of capsule casts, the capsule casts are performed asynchronously and in parallel. The results of each capsule cast is written to the results buffer. Since the results are written asynchronously, you cannot access the results buffer until the job is completed.
    ///
    ///The results for a command at index N in the command buffer are stored at index N * maxHits in the results buffer.
    ///
    ///Capsulecast command also allows you to control whether or not Trigger colliders and back-face triangles generate a hit. Use <see cref="QueryParameters" /> to control hit flags.
    ///
    ///Note: Only BatchQuery.ExecuteCapsulecastJob is logged into the profiler. Query count information is not logged.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using Unity.Jobs;
    ///using UnityEngine;
    ///
    ///public class CapsulecastExample : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        // Perform a single capsule cast using CapsulecastCommand and wait for it to complete
    ///        // Set up the command and result buffers
    ///        var results = new NativeArray<RaycastHit>(2, Allocator.TempJob);
    ///        var commands = new NativeArray<CapsulecastCommand>(1, Allocator.TempJob);
    ///
    ///        // Set the data of the first command
    ///        Vector3 point1 = Vector3.up * -0.5f;
    ///        Vector3 point2 = Vector3.up * 0.5f;
    ///        Vector3 direction = Vector3.forward;
    ///        float radius = 0.5f;
    ///
    ///        commands[0] = new CapsulecastCommand(point1, point2, radius, direction, QueryParameters.Default);
    ///
    ///        // Schedule the batch of capsulecasts
    ///        var handle = CapsulecastCommand.ScheduleBatch(commands, results, 1, 2, default(JobHandle));
    ///
    ///        // Wait for the batch processing job to complete
    ///        handle.Complete();
    ///
    ///        // If batchedHit.collider is not null there was a hit
    ///        foreach (var hit in results)
    ///        {
    ///            if (hit.collider != null)
    ///            {
    ///                // Do something with results
    ///            }
    ///        }
    ///
    ///        // Dispose the buffers
    ///        results.Dispose();
    ///        commands.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.CapsuleCast" />
    [NativeHeader("Modules/Physics/BatchCommands/CapsulecastCommand.h")]
    [NativeHeader("ManagedKernel/Jobs/ScriptBindings/JobsBindingsTypes.h")]
    public partial struct CapsulecastCommand
    {
        ///<summary>Creates a CapsulecastCommand.</summary>
        ///<remarks>This command is run in the default physics scene.</remarks>
        ///<param name="p1">The center of the sphere at the <c>start</c> of the capsule.</param>
        ///<param name="p2">The center of the sphere at the <c>end</c> of the capsule.</param>
        ///<param name="radius">The radius of the capsule.</param>
        ///<param name="direction">The direction of the capsule cast.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</param>
        ///<param name="distance">The maximum length of the sweep.</param>
        public CapsulecastCommand(Vector3 p1, Vector3 p2, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.point1 = p1;
            this.point2 = p2;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.queryParameters = queryParameters;
        }

        ///<summary>Creates a CapsulecastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the command in.</param>
        ///<param name="p1">The center of the sphere at the <c>start</c> of the capsule.</param>
        ///<param name="p2">The center of the sphere at the <c>end</c> of the capsule.</param>
        ///<param name="radius">The radius of the capsule.</param>
        ///<param name="direction">The direction of the capsule cast.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</param>
        ///<param name="distance">The maximum length of the sweep.</param>
        public CapsulecastCommand(PhysicsScene physicsScene, Vector3 p1, Vector3 p2, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.point1 = p1;
            this.point2 = p2;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = physicsScene;
            this.queryParameters = queryParameters;
        }

        ///<summary>The center of the sphere at the <c>start</c> of the capsule.</summary>
        public Vector3 point1 { get; set; }
        ///<summary>The center of the sphere at the <c>end</c> of the capsule.</summary>
        public Vector3 point2 { get; set; }
        ///<summary>The radius of the capsule.</summary>
        public float radius {get; set; }
        ///<summary>The direction of the capsule cast.</summary>
        public Vector3 direction {get; set; }
        ///<summary>The maximum distance the capsule cast checks for collision.</summary>
        public float distance { get; set; }
        ///<summary>The physics scene this command is run in.</summary>
        public PhysicsScene physicsScene { get; set; }
        ///<summary>Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</summary>
        public QueryParameters queryParameters;

        ///<summary>Schedules a batch of capsule casts to perform in a job.</summary>
        ///<param name="commands">A NaviveArray of CapsulecastCommands to perform.</param>
        ///<param name="results">A NavtiveArray of RaycastHit where the result of commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="dependsOn">A jobHandle of a job that must be completed before performing capsule casts.</param>
        ///<param name="maxHits">The maximum number of Colliders the CapsuleCast can hit.</param>
        ///<returns>Returns a JobHandle of the job that will performs the capsule casts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<CapsulecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = new JobHandle())
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return new JobHandle();
            }
            else if (results.Length < maxHits * commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return new JobHandle();
            }

            var jobData = new BatchQueryJob<CapsulecastCommand, RaycastHit>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<CapsulecastCommand, RaycastHit>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleCapsulecastBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob, maxHits);
        }

        ///<summary>Schedules a batch of capsule casts to perform in a job.</summary>
        ///<remarks>By default maxHits in this variant is set to 1.</remarks>
        ///<param name="commands">A NaviveArray of CapsulecastCommands to perform.</param>
        ///<param name="results">A NavtiveArray of RaycastHit where the result of commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="dependsOn">A jobHandle of a job that must be completed before performing capsule casts.</param>
        ///<returns>Returns a JobHandle of the job that will performs the capsule casts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<CapsulecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = new JobHandle())
        {
            return ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
        }

        [FreeFunction("ScheduleCapsulecastCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleCapsulecastBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob, int maxHits);
    }

    ///<summary>Use this struct to set up a box cast command to be performed asynchronously during a job.</summary>
    ///<remarks>When you use this struct to schedule a batch of box casts, the box casts will are performed asynchronously and in parallel. The results of each box cast is written to the results buffer. Since the results are written asynchronously, you cannot accesss the results buffer until the job is completed.
    ///
    ///The results for a command at index N in the command buffer are stored at index N * maxHits in the results buffer.
    ///
    ///Boxcast command also allows you to control whether or not Trigger colliders and back-face triangles generate a hit. Use <see cref="QueryParameters" /> to control hit flags.
    ///
    ///Note: Only BatchQuery.ExecuteBoxcastJob is logged into the profiler. Query count information is not logged.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using Unity.Jobs;
    ///using UnityEngine;
    ///
    ///public class BoxcastCommandExample : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        // Perform a single boxcast using BoxcastCommand and wait for it to complete
    ///        // Set up the command and result buffers
    ///        var results = new NativeArray<RaycastHit>(2, Allocator.TempJob);
    ///        var commands = new NativeArray<BoxcastCommand>(1, Allocator.TempJob);
    ///
    ///        // Set the data of the first command
    ///        Vector3 center = Vector3.zero;
    ///        Vector2 halfExtents = Vector3.one * 0.5f;
    ///        Quaternion orientation = Quaternion.identity;
    ///        Vector3 direction = Vector3.forward;
    ///
    ///        commands[0] = new BoxcastCommand(center, halfExtents, orientation, direction, QueryParameters.Default);
    ///
    ///        // Schedule the batch of boxcasts
    ///        var handle = BoxcastCommand.ScheduleBatch(commands, results, 1, 2, default(JobHandle));
    ///
    ///        // Wait for the batch processing job to complete
    ///        handle.Complete();
    ///
    ///        // If batchedHit.collider is not null there was a hit
    ///        foreach (var hit in results)
    ///        {
    ///            if (hit.collider != null)
    ///            {
    ///                // Do something with results
    ///            }
    ///        }
    ///
    ///        // Dispose the buffers
    ///        results.Dispose();
    ///        commands.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.BoxCast" />
    [NativeHeader("Modules/Physics/BatchCommands/BoxcastCommand.h")]
    [NativeHeader("ManagedKernel/Jobs/ScriptBindings/JobsBindingsTypes.h")]
    public partial struct BoxcastCommand
    {
        ///<summary>Creates a BoxcastCommand.</summary>
        ///<remarks>This command is run in the default physics scene.</remarks>
        ///<param name="center">The center of the box.</param>
        ///<param name="halfExtents">The half size of the box in each dimension.</param>
        ///<param name="orientation">The rotation of the box.</param>
        ///<param name="direction">The direction in which to sweep the box.</param>
        ///<param name="distance">The maximum length of the cast.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</param>
        public BoxcastCommand(Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.direction = direction;
            this.distance = distance;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.queryParameters = queryParameters;
        }

        ///<summary>Creates a BoxcastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the command in.</param>
        ///<param name="center">The center of the box.</param>
        ///<param name="halfExtents">The half size of the box in each dimension.</param>
        ///<param name="orientation">The rotation of the box.</param>
        ///<param name="direction">The direction in which to sweep the box.</param>
        ///<param name="distance">The maximum length of the cast.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</param>
        public BoxcastCommand(PhysicsScene physicsScene, Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.direction = direction;
            this.distance = distance;
            this.physicsScene = physicsScene;
            this.queryParameters = queryParameters;
        }

        ///<summary>The center of the box.</summary>
        public Vector3 center { get; set; }
        ///<summary>The half size of the box in each dimension.</summary>
        public Vector3 halfExtents {get; set; }
        ///<summary>The rotation of the box.</summary>
        public Quaternion orientation {get; set; }
        ///<summary>The direction in which to sweep the box.</summary>
        public Vector3 direction {get; set; }
        ///<summary>The maximum distance of the sweep.</summary>
        public float distance { get; set; }
        ///<summary>The physics scene this command is run in.</summary>
        public PhysicsScene physicsScene { get; set; }
        ///<summary>Structure for specifying additional parameters for a batch query such as layer mask, hit triggers and hit backfaces.</summary>
        public QueryParameters queryParameters;

        ///<summary>Schedules a batch of boxcasts to be performed in a job.</summary>
        ///<param name="commands">A NativeArray of the BoxcastCommand to perform.</param>
        ///<param name="results">A NativeArray of RaycastHit where the result of commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="maxHits">The maximum number of Colliders the BoxCast can hit.</param>
        ///<param name="dependsOn">A JobHandle of a job that must be completed before performing the box casts.</param>
        ///<returns>Returns a JobHandle of the job that will perform the box casts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<BoxcastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = new JobHandle())
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return new JobHandle();
            }
            else if (results.Length < maxHits * commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return new JobHandle();
            }

            var jobData = new BatchQueryJob<BoxcastCommand, RaycastHit>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<BoxcastCommand, RaycastHit>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleBoxcastBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob, maxHits);
        }

        ///<summary>Schedules a batch of boxcasts to be performed in a job.</summary>
        ///<remarks>By default maxHits in this variant is set to 1.</remarks>
        ///<param name="commands">A NativeArray of the BoxcastCommand to perform.</param>
        ///<param name="results">A NativeArray of RaycastHit where the result of commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="dependsOn">A JobHandle of a job that must be completed before performing the box casts.</param>
        ///<returns>Returns a JobHandle of the job that will perform the box casts.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<BoxcastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = new JobHandle())
        {
            return ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
        }

        [FreeFunction("ScheduleBoxcastCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleBoxcastBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob, int maxHits);
    }

    ///<summary>Struct used to set up a closest point command to be performed asynchronously during a job.
    ///
    ///When you use this struct to schedule a batch of closest commands, they are performed asynchronously and in parallel to each other. The results of the closest points are written to the results buffer. Because the results are written asynchronously, the results buffer cannot be accessed until the job has been completed.
    ///
    ///The result for a command at index N in the command buffer is stored at index N in the results buffer.</summary>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using Unity.Jobs;
    ///using UnityEngine;
    ///
    ///public class ClosestPoint : MonoBehaviour
    ///{
    ///    private void Start()
    ///    {
    ///        var collider = new GameObject().AddComponent<BoxCollider>();
    ///        // Perform a single closest point using ClosestPointCommand and wait for it to complete
    ///        // Set up the command and result buffers
    ///        var results = new NativeArray<Vector3>(1, Allocator.TempJob);
    ///
    ///        var commands = new NativeArray<ClosestPointCommand>(1, Allocator.TempJob);
    ///
    ///        commands[0] = new ClosestPointCommand(Vector3.one * 5, collider.GetEntityId(), Vector3.zero, Quaternion.identity, collider.transform.lossyScale);
    ///
    ///        // Schedule the batch of closest points
    ///        JobHandle handle = ClosestPointCommand.ScheduleBatch(commands, results, 1, default(JobHandle));
    ///
    ///        // Wait for the batch processing job to complete
    ///        handle.Complete();
    ///
    ///        // Copy the result. If the point is inside of the Collider, it is returned as a result
    ///        Vector3 closestPoint = results[0];
    ///
    ///        // Dispose of the buffers
    ///        results.Dispose();
    ///        commands.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.ClosestPoint" />
    [NativeHeader("Modules/Physics/BatchCommands/ClosestPointCommand.h")]
    [NativeHeader("ManagedKernel/Jobs/ScriptBindings/JobsBindingsTypes.h")]
    public struct ClosestPointCommand
    {
        ///<summary>Obsolete. Use the constructor with EntityId instead.</summary>
        ///<remarks>Create a ClosestPointCommand using Instance ID of the Collider.</remarks>
        ///<param name="point">Location you want to find the closest point to.</param>
        ///<param name="colliderInstanceID">The ID of the Collider that you find the closest point on.</param>
        ///<param name="position">The position of the Collider.</param>
        ///<param name="rotation">The rotation of the Collider.</param>
        ///<param name="scale">The global scale of the Collider.</param>
        [System.Obsolete("ClosestPointCommand(Vector3, int, Vector3, Quaternion, Vector3) is obsolete. Use ClosestPointCommand(Vector3, EntityId, Vector3, Quaternion, Vector3) instead.", true)]
        public ClosestPointCommand(Vector3 point, int colliderInstanceID, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.point = point;
            this.colliderEntityId = colliderInstanceID;
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        ///<summary>Create a ClosestPointCommand using a Collider reference.</summary>
        ///<remarks>Can only be invoked from the main thread, since Unity Components are unavailable off the main thread. In the threaded context, use the other constructor that accepts EntityId instead.</remarks>
        ///<param name="point">Location you want to find the closest point to.</param>
        ///<param name="collider">The Collider that you find the closest point on.</param>
        ///<param name="position">The position of the Collider.</param>
        ///<param name="rotation">The rotation of the Collider.</param>
        ///<param name="scale">The global scale of the Collider.</param>
        public ClosestPointCommand(Vector3 point, Collider collider, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.point = point;
            this.colliderEntityId = collider.GetEntityId();
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        ///<summary>Create a ClosestPointCommand using EntityId of the Collider.</summary>
        ///<param name="point">Location you want to find the closest point to.</param>
        ///<param name="colliderEntityId">The EntityId of the Collider that you find the closest point on.</param>
        ///<param name="position">The position of the Collider.</param>
        ///<param name="rotation">The rotation of the Collider.</param>
        ///<param name="scale">The global scale of the Collider.</param>
        public ClosestPointCommand(Vector3 point, EntityId colliderEntityId, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.point = point;
            this.colliderEntityId = colliderEntityId;
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        ///<summary>Location you want to find the closest point to.</summary>
        public Vector3 point { get; set; }
        ///<summary>The EntityId of the Collider that you find the closest point on.</summary>
        public EntityId colliderEntityId { get; set; }
        ///<summary>Obsolete. Use colliderEntityId instead.</summary>
        ///<remarks>The ID of the Collider that you find the closest point on.</remarks>
        [System.Obsolete("colliderInstanceID is deprecated, use colliderEntityId instead.", true)]
        public int colliderInstanceID
        {
            get { return colliderEntityId; }
            set { colliderEntityId = value; }
        }
        ///<summary>The position of the Collider.</summary>
        public Vector3 position { get; set; }
        ///<summary>The rotation of the Collider.</summary>
        public Quaternion rotation { get; set; }
        ///<summary>The global scale of the Collider.</summary>
        public Vector3 scale { get; set; }

        ///<summary>Schedule a batch of closest points which are performed in a job.</summary>
        ///<param name="commands">A NativeArray of the ClosestPointCommands to perform.</param>
        ///<param name="results">A NativeArray of the Vector3 where the results of the commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of jobs which should be performed in a single job.</param>
        ///<param name="dependsOn">A JobHandle of a job which must be completed before the closest point starts.</param>
        ///<returns>The JobHandle of the job that performs the closest point commands.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<ClosestPointCommand> commands, NativeArray<Vector3> results, int minCommandsPerJob, JobHandle dependsOn = new JobHandle())
        {
            var jobData = new BatchQueryJob<ClosestPointCommand, Vector3>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<ClosestPointCommand, Vector3>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleClosestPointCommandBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob);
        }

        [FreeFunction("ScheduleClosestPointCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleClosestPointCommandBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob);
    }

    ///<summary>Struct used to setup an overlap sphere command to be performed asynchronously during a job.</summary>
    ///<remarks>When you use this struct to schedule a batch of overlap sphere commands, the commands are performed asynchronously. The results of the overlap sphere are written to the results buffer. Because the results are written asynchronously, the results buffer can't be accessed until the job is complete.
    ///
    ///The results for a command at index N in the command buffer are stored at index N * maxHits in the results buffer.
    ///
    ///If maxHits is larger than the actual number of results for the command the result buffer will contain some invalid results which did not hit anything. The first invalid result is identified by the collider instance ID being 0. The second and later invalid results are not written to the overlap sphere command so their collider instance IDs are not guaranteed to be 0. When iterating over the results the loop should stop when the first invalid result is found.
    ///
    ///Overlap sphere command also controls whether or not Trigger colliders generate a hit. You should adjust maxHits and result array size accordingly to store all hits. Use <see cref="QueryParameters" /> to control hit flags. <see cref="QueryParameters.hitBackfaces" /> and <see cref="QueryParameters.hitMultipleFaces" /> flags are not supported and won’t have any impact on overlap results.
    ///
    ///Note: Only BatchQuery.ExecuteOverlapSphere is logged into the profiler. Query count information is not logged.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using UnityEngine;
    ///
    ///public class SphereOverlap : MonoBehaviour
    ///{
    ///    //Print names of GameObject inside the sphere
    ///    void BatchOverlapSphere()
    ///    {
    ///        var commands = new NativeArray<OverlapSphereCommand>(1, Allocator.TempJob);
    ///        var results = new NativeArray<ColliderHit>(3, Allocator.TempJob);
    ///
    ///        commands[0] = new OverlapSphereCommand(Vector3.zero, 10f, QueryParameters.Default);
    ///
    ///        OverlapSphereCommand.ScheduleBatch(commands, results, 1, 3).Complete();
    ///
    ///        foreach (var hit in results)
    ///            Debug.Log(hit.collider.name);
    ///
    ///        commands.Dispose();
    ///        results.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.OverlapSphere" />
    ///<seealso cref="ColliderHit" />
    [NativeHeader("Modules/Physics/BatchCommands/OverlapSphereCommand.h")]
    public struct OverlapSphereCommand
    {
        ///<summary>Create an OverlapSphereCommand.</summary>
        ///<remarks>This query is run in the default physics scene.</remarks>
        ///<param name="point">The center of the sphere.</param>
        ///<param name="radius">The radius of the sphere.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</param>
        public OverlapSphereCommand(Vector3 point, float radius, QueryParameters queryParameters)
        {
            this.point = point;
            this.radius = radius;
            this.queryParameters = queryParameters;
            this.physicsScene = Physics.defaultPhysicsScene;
        }

        ///<summary>Create an OverlapSphereCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the overlap sphere query in.</param>
        ///<param name="point">The center of the sphere.</param>
        ///<param name="radius">The radius of the sphere.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</param>
        public OverlapSphereCommand(PhysicsScene physicsScene, Vector3 point, float radius, QueryParameters queryParameters)
        {
            this.physicsScene = physicsScene;
            this.point = point;
            this.radius = radius;
            this.queryParameters = queryParameters;
        }

        ///<summary>The center of the sphere.</summary>
        public Vector3 point { get; set; }
        ///<summary>The radius of the sphere.</summary>
        public float radius {get; set; }
        ///<summary>The physics scene the command is run in.</summary>
        public PhysicsScene physicsScene { get; set; }
        ///<summary>Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</summary>
        public QueryParameters queryParameters;

        ///<summary>Schedule a batch of overlap sphere commands to perform in a job.</summary>
        ///<param name="commands">A NativeArray of the OverlapSphereCommands to perform.</param>
        ///<param name="results">A NativeArray of the ColliderHits where the results of the commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="maxHits">The maximum number of Colliders the overlap can hit.</param>
        ///<param name="dependsOn">A JobHandle of a job which must be completed before the overlap sphere starts.</param>
        ///<returns>The JobHandle of the job which will perform the overlap sphere.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<OverlapSphereCommand> commands, NativeArray<ColliderHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = new JobHandle())
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return new JobHandle();
            }
            else if (results.Length < maxHits * commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return new JobHandle();
            }

            var jobData = new BatchQueryJob<OverlapSphereCommand, ColliderHit>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<OverlapSphereCommand, ColliderHit>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleOverlapSphereBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob, maxHits);
        }

        [FreeFunction("ScheduleOverlapSphereCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleOverlapSphereBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob, int maxHits);
    }

    ///<summary>Struct used to set up an overlap box command to be performed asynchronously during a job.</summary>
    ///<remarks>When you use this struct to schedule a batch of overlap box commands, the commands are performed asynchronously. The results of the overlap box are written to the results buffer. Because the results are written asynchronously, the results buffer can't be accessed until the job is complete.
    ///
    ///The results for a command at index N in the command buffer are stored at index N * maxHits in the results buffer.
    ///
    ///If maxHits is larger than the actual number of results for the command the result buffer will contain some invalid results which did not hit anything. The first invalid result is identified by the collider instance ID being 0. The second and later invalid results are not written to the overlap box command so their collider instance IDs are not guaranteed to be 0. When iterating over the results the loop should stop when the first invalid result is found.
    ///
    ///Overlap box command also controls whether or not Trigger colliders generate a hit. You should adjust maxHits and result array size accordingly to store all hits. Use <see cref="QueryParameters" /> to control hit flags. <see cref="QueryParameters.hitBackfaces" /> and <see cref="QueryParameters.hitMultipleFaces" /> flags are not supported and won’t have any impact on overlap results.
    ///
    ///Note: Only BatchQuery.ExecuteOverlapBoxJob is logged into the profiler. Query count information is not logged.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using UnityEngine;
    ///
    ///public class BoxOverlap : MonoBehaviour
    ///{
    ///    //Print names of GameObjects inside the box
    ///    void BatchOverlapBox()
    ///    {
    ///        var commands = new NativeArray<OverlapBoxCommand>(1, Allocator.TempJob);
    ///        var results = new NativeArray<ColliderHit>(3, Allocator.TempJob);
    ///
    ///        commands[0] = new OverlapBoxCommand(Vector3.zero, Vector3.one, Quaternion.identity, QueryParameters.Default);
    ///
    ///        OverlapBoxCommand.ScheduleBatch(commands, results, 1, 3).Complete();
    ///
    ///        foreach (var hit in results)
    ///            Debug.Log(hit.collider.name);
    ///
    ///        commands.Dispose();
    ///        results.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.OverlapBox" />
    ///<seealso cref="ColliderHit" />
    [NativeHeader("Modules/Physics/BatchCommands/OverlapBoxCommand.h")]
    public struct OverlapBoxCommand
    {
        ///<summary>Create an OverlapBoxCommand.</summary>
        ///<remarks>The query is run in the default physics scene.</remarks>
        ///<param name="center">The center of the box.</param>
        ///<param name="halfExtents">The half of the size of the box in each dimension.</param>
        ///<param name="orientation">The orientation of the box.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask, hit triggers.</param>
        public OverlapBoxCommand(Vector3 center, Vector3 halfExtents, Quaternion orientation, QueryParameters queryParameters)
        {
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.queryParameters = queryParameters;
            this.physicsScene = Physics.defaultPhysicsScene;
        }

        ///<summary>Create a OverlapBoxCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the overlap box query in.</param>
        ///<param name="center">The center of the box.</param>
        ///<param name="halfExtents">Half of the size of the box in each dimension.</param>
        ///<param name="orientation">The orientation of the box.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</param>
        public OverlapBoxCommand(PhysicsScene physicsScene, Vector3 center, Vector3 halfExtents, Quaternion orientation, QueryParameters queryParameters)
        {
            this.physicsScene = physicsScene;
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.queryParameters = queryParameters;
        }

        ///<summary>The center of the box.</summary>
        public Vector3 center { get; set; }
        ///<summary>Half of the size of the box in each dimension.</summary>
        public Vector3 halfExtents { get; set; }
        ///<summary>The orientation of the box.</summary>
        public Quaternion orientation { get; set; }
        ///<summary>The physics scene this command is run in.</summary>
        public PhysicsScene physicsScene { get; set; }
        ///<summary>Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</summary>
        public QueryParameters queryParameters;

        ///<summary>Schedule a batch of overlap box commands to perform in a job.</summary>
        ///<param name="commands">A NativeArray of the OverlapBoxCommands to perform.</param>
        ///<param name="results">A NativeArray of the ColliderHits where the results of the commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="maxHits">The maximum number of Colliders the overlap can hit.</param>
        ///<param name="dependsOn">A JobHandle of a job which must be completed before the overlap box starts.</param>
        ///<returns>The JobHandle of the job which will perform the overlap box.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<OverlapBoxCommand> commands, NativeArray<ColliderHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = new JobHandle())
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return new JobHandle();
            }
            else if (results.Length < maxHits * commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return new JobHandle();
            }

            var jobData = new BatchQueryJob<OverlapBoxCommand, ColliderHit>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<OverlapBoxCommand, ColliderHit>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleOverlapBoxBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob, maxHits);
        }

        [FreeFunction("ScheduleOverlapBoxCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleOverlapBoxBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob, int maxHits);
    }

    ///<summary>Struct used to set up an overlap capsule command to be performed asynchronously during a job.</summary>
    ///<remarks>When you use this struct to schedule a batch of overlap capsule commands, the commands are performed asynchronously. The results of the overlap capsule are written to the results buffer. Because the results are written asynchronously, the results buffer can't be accessed until the job is complete.
    ///
    ///The results for a command at index N in the command buffer are stored at index N * maxHits in the results buffer.
    ///
    ///If maxHits is larger than the actual number of results for the command the result buffer will contain some invalid results which did not hit anything. The first invalid result is identified by the collider instance ID being 0. The second and later invalid results are not written to the overlap capsule command so their collider instance IDs are not guaranteed to be 0. When iterating over the results the loop should stop when the first invalid result is found.
    ///
    ///Overlap capsule command also controls whether or not Trigger colliders generate a hit. You should adjust maxHits and result array size accordingly to store all hits. Use QueryParameters to control hit flags. <see cref="QueryParameters.hitBackfaces" /> and <see cref="QueryParameters.hitMultipleFaces" /> flags are not supported and won’t have any impact on overlap results.
    ///
    ///Note: Only BatchQuery.ExecuteOverlapCapsuleJob is logged into the profiler. Query count information is not logged.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using Unity.Collections;
    ///using UnityEngine;
    ///
    ///public class CapsuleOverlap : MonoBehaviour
    ///{
    ///    //Print iname of GameObjects inside the capsule
    ///    void BatchOverlapCapsule()
    ///    {
    ///        var commands = new NativeArray<OverlapCapsuleCommand>(1, Allocator.TempJob);
    ///        var results = new NativeArray<ColliderHit>(3, Allocator.TempJob);
    ///
    ///        commands[0] = new OverlapCapsuleCommand(Vector3.zero, Vector3.one, 10f, QueryParameters.Default);
    ///
    ///        OverlapCapsuleCommand.ScheduleBatch(commands, results, 1, 3).Complete();
    ///
    ///        foreach (var hit in results)
    ///            Debug.Log(hit.collider.name);
    ///
    ///        commands.Dispose();
    ///        results.Dispose();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="Physics.OverlapCapsule" />
    ///<seealso cref="ColliderHit" />
    [NativeHeader("Modules/Physics/BatchCommands/OverlapCapsuleCommand.h")]
    public struct OverlapCapsuleCommand
    {
        ///<summary>Create an OverlapCapsuleCommand.</summary>
        ///<remarks>The query is run in the default physics scene.</remarks>
        ///<param name="point0">The center of the sphere at the <c>start</c> of the capsule.</param>
        ///<param name="point1">The center of the sphere at the <c>end</c> of the capsule.</param>
        ///<param name="radius">The radius of the capsule.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</param>
        public OverlapCapsuleCommand(Vector3 point0, Vector3 point1, float radius, QueryParameters queryParameters)
        {
            this.point0 = point0;
            this.point1 = point1;
            this.radius = radius;
            this.queryParameters = queryParameters;
            this.physicsScene = Physics.defaultPhysicsScene;
        }

        ///<summary>Create an OverlapCapsuleCommand.</summary>
        ///<param name="physicsScene">The physics scene this command is run in.</param>
        ///<param name="point0">The center of the sphere at the <c>start</c> of the capsule.</param>
        ///<param name="point1">The center of the sphere at the <c>end</c> of the capsule.</param>
        ///<param name="radius">The radius of the capsule.</param>
        ///<param name="queryParameters">Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</param>
        public OverlapCapsuleCommand(PhysicsScene physicsScene, Vector3 point0, Vector3 point1, float radius, QueryParameters queryParameters)
        {
            this.physicsScene = physicsScene;
            this.point0 = point0;
            this.point1 = point1;
            this.radius = radius;
            this.queryParameters = queryParameters;
        }

        ///<summary>The center of the sphere at the <c>start</c> of the capsule.</summary>
        public Vector3 point0 { get; set; }
        ///<summary>The center of the sphere at the <c>end</c> of the capsule.</summary>
        public Vector3 point1 { get; set; }
        ///<summary>The radius of the capsule.</summary>
        public float radius { get; set; }
        ///<summary>The physics scene this command is run in.</summary>
        public PhysicsScene physicsScene { get; set; }
        ///<summary>Structure for specifying additional parameters for a batch query such as layer mask or hit triggers.</summary>
        public QueryParameters queryParameters;

        ///<summary>Schedule a batch of overlap capsule commands to perform in a job.</summary>
        ///<param name="commands">A NativeArray of the OverlapCapsuleCommands to perform.</param>
        ///<param name="results">A NativeArray of the ColliderHits where the results of the commands are stored.</param>
        ///<param name="minCommandsPerJob">The minimum number of commands to perform in a single job.</param>
        ///<param name="maxHits">The maximum number of Colliders the overlap can hit.</param>
        ///<param name="dependsOn">A JobHandle of a job which must be completed before the overlap capsule starts.</param>
        ///<returns>The JobHandle of the job wich will perform the overlap capsule.</returns>
        public unsafe static JobHandle ScheduleBatch(NativeArray<OverlapCapsuleCommand> commands, NativeArray<ColliderHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = new JobHandle())
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return new JobHandle();
            }
            else if (results.Length < maxHits * commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return new JobHandle();
            }

            var jobData = new BatchQueryJob<OverlapCapsuleCommand, ColliderHit>(commands, results);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), BatchQueryJobStruct<BatchQueryJob<OverlapCapsuleCommand, ColliderHit>>.Initialize(), dependsOn, ScheduleMode.Parallel);

            return ScheduleOverlapCapsuleBatch(ref scheduleParams, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(commands), commands.Length, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(results), results.Length, minCommandsPerJob, maxHits);
        }

        [FreeFunction("ScheduleOverlapCapsuleCommandBatch", ThrowsException = true)]
        unsafe extern private static JobHandle ScheduleOverlapCapsuleBatch(ref JobsUtility.JobScheduleParameters parameters, void* commands, int commandLen, void* result, int resultLen, int minCommandsPerJob, int maxHits);
    }
}
