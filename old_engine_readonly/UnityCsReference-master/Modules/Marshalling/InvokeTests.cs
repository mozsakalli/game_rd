// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using static UnityEngine.InvokeTestsUtility.MonoBehaviourLikeClassWithMagicMethods;

namespace UnityEngine
{
    internal static class InvokeTestsUtility
    {
        // The Banned API Analyzer forbids System.Reflection.MethodInfo in runtime modules to keep player
        // metadata size down. These tests exist specifically to measure the cost of the reflection-based
        // method discovery and delegate creation paths against their native equivalents, so the banned
        // APIs are the subject under test here and cannot be replaced with a reflection-free alternative.
#pragma warning disable RS0030
        public static bool TypeHasOverrideMethodDeclared(Type type, Type baseType, string methodName)
        {
            while (type != baseType)
            {
                var methodInfo = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (methodInfo != null)
                    return true;

                type = type.BaseType;
            }
            return false;
        }
#pragma warning restore RS0030

        [RequiredByNativeCode]
        internal class MonoBehaviourLikeClassWithMagicMethods
        {
            // Derived classes may have these methods declared:
            // void OnEvent0();
            // void OnEvent1(IntPtr a);
            // void OnEvent2(IntPtr a, IntPtr b);
            // void OnEvent3(IntPtr a, IntPtr b, object c);

            [RequiredByNativeCode]
            internal static void GCHandleFree(IntPtr handlePtr)
            {
                if (handlePtr != IntPtr.Zero)
                    GCHandle.FromIntPtr(handlePtr).Free();
            }

            abstract class MagicMethodsDelegateVirtualProxyBase
            {
                public virtual void OnEvent0(object target) { }
                public virtual void OnEvent1(object target, IntPtr a) { }
                public virtual void OnEvent2(object target, IntPtr a, IntPtr b) { }
                public virtual void OnEvent3(object target, IntPtr a, IntPtr b, object c) { }
            }

            class MagicMethodsDelegateVirtualProxy<T> : MagicMethodsDelegateVirtualProxyBase where T : class
            {
                readonly Action<T> m_OnEvent0Delegate;
                readonly Action<T, IntPtr> m_OnEvent1Delegate;
                readonly Action<T, IntPtr, IntPtr> m_OnEvent2Delegate;
                readonly Action<T, IntPtr, IntPtr, object> m_OnEvent3Delegate;

                // See the note on TypeHasOverrideMethodDeclared: MethodInfo.CreateDelegate is what this
                // benchmark measures, so the banned API cannot be avoided here.
#pragma warning disable RS0030
                public MagicMethodsDelegateVirtualProxy()
                {
                    m_OnEvent0Delegate = (Action<T>)(typeof(T).GetMethod("OnEvent0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.CreateDelegate(typeof(Action<T>)));
                    m_OnEvent1Delegate = (Action<T, IntPtr>)(typeof(T).GetMethod("OnEvent1", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.CreateDelegate(typeof(Action<T, IntPtr>)));
                    m_OnEvent2Delegate = (Action<T, IntPtr, IntPtr>)(typeof(T).GetMethod("OnEvent2", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.CreateDelegate(typeof(Action<T, IntPtr, IntPtr>)));
                    m_OnEvent3Delegate = (Action<T, IntPtr, IntPtr, object>)(typeof(T).GetMethod("OnEvent3", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.CreateDelegate(typeof(Action<T, IntPtr, IntPtr, object>)));
                }
#pragma warning restore RS0030

                public override void OnEvent0(object target)
                {
                    m_OnEvent0Delegate?.Invoke(UnsafeUtility.As<T>(target));
                }

                public override void OnEvent1(object target, IntPtr a)
                {
                    m_OnEvent1Delegate?.Invoke(UnsafeUtility.As<T>(target), a);
                }

                public override void OnEvent2(object target, IntPtr a, IntPtr b)
                {
                    m_OnEvent2Delegate?.Invoke(UnsafeUtility.As<T>(target), a, b);
                }

                public override void OnEvent3(object target, IntPtr a, IntPtr b, object c)
                {
                    m_OnEvent3Delegate?.Invoke(UnsafeUtility.As<T>(target), a, b, c);
                }
            }

            [RequiredByNativeCode]
            internal static void InvokeDelegateOnEvent0(IntPtr d, MonoBehaviourLikeClassWithMagicMethods target)
            {
                var gcHandle = GCHandle.FromIntPtr(d);
                ((MagicMethodsDelegateVirtualProxyBase)gcHandle.Target).OnEvent0(target);
            }

            [RequiredByNativeCode]
            internal static void InvokeDelegateOnEvent1(IntPtr d, MonoBehaviourLikeClassWithMagicMethods target, IntPtr a)
            {
                var gcHandle = GCHandle.FromIntPtr(d);
                ((MagicMethodsDelegateVirtualProxyBase)gcHandle.Target).OnEvent1(target, a);
            }

            [RequiredByNativeCode]
            internal static void InvokeDelegateOnEvent2(IntPtr d, MonoBehaviourLikeClassWithMagicMethods target, IntPtr a, IntPtr b)
            {
                var gcHandle = GCHandle.FromIntPtr(d);
                ((MagicMethodsDelegateVirtualProxyBase)gcHandle.Target).OnEvent2(target, a, b);
            }

            [RequiredByNativeCode]
            internal static void InvokeDelegateOnEvent3(IntPtr d, MonoBehaviourLikeClassWithMagicMethods target, IntPtr a, IntPtr b, object c)
            {
                var gcHandle = GCHandle.FromIntPtr(d);
                ((MagicMethodsDelegateVirtualProxyBase)gcHandle.Target).OnEvent3(target, a, b, c);
            }

            [RequiredByNativeCode]
            internal static IntPtr GetMagicMethodsDelegateVirtualProxy(MonoBehaviourLikeClassWithMagicMethods obj)
            {
                var invoker = (MagicMethodsDelegateVirtualProxyBase)Activator.CreateInstance(typeof(MagicMethodsDelegateVirtualProxy<>).MakeGenericType(obj.GetType()));
                return GCHandle.ToIntPtr(GCHandle.Alloc(invoker, GCHandleType.Normal));
            }
        }

        internal interface ITypeWithVirtualMethods
        {
            void OnEvent0();
            void OnEvent1(IntPtr a);
            void OnEvent2(IntPtr a, IntPtr b);
            void OnEvent3(IntPtr a, IntPtr b, object c);
        }

        [RequiredByNativeCode]
        internal abstract class PlayableBehaviourLikeClassWithVirtualMethods : ITypeWithVirtualMethods
        {
            public virtual void OnEvent0() { }
            public virtual void OnEvent1(IntPtr a) { }
            public virtual void OnEvent2(IntPtr a, IntPtr b) { }
            public virtual void OnEvent3(IntPtr a, IntPtr b, object c) { }

            [RequiredByNativeCode]
            internal static void InvokeOnEvent0(PlayableBehaviourLikeClassWithVirtualMethods obj)
            {
                obj.OnEvent0();
            }

            [RequiredByNativeCode]
            internal static void InvokeOnEvent1(PlayableBehaviourLikeClassWithVirtualMethods obj, IntPtr a)
            {
                obj.OnEvent1(a);
            }

            [RequiredByNativeCode]
            internal static void InvokeOnEvent2(PlayableBehaviourLikeClassWithVirtualMethods obj, IntPtr a, IntPtr b)
            {
                obj.OnEvent2(a, b);
            }

            [RequiredByNativeCode]
            internal static void InvokeOnEvent3(PlayableBehaviourLikeClassWithVirtualMethods obj, IntPtr a, IntPtr b, object c)
            {
                obj.OnEvent3(a, b, c);
            }

            [RequiredByNativeCode]
            [Flags]
            internal enum AvailableMethodsMask : int
            {
                None = 0,
                OnEvent0 = 1 << 0,
                OnEvent1 = 1 << 1,
                OnEvent2 = 1 << 2,
                OnEvent3 = 1 << 3,
            }

            [RequiredByNativeCode]
            internal static AvailableMethodsMask GetAvailableMethodsMaskForAllPlayableBehaviourLikeClassVirtualMethods(PlayableBehaviourLikeClassWithVirtualMethods obj)
            {
                var mask = AvailableMethodsMask.None;
                var type = obj.GetType();
                var playableBehaviourType = typeof(PlayableBehaviourLikeClassWithVirtualMethods);

                if (TypeHasOverrideMethodDeclared(type, playableBehaviourType, nameof(OnEvent0)))
                    mask |= AvailableMethodsMask.OnEvent0;
                if (TypeHasOverrideMethodDeclared(type, playableBehaviourType, nameof(OnEvent1)))
                    mask |= AvailableMethodsMask.OnEvent1;
                if (TypeHasOverrideMethodDeclared(type, playableBehaviourType, nameof(OnEvent2)))
                    mask |= AvailableMethodsMask.OnEvent2;
                if (TypeHasOverrideMethodDeclared(type, playableBehaviourType, nameof(OnEvent3)))
                    mask |= AvailableMethodsMask.OnEvent3;

                return mask;
            }
        }

        [NativeHeader("Modules/Marshalling/InvokeTests.bindings.h")]
        internal class TestMethodsInvocationPerformance
        {
            public static extern void ClearMethodsDelegatesCache();

            public static extern void CacheMagicMethods_NativeReflection(MonoBehaviourLikeClassWithMagicMethods obj, int iterations);
            public static extern void CacheMagicMethod_ManagedDelegates(MonoBehaviourLikeClassWithMagicMethods obj, int iterations);
            public static extern void TestVirtualMethods_ManagedGetMethod(PlayableBehaviourLikeClassWithVirtualMethods obj, int iterations);

            public static extern void CallMethods_NativeInvoke(object obj, int iterations);
            public static extern void CallMagicMethods_DelegateInvoke(object obj, int iterations);
            public static extern void CallVirtualMethods_VirtualInvoke(PlayableBehaviourLikeClassWithVirtualMethods obj, int iterations);
        }
    }
}

