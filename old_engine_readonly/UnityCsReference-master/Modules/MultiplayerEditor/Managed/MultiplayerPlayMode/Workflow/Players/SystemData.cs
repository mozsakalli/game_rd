// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unity.Multiplayer.PlayMode.Editor
{
    [Serializable]
    class SystemData
    {
        [JsonInclude]
        public bool IsMppmActive { get; internal set; }
        [JsonInclude]
        public bool IsMutePlayers { get; internal set; }

        [JsonInclude]
        [JsonRequired]
        [NonSerialized] // Unity serialization skips dictionaries; this type is only ever serialized as JSON.
        public Dictionary<int, PlayerStateJson> Data = new Dictionary<int, PlayerStateJson>();

        internal static string Serialize(ParsingSystemDelegates parsing, SystemData systemData)
        {
            return parsing.SerializeObjectFunc(systemData);
        }

        internal static bool TryDeserialize(ParsingSystemDelegates parsing, string data, out SystemData systemData)
        {
            if (string.IsNullOrEmpty(data))
            {
                systemData = null;
                return false;
            }

            try
            {
                systemData = (SystemData)parsing.DeserializeObjectFunc(data, typeof(SystemData));
            }
            catch (JsonException)
            {
                systemData = null;
            }

            return systemData != null;
        }
    }
}
