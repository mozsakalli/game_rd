// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unity.Multiplayer.PlayMode.Editor
{
    [Serializable]
    class PlayerIdentifier
    {
        public Guid Guid { get; internal set; }

        // ensure that there is always a guid since guid is a struct
        [JsonConstructor]
        public PlayerIdentifier(Guid guid)
        {
            Guid = guid;
        }

        public static PlayerIdentifier New()
        {
            return new PlayerIdentifier(Guid.NewGuid());
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerIdentifier identifier
                   && Equals(Guid, identifier.Guid);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Guid);
        }

        public static bool TryParse(string input, out PlayerIdentifier identifier)
        {
            identifier = null;

            if (string.IsNullOrWhiteSpace(input)) return false;

            identifier = TryDeserializeObject(input);
            return identifier != null;
        }

        public override string ToString()
        {
            return $"Guid: {Guid}";
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        static PlayerIdentifier TryDeserializeObject(string input)
        {
            try
            {
                // Newtonsoft marked Guid as Required.Always; reject payloads without one to keep that behavior.
                var identifier = JsonSerializer.Deserialize<PlayerIdentifier>(input);
                return identifier == null || identifier.Guid == Guid.Empty ? null : identifier;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static bool operator ==(PlayerIdentifier lhs, PlayerIdentifier rhs)
        {
            return ReferenceEquals(lhs, null)
                ? ReferenceEquals(rhs, null)
                : lhs.Equals(rhs);
        }

        public static bool operator !=(PlayerIdentifier lhs, PlayerIdentifier rhs)
        {
            return !(lhs == rhs);
        }
    }
}
