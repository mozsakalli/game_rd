// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Text.Json.Serialization;

namespace Unity.Multiplayer.PlayMode.Editor
{
    class VirtualProjectIdentifier
    {
        const int k_ProjectIdentifierLength = 8;

        // JSON property names kept as "m_Id"/"m_Prefix" for compatibility with data serialized by Newtonsoft.Json.
        [JsonPropertyName("m_Id")] public string Id { get; }
        [JsonPropertyName("m_Prefix")] public string Prefix { get; }

        [JsonConstructor]
        VirtualProjectIdentifier(string id, string prefix = "")
        {
            Id = id;
            Prefix = prefix;
        }

        public override string ToString()
        {
            return $"{Prefix}{Id}";
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualProjectIdentifier identifier
                   && Equals(Id, identifier.Id);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public static bool operator ==(VirtualProjectIdentifier lhs, VirtualProjectIdentifier rhs)
        {
            return ReferenceEquals(lhs, null)
                ? ReferenceEquals(rhs, null)
                : lhs.Equals(rhs);
        }

        public static bool operator !=(VirtualProjectIdentifier lhs, VirtualProjectIdentifier rhs)
        {
            return !(lhs == rhs);
        }

        public static VirtualProjectIdentifier NewVirtualProjectIdentifier(string prefix = "")
        {
            return new VirtualProjectIdentifier(GenerateShortIdentifier(), prefix);
        }

        public static bool TryParse(string input, out VirtualProjectIdentifier identifier)
        {
            identifier = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }
            if (input.Length < k_ProjectIdentifierLength)
            {
                return false;
            }

            var potentialGuid = input.Substring(input.Length - k_ProjectIdentifierLength);
            var prefix = input.Replace(potentialGuid, string.Empty);
            identifier = new VirtualProjectIdentifier(potentialGuid, prefix);
            return true;
        }

        public static string GenerateShortIdentifier()
        {
            return Guid.NewGuid().ToString("N").Substring(0, k_ProjectIdentifierLength);
        }
    }
}
