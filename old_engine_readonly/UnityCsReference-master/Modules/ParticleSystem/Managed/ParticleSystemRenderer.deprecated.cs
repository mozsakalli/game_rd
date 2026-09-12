// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    ///<summary>All possible Particle System vertex shader inputs.</summary>
    [Flags, Obsolete("ParticleSystemVertexStreams is deprecated. Please use ParticleSystemVertexStream instead.", false)]
    public enum ParticleSystemVertexStreams
    {
        ///<summary>The world space position of each particle.</summary>
        Position = 1 << 0,
        ///<summary>The normal of each particle.</summary>
        Normal = 1 << 1,
        ///<summary>Tangent vectors for normal mapping.</summary>
        Tangent = 1 << 2,
        ///<summary>The color of each particle.</summary>
        Color = 1 << 3,
        ///<summary>The texture coordinates of each particle.</summary>
        UV = 1 << 4,
        ///<summary>With the TextureSheetAnimationModule enabled, this contains the UVs for the second texture frame, the blend factor for each particle, and the raw frame, allowing for blending of frames.</summary>
        UV2BlendAndFrame = 1 << 5,
        ///<summary>The center position of each particle, with the vertex ID of each particle, from 0-3, stored in the w component.</summary>
        CenterAndVertexID = 1 << 6,
        ///<summary>The size of each particle.</summary>
        Size = 1 << 7,
        ///<summary>The rotation of each particle.</summary>
        Rotation = 1 << 8,
        ///<summary>The 3D velocity of each particle.</summary>
        Velocity = 1 << 9,
        ///<summary>Alive time as a 0-1 value in the X component, and Total Lifetime in the Y component.
        ///To get the current particle age, simply multiply X by Y.</summary>
        Lifetime = 1 << 10,
        ///<summary>The first stream of custom data, supplied from script.</summary>
        ///<seealso cref="ParticleSystem.SetCustomParticleData" />
        ///<seealso cref="ParticleSystem.GetCustomParticleData" />
        Custom1 = 1 << 11,
        ///<summary>The second stream of custom data, supplied from script.</summary>
        ///<seealso cref="ParticleSystem.SetCustomParticleData" />
        ///<seealso cref="ParticleSystem.GetCustomParticleData" />
        Custom2 = 1 << 12,
        ///<summary>4 random numbers. The first 3 are deterministic and assigned once when each particle is born, but the 4th value will change during the lifetime of the particle.</summary>
        Random = 1 << 13,
        ///<summary>A mask with no vertex streams enabled.</summary>
        None = 0,
        ///<summary>A mask with all vertex streams enabled.</summary>
        All = 0x7fffffff
    }

    ///<summary>Use this class to render particles on to the screen.</summary>
    public sealed partial class ParticleSystemRenderer
    {
        ///<summary>Enable a set of vertex Shader streams on the Particle System renderer.</summary>
        ///<remarks>
        ///  <para />
        ///  <para>Here is an example of a custom Shader that you can use with the above script:</para>
        ///</remarks>
        ///<param name="streams">Streams to enable.</param>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEditor;
        ///using System.Collections.Generic;
        ///
        ///[RequireComponent(typeof(ParticleSystem))]
        ///public class ExampleClass : MonoBehaviour {
        ///
        ///    private ParticleSystem ps;
        ///    private ParticleSystemRenderer psr;
        ///    private List<Vector4> customData = new List<Vector4>();
        ///    public float minDist = 30.0f;
        ///
        ///    void Start() {
        ///
        ///        ps = GetComponent<ParticleSystem>();
        ///        psr = GetComponent<ParticleSystemRenderer>();
        ///
        ///        // emit in a sphere with no speed
        ///        var main = ps.main;
        ///        main.startSpeedMultiplier = 0.0f;
        ///        main.simulationSpace = ParticleSystemSimulationSpace.World; // so our particle positions don't require any extra transformation, to compare with the mouse position
        ///        var emission = ps.emission;
        ///        emission.rateOverTimeMultiplier = 200.0f;
        ///        var shape = ps.shape;
        ///        shape.shapeType = ParticleSystemShapeType.Sphere;
        ///        shape.radius = 4.0f;
        ///        psr.sortMode = ParticleSystemSortMode.YoungestInFront;
        ///
        ///        // send custom data to the shader
        ///        psr.EnableVertexStreams(ParticleSystemVertexStreams.Custom1);
        ///    }
        ///
        ///    void Update() {
        ///
        ///        Camera mainCam = Camera.main;
        ///
        ///        ps.GetCustomParticleData(customData, ParticleSystemCustomData.Custom1);
        ///
        ///        int particleCount = ps.particleCount;
        ///        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[particleCount];
        ///        ps.GetParticles(particles);
        ///
        ///        for (int i = 0; i < particles.Length; i++)
        ///        {
        ///            Vector3 sPos = mainCam.WorldToScreenPoint(particles[i].position + ps.transform.position);
        ///
        ///            // set custom data to 1, if close enough to the mouse
        ///            if (Vector2.Distance(sPos, Input.mousePosition) < minDist)
        ///            {
        ///                customData[i] = new Vector4(1, 0, 0, 0);
        ///            }
        ///            // otherwise, fade the custom data back to 0
        ///            else
        ///            {
        ///                float particleLife = particles[i].remainingLifetime / ps.main.startLifetimeMultiplier;
        ///
        ///                if (customData[i].x > 0)
        ///                {
        ///                    float x = customData[i].x;
        ///                    x = Mathf.Max(x - Time.deltaTime, 0.0f);
        ///                    customData[i] = new Vector4(x, 0, 0, 0);
        ///                }
        ///            }
        ///        }
        ///
        ///        ps.SetCustomParticleData(customData, ParticleSystemCustomData.Custom1);
        ///    }
        ///
        ///    void OnGUI() {
        ///
        ///        minDist = GUI.HorizontalSlider(new Rect(25, 40, 100, 30), minDist, 0.0f, 100.0f);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<example nocheck="true">
        ///  <code><![CDATA[Shader "Particles/CustomVertexStream" {
        ///Properties {
        ///    _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
        ///    _MainTex ("Particle Texture", 2D) = "white" {}
        ///    _OffsetValue("Offset Value", Range(0,1)) = 0.4
        ///}
        ///
        ///Category {
        ///    Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        ///    Blend SrcAlpha OneMinusSrcAlpha
        ///    ColorMask RGB
        ///    Cull Off Lighting Off ZWrite Off
        ///
        ///    SubShader {
        ///        Pass {
        ///
        ///            CGPROGRAM
        ///            #pragma vertex vert
        ///            #pragma fragment frag
        ///            #pragma multi_compile_particles
        ///            #pragma multi_compile_fog
        ///
        ///            #include "UnityCG.cginc"
        ///
        ///            sampler2D _MainTex;
        ///            fixed4 _TintColor;
        ///            float _OffsetValue;
        ///
        ///            struct appdata_t {
        ///                float4 vertex : POSITION;
        ///                float3 normal : NORMAL;
        ///                fixed4 color : COLOR;
        ///                float2 texcoord : TEXCOORD0;
        ///                float4 customData : TEXCOORD1;
        ///            };
        ///
        ///            struct v2f {
        ///                float4 vertex : SV_POSITION;
        ///                fixed4 color : COLOR;
        ///                float2 texcoord : TEXCOORD0;
        ///                float4 customData : TEXCOORD1;
        ///                UNITY_FOG_COORDS(2)
        ///            };
        ///
        ///            float4 _MainTex_ST;
        ///
        ///            v2f vert (appdata_t v)
        ///            {
        ///                v.vertex.y = lerp(v.vertex.y, v.vertex.y + _OffsetValue, v.customData.x);
        ///
        ///                v2f o;
        ///                o.vertex = UnityObjectToClipPos(v.vertex);
        ///
        ///                float4 offsetX = float4(-1, 1, 1, -1);
        ///                float4 offsetY = float4(1, 1, -1, -1);
        ///
        ///                o.color = v.color;
        ///                o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
        ///                o.customData = v.customData;
        ///                UNITY_TRANSFER_FOG(o,o.vertex);
        ///
        ///                return o;
        ///            }
        ///
        ///            fixed4 frag (v2f i) : SV_Target
        ///            {
        ///                fixed4 col = 2.0f * i.color * _TintColor * tex2D(_MainTex, i.texcoord);
        ///                fixed4 col2 = fixed4(i.customData.x, i.customData.y, i.customData.z, col.a);
        ///                fixed4 final = lerp(col, col*col2, i.customData.x);
        ///
        ///                UNITY_APPLY_FOG(i.fogCoord, final);
        ///                return final;
        ///            }
        ///            ENDCG
        ///        }
        ///    }
        ///}
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="ParticleSystemRenderer.DisableVertexStreams" />
        ///<seealso cref="ParticleSystem.SetCustomParticleData" />
        [Obsolete("EnableVertexStreams is deprecated. Use SetActiveVertexStreams instead.", false)]
        public void EnableVertexStreams(ParticleSystemVertexStreams streams) { Internal_SetVertexStreams(streams, true); }
        ///<summary>Disable a set of vertex Shader streams on the Particle System Renderer.
        ///The position stream is always enabled, and any attempts to remove it are ignored.</summary>
        ///<param name="streams">Streams to disable.</param>
        ///<seealso cref="ParticleSystemRenderer.SetActiveVertexStreams" />
        [Obsolete("DisableVertexStreams is deprecated. Use SetActiveVertexStreams instead.", false)]
        public void DisableVertexStreams(ParticleSystemVertexStreams streams) { Internal_SetVertexStreams(streams, false); }
        ///<summary>Query whether the Particle System Renderer uses a particular set of vertex streams.</summary>
        ///<param name="streams">Streams to query.</param>
        ///<returns>
        ///  <c>true</c> if the queried streams are enabled. Returns <c>false</c> otherwise.</returns>
        ///<seealso cref="ParticleSystemRenderer.GetEnabledVertexStreams" />
        ///<seealso cref="ParticleSystemRenderer.SetActiveVertexStreams" />
        [Obsolete("AreVertexStreamsEnabled is deprecated. Use GetActiveVertexStreams instead.", false)]
        public bool AreVertexStreamsEnabled(ParticleSystemVertexStreams streams) { return Internal_GetEnabledVertexStreams(streams) == streams; }
        ///<summary>Queries whether the Particle System renderer uses a particular set of vertex streams.</summary>
        ///<param name="streams">Streams to query.</param>
        ///<returns>The subset of the queried streams that are actually enabled.</returns>
        ///<seealso cref="ParticleSystemRenderer.AreVertexStreamsEnabled" />
        ///<seealso cref="ParticleSystemRenderer.SetActiveVertexStreams" />
        [Obsolete("GetEnabledVertexStreams is deprecated. Use GetActiveVertexStreams instead.", false)]
        public ParticleSystemVertexStreams GetEnabledVertexStreams(ParticleSystemVertexStreams streams) { return Internal_GetEnabledVertexStreams(streams); }

        [Obsolete("Internal_SetVertexStreams is deprecated. Use SetActiveVertexStreams instead.", false)]
        internal void Internal_SetVertexStreams(ParticleSystemVertexStreams streams, bool enabled)
        {
            List<ParticleSystemVertexStream> streamList = new List<ParticleSystemVertexStream>(activeVertexStreamsCount);
            GetActiveVertexStreams(streamList);

            if (enabled)
            {
                if ((streams & ParticleSystemVertexStreams.Position) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Position)) { streamList.Add(ParticleSystemVertexStream.Position); } }
                if ((streams & ParticleSystemVertexStreams.Normal) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Normal)) { streamList.Add(ParticleSystemVertexStream.Normal); } }
                if ((streams & ParticleSystemVertexStreams.Tangent) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Tangent)) { streamList.Add(ParticleSystemVertexStream.Tangent); } }
                if ((streams & ParticleSystemVertexStreams.Color) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Color)) { streamList.Add(ParticleSystemVertexStream.Color); } }
                if ((streams & ParticleSystemVertexStreams.UV) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.UV)) { streamList.Add(ParticleSystemVertexStream.UV); } }
                if ((streams & ParticleSystemVertexStreams.UV2BlendAndFrame) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.UV2)) { streamList.Add(ParticleSystemVertexStream.UV2); streamList.Add(ParticleSystemVertexStream.AnimBlend); streamList.Add(ParticleSystemVertexStream.AnimFrame); } }
                if ((streams & ParticleSystemVertexStreams.CenterAndVertexID) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Center)) { streamList.Add(ParticleSystemVertexStream.Center); streamList.Add(ParticleSystemVertexStream.VertexID); } }
                if ((streams & ParticleSystemVertexStreams.Size) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.SizeXYZ)) { streamList.Add(ParticleSystemVertexStream.SizeXYZ); } }
                if ((streams & ParticleSystemVertexStreams.Rotation) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Rotation3D)) { streamList.Add(ParticleSystemVertexStream.Rotation3D); } }
                if ((streams & ParticleSystemVertexStreams.Velocity) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Velocity)) { streamList.Add(ParticleSystemVertexStream.Velocity); } }
                if ((streams & ParticleSystemVertexStreams.Lifetime) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.AgePercent)) { streamList.Add(ParticleSystemVertexStream.AgePercent); streamList.Add(ParticleSystemVertexStream.InvStartLifetime); } }
                if ((streams & ParticleSystemVertexStreams.Custom1) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Custom1XYZW)) { streamList.Add(ParticleSystemVertexStream.Custom1XYZW); } }
                if ((streams & ParticleSystemVertexStreams.Custom2) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.Custom2XYZW)) { streamList.Add(ParticleSystemVertexStream.Custom2XYZW); } }
                if ((streams & ParticleSystemVertexStreams.Random) != 0) { if (!streamList.Contains(ParticleSystemVertexStream.StableRandomXYZ)) { streamList.Add(ParticleSystemVertexStream.StableRandomXYZ); streamList.Add(ParticleSystemVertexStream.VaryingRandomX); } }
            }
            else
            {
                if ((streams & ParticleSystemVertexStreams.Position) != 0) { streamList.Remove(ParticleSystemVertexStream.Position); }
                if ((streams & ParticleSystemVertexStreams.Normal) != 0) { streamList.Remove(ParticleSystemVertexStream.Normal); }
                if ((streams & ParticleSystemVertexStreams.Tangent) != 0) { streamList.Remove(ParticleSystemVertexStream.Tangent); }
                if ((streams & ParticleSystemVertexStreams.Color) != 0) { streamList.Remove(ParticleSystemVertexStream.Color); }
                if ((streams & ParticleSystemVertexStreams.UV) != 0) { streamList.Remove(ParticleSystemVertexStream.UV); }
                if ((streams & ParticleSystemVertexStreams.UV2BlendAndFrame) != 0) { streamList.Remove(ParticleSystemVertexStream.UV2); streamList.Remove(ParticleSystemVertexStream.AnimBlend); streamList.Remove(ParticleSystemVertexStream.AnimFrame); }
                if ((streams & ParticleSystemVertexStreams.CenterAndVertexID) != 0) { streamList.Remove(ParticleSystemVertexStream.Center); streamList.Remove(ParticleSystemVertexStream.VertexID); }
                if ((streams & ParticleSystemVertexStreams.Size) != 0) { streamList.Remove(ParticleSystemVertexStream.SizeXYZ); }
                if ((streams & ParticleSystemVertexStreams.Rotation) != 0) { streamList.Remove(ParticleSystemVertexStream.Rotation3D); }
                if ((streams & ParticleSystemVertexStreams.Velocity) != 0) { streamList.Remove(ParticleSystemVertexStream.Velocity); }
                if ((streams & ParticleSystemVertexStreams.Lifetime) != 0) { streamList.Remove(ParticleSystemVertexStream.AgePercent); streamList.Remove(ParticleSystemVertexStream.InvStartLifetime); }
                if ((streams & ParticleSystemVertexStreams.Custom1) != 0) { streamList.Remove(ParticleSystemVertexStream.Custom1XYZW); }
                if ((streams & ParticleSystemVertexStreams.Custom2) != 0) { streamList.Remove(ParticleSystemVertexStream.Custom2XYZW); }
                if ((streams & ParticleSystemVertexStreams.Random) != 0) { streamList.Remove(ParticleSystemVertexStream.StableRandomXYZW); streamList.Remove(ParticleSystemVertexStream.VaryingRandomX); }
            }

            SetActiveVertexStreams(streamList);
        }

        [Obsolete("Internal_GetVertexStreams is deprecated. Use GetActiveVertexStreams instead.", false)]
        internal ParticleSystemVertexStreams Internal_GetEnabledVertexStreams(ParticleSystemVertexStreams streams)
        {
            List<ParticleSystemVertexStream> streamList = new List<ParticleSystemVertexStream>(activeVertexStreamsCount);
            GetActiveVertexStreams(streamList);

            ParticleSystemVertexStreams deprecatedStreams = 0;
            if (streamList.Contains(ParticleSystemVertexStream.Position)) deprecatedStreams |= ParticleSystemVertexStreams.Position;
            if (streamList.Contains(ParticleSystemVertexStream.Normal)) deprecatedStreams |= ParticleSystemVertexStreams.Normal;
            if (streamList.Contains(ParticleSystemVertexStream.Tangent)) deprecatedStreams |= ParticleSystemVertexStreams.Tangent;
            if (streamList.Contains(ParticleSystemVertexStream.Color)) deprecatedStreams |= ParticleSystemVertexStreams.Color;
            if (streamList.Contains(ParticleSystemVertexStream.UV)) deprecatedStreams |= ParticleSystemVertexStreams.UV;
            if (streamList.Contains(ParticleSystemVertexStream.UV2)) deprecatedStreams |= ParticleSystemVertexStreams.UV2BlendAndFrame;
            if (streamList.Contains(ParticleSystemVertexStream.Center)) deprecatedStreams |= ParticleSystemVertexStreams.CenterAndVertexID;
            if (streamList.Contains(ParticleSystemVertexStream.SizeXYZ)) deprecatedStreams |= ParticleSystemVertexStreams.Size;
            if (streamList.Contains(ParticleSystemVertexStream.Rotation3D)) deprecatedStreams |= ParticleSystemVertexStreams.Rotation;
            if (streamList.Contains(ParticleSystemVertexStream.Velocity)) deprecatedStreams |= ParticleSystemVertexStreams.Velocity;
            if (streamList.Contains(ParticleSystemVertexStream.AgePercent)) deprecatedStreams |= ParticleSystemVertexStreams.Lifetime;
            if (streamList.Contains(ParticleSystemVertexStream.Custom1XYZW)) deprecatedStreams |= ParticleSystemVertexStreams.Custom1;
            if (streamList.Contains(ParticleSystemVertexStream.Custom2XYZW)) deprecatedStreams |= ParticleSystemVertexStreams.Custom2;
            if (streamList.Contains(ParticleSystemVertexStream.StableRandomXYZ)) deprecatedStreams |= ParticleSystemVertexStreams.Random;

            return (deprecatedStreams & streams);
        }

        ///<summary>Creates a snapshot of ParticleSystemRenderer and stores it in a <c>mesh</c>.</summary>
        ///<param name="mesh">A static Mesh to receive the snapshot of the particles.</param>
        ///<param name="useTransform">Specifies whether to include the rotation and scale of the Transform in the baked Mesh.</param>
        [Obsolete("BakeMesh with useTransform is deprecated. Use BakeMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeMesh(Mesh mesh, bool useTransform = false) { BakeMesh(mesh, Camera.main, useTransform); }
        ///<summary>Creates a snapshot of ParticleSystemRenderer and stores it in a <c>mesh</c>.</summary>
        ///<param name="mesh">A static Mesh to receive the snapshot of the particles.</param>
        ///<param name="camera">The Camera used to determine which way camera-space particles face.</param>
        ///<param name="useTransform">Specifies whether to include the rotation and scale of the Transform in the baked Mesh.</param>
        [Obsolete("BakeMesh with useTransform is deprecated. Use BakeMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeMesh(Mesh mesh, Camera camera, bool useTransform = false) { BakeMesh(mesh, camera, useTransform ? ParticleSystemBakeMeshOptions.BakeRotationAndScale : ParticleSystemBakeMeshOptions.Default); }

        ///<summary>Creates a snapshot of ParticleSystem Trails and stores them in a <c>mesh</c>.</summary>
        ///<param name="mesh">A static Mesh to receive the snapshot of the particle trails.</param>
        ///<param name="useTransform">Specifies whether to include the rotation and scale of the Transform in the baked Mesh.</param>
        [Obsolete("BakeTrailsMesh with useTransform is deprecated. Use BakeTrailsMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeTrailsMesh(Mesh mesh, bool useTransform = false) { BakeTrailsMesh(mesh, Camera.main, useTransform); }
        ///<summary>Creates a snapshot of ParticleSystem Trails and stores them in a <c>mesh</c>.</summary>
        ///<param name="mesh">A static Mesh to receive the snapshot of the particle trails.</param>
        ///<param name="camera">The Camera used to determine which way camera-space trails face.</param>
        ///<param name="useTransform">Specifies whether to include the rotation and scale of the Transform in the baked Mesh.</param>
        [Obsolete("BakeTrailsMesh with useTransform is deprecated. Use BakeTrailsMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeTrailsMesh(Mesh mesh, Camera camera, bool useTransform = false) { BakeTrailsMesh(mesh, camera, useTransform ? ParticleSystemBakeMeshOptions.BakeRotationAndScale : ParticleSystemBakeMeshOptions.Default); }
    }
}
