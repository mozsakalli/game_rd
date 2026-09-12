// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unity.Multiplayer.PlayMode.Editor
{
    [Serializable]
    struct CloneData
    {
        const LayoutFlags k_DefaultLayout = LayoutFlags.GameView | LayoutFlags.ConsoleWindow;

        [JsonInclude]
        public LayoutFlags EditModeLayoutFlags;
        [JsonInclude]
        public LayoutFlags PlayModeLayoutFlags;

        public override string ToString()
        {
            return $"{nameof(EditModeLayoutFlags)}: {EditModeLayoutFlags}, {nameof(PlayModeLayoutFlags)}: {PlayModeLayoutFlags}";
        }

        public static CloneData NewDefault()
        {
            return new CloneData
            {
                EditModeLayoutFlags = k_DefaultLayout,
                PlayModeLayoutFlags = k_DefaultLayout,
            };
        }

        public static string Serialize(CloneData data)
        {
            return JsonSerializer.Serialize(data, ParsingSystem.SerializerOptions);
        }

        public static bool TryDeserialize(string data, out CloneData cloneData)
        {
            if (string.IsNullOrEmpty(data))
            {
                cloneData = NewDefault();
                return false;
            }

            try
            {
                cloneData = JsonSerializer.Deserialize<CloneData>(data, ParsingSystem.SerializerOptions);
                return true;
            }
            catch (JsonException)
            {
                cloneData = NewDefault();
                return false;
            }
        }
    }
}
