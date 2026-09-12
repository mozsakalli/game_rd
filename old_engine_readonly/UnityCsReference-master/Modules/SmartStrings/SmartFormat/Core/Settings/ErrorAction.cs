// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

//
// Copyright SmartFormat Project maintainers and contributors.
// Licensed under the MIT license.

using System;

namespace Unity.SmartStrings.Core.Settings;

/// <summary>
/// Determines how format errors are handled.
/// </summary>
[Obsolete("Use 'ParseErrorAction' or 'FormatErrorAction' instead.", false)]
public enum ErrorAction
{
    /// <summary>Throws an exception. This is only recommended for debugging, so that formatting errors can be easily found.</summary>
    ThrowError,

    /// <summary>Includes an issue message in the output.</summary>
    OutputErrorInResult,

    /// <summary>Ignores errors and tries to output the data anyway.</summary>
    Ignore,

    /// <summary>Leaves invalid tokens unmodified in the text.</summary>
    MaintainTokens
}
