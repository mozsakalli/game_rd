// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Text.Json;
using Unity.Scripting.LifecycleManagement;

namespace Unity.Multiplayer.PlayMode.Editor
{
    struct ParsingSystemDelegates
    {   // This simply represents the static methods of System.Text.Json
        public delegate object DeserializeObject(string data, Type type);
        public delegate string SerializeObject(object data);

        public DeserializeObject DeserializeObjectFunc;
        public SerializeObject SerializeObjectFunc;
    }

    static class ParsingSystem
    {
        [NoAutoStaticsCleanup] // immutable serializer options; safe to persist across reload
        public static JsonSerializerOptions SerializerOptions { get; } = new JsonSerializerOptions { WriteIndented = true };

        [NoAutoStaticsCleanup] // delegates only wrap System.Text.Json calls; no ALC pinning concern
        public static ParsingSystemDelegates Delegates { get; } = new ParsingSystemDelegates
        {
            SerializeObjectFunc = data => JsonSerializer.Serialize(data, SerializerOptions),
            DeserializeObjectFunc = (data, type) => JsonSerializer.Deserialize(data, type, SerializerOptions),
        };
    }
}
