// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Unity.Multiplayer.PlayMode.Editor
{
    [Serializable]
    struct PlayerTagsData
    {
        [JsonInclude]
        public List<string> PlayerTags;
        [JsonInclude]
        public string version;
    }
}
