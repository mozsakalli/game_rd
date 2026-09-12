// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.IO;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;

namespace Unity.Multiplayer.PlayMode.Editor
{
    struct SerializeMessageDelegates
    {   // This simply represents the static methods for serializing objects of any message type
        internal delegate void Serialize(BinaryWriter writer, object value);
        internal delegate object Deserialize(BinaryReader reader);

        internal Serialize SerializeFunc;
        internal Deserialize DeserializeFunc;
    }
    class SerializeMessageDelegatesAttribute : Attribute
    {
    }

    static partial class SerializeMessageMapping
    {
        [AutoStaticsCleanupOnCodeReload] // filled by reflection at startup; Type keys stale after reload
        public static Dictionary<Type, SerializeMessageDelegates> SerializeMessageDelegatesMap = new Dictionary<Type, SerializeMessageDelegates>();

        [OnCodeLoaded]
        static void Initialize()
        {
            var methods = TypeCache.GetMethodsWithAttribute<SerializeMessageDelegatesAttribute>();
            Debug.Assert(methods.Count > 0, "No methods with message serialization.");
            foreach (var methodInfo in methods)
            {
                Debug.Assert(methodInfo != null);
                Debug.Assert(methodInfo.ReflectedType != null);
                var result = (SerializeMessageDelegates)methodInfo.Invoke(new object(), Array.Empty<object>());
                Debug.Assert(result.SerializeFunc != null);
                Debug.Assert(result.DeserializeFunc != null);
                SerializeMessageDelegatesMap[methodInfo.ReflectedType] = result;
            }
        }
    }
}
