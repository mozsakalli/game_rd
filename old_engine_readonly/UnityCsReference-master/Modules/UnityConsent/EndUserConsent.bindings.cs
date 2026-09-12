// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine.UnityConsent
{
    /// <summary>
    /// Provides methods and events to manage and track the user's consent state.
    /// </summary>
    [NativeHeader("Modules/UnityConsent/EndUserConsent.h")]
    public static partial class EndUserConsent
    {
        /// <summary>
        /// Retrieves the current consent state of the user.
        /// </summary>
        /// <returns>
        /// The current <see cref="ConsentState"/> of the user.
        /// </returns>
        [NativeMethod("GetConsentStateStatic")]
        public extern static ConsentState GetConsentState();

        /// <summary>
        /// Updates the consent state of the user.
        /// </summary>
        /// <param name="consentState">The new <see cref="ConsentState"/> to set.</param>
        [NativeMethod("SetConsentStateStatic")]
        public extern static void SetConsentState(ConsentState consentState);

        /// <summary>
        /// Occurs when the consent state of the user changes. The argument is the new consent state.
        /// </summary>
        [AutoStaticsCleanupOnCodeReload]
        public static event Action<ConsentState> consentStateChanged;

        [RequiredByNativeCode]
        static void OnConsentStateChanged()
        {
            if (consentStateChanged != null)
            {
                consentStateChanged(GetConsentState());
            }
        }
    }
}
