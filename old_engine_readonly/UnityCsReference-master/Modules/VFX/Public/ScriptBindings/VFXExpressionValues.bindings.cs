// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine.VFX
{
    ///<summary>This class is a wrapper to the set of expression values.</summary>
    ///<remarks>Only used with <see cref="VFX.VFXSpawnerState" />.</remarks>
    [RequiredByNativeCode]
    [StructLayout(LayoutKind.Sequential)]
    [NativeHeader("Modules/VFX/Public/VFXExpressionValues.h")]
    public class VFXExpressionValues
    {
        internal IntPtr m_Ptr;
        private VFXExpressionValues()
        {
        }

        [RequiredByNativeCode]
        static internal VFXExpressionValues CreateExpressionValuesWrapper(IntPtr ptr)
        {
            var expressionValue = new VFXExpressionValues();
            expressionValue.m_Ptr = ptr;
            return expressionValue;
        }

        ///<summary>Returns a boolean that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<bool>")] extern public bool GetBool(int nameID);
        ///<summary>Returns an integer that corresponds to the bound named expression. IF this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<int>")] extern public int GetInt(int nameID);
        ///<summary>Returns an unsigned integer that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<UInt32>")] extern public uint GetUInt(int nameID);
        ///<summary>Returns a float that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<float>")] extern public float GetFloat(int nameID);
        ///<summary>Returns a Vector2 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<Vector2f>")] extern public Vector2 GetVector2(int nameID);
        ///<summary>Returns a Vector3 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<Vector3f>")] extern public Vector3 GetVector3(int nameID);
        ///<summary>Returns a Vector4 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<Vector4f>")] extern public Vector4 GetVector4(int nameID);
        ///<summary>Returns a Matrix4 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<Matrix4x4f>")] extern public Matrix4x4 GetMatrix4x4(int nameID);
        ///<summary>Returns a texture that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<Texture*>")] extern public Texture GetTexture(int nameID);
        ///<summary>Returns a mesh that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        [NativeMethod(ThrowsException = true), NativeName("GetValueFromScript<Mesh*>")] extern public Mesh GetMesh(int nameID);

        ///<summary>Returns a an animation curve that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        public AnimationCurve GetAnimationCurve(int nameID)
        {
            var animationCurve = new AnimationCurve();
            Internal_GetAnimationCurveFromScript(nameID, animationCurve);
            return animationCurve;
        }

        [NativeMethod(ThrowsException = true)]
        extern internal void Internal_GetAnimationCurveFromScript(int nameID, AnimationCurve curve);
        ///<summary>Returns a gradient that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="nameID">The name ID of the property retrieved by <see cref="Shader.PropertyToID" />.</param>
        public Gradient GetGradient(int nameID)
        {
            var gradient = new Gradient();
            Internal_GetGradientFromScript(nameID, gradient);
            return gradient;
        }

        [NativeMethod(ThrowsException = true)]
        extern internal void Internal_GetGradientFromScript(int nameID, Gradient gradient);

        ///<summary>Returns a boolean that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public bool GetBool(string name)
        {
            return GetBool(Shader.PropertyToID(name));
        }

        ///<summary>Returns an integer that corresponds to the bound named expression. IF this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public int GetInt(string name)
        {
            return GetInt(Shader.PropertyToID(name));
        }

        ///<summary>Returns an unsigned integer that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public uint GetUInt(string name)
        {
            return GetUInt(Shader.PropertyToID(name));
        }

        ///<summary>Returns a float that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public float GetFloat(string name)
        {
            return GetFloat(Shader.PropertyToID(name));
        }

        ///<summary>Returns a Vector2 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public Vector2 GetVector2(string name)
        {
            return GetVector2(Shader.PropertyToID(name));
        }

        ///<summary>Returns a Vector3 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public Vector3 GetVector3(string name)
        {
            return GetVector3(Shader.PropertyToID(name));
        }

        ///<summary>Returns a Vector4 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public Vector4 GetVector4(string name)
        {
            return GetVector4(Shader.PropertyToID(name));
        }

        ///<summary>Returns a Matrix4 that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public Matrix4x4 GetMatrix4x4(string name)
        {
            return GetMatrix4x4(Shader.PropertyToID(name));
        }

        ///<summary>Returns a texture that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public Texture GetTexture(string name)
        {
            return GetTexture(Shader.PropertyToID(name));
        }

        ///<summary>Returns a an animation curve that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public AnimationCurve GetAnimationCurve(string name)
        {
            return GetAnimationCurve(Shader.PropertyToID(name));
        }

        ///<summary>Returns a gradient that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public Gradient GetGradient(string name)
        {
            return GetGradient(Shader.PropertyToID(name));
        }

        ///<summary>Returns a mesh that corresponds to the bound named expression. If this entry is not available, or the type doesn't match, an exception is thrown.</summary>
        ///<param name="name">The name of the property.</param>
        public Mesh GetMesh(string name)
        {
            return GetMesh(Shader.PropertyToID(name));
        }

        internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(VFXExpressionValues vFXExpressionValues) => vFXExpressionValues.m_Ptr;
        }
    }
}
