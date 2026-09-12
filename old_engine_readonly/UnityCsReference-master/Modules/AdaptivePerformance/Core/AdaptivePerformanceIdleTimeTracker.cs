// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;

namespace UnityEngine.AdaptivePerformance
{
    /// <summary>
    /// Tracks how long the player has been idle so battery mode can reduce quality further during inactivity.
    /// </summary>
    /// <remarks>
    /// By default, idle detection uses the legacy Input Manager (`Input.anyKey` and `Input.mousePositionDelta`).
    /// If the legacy Input Manager is disabled, idle detection stops and battery mode's idle-based scaling becomes inactive.
    /// To re-enable it, assign <see cref="IsAnyKeyPressedProvider"/> and <see cref="IsCursorMovingProvider"/> with implementations backed by your input system, such as the Input System package.
    /// </remarks>
    public class AdaptivePerformanceIdleTimeTracker
    {
        private float m_PlayerIdleTime = 0.0f;

        // True once a default provider's try/catch caught InvalidOperationException
        // (legacy Input Manager unavailable). Used together with s_AnyCustomProviderAssigned
        // to decide whether to freeze m_PlayerIdleTime in UpdateIdleTime — without an
        // input source, both true and false answers from the providers would corrupt
        // idle accounting in different directions.
        [NoAutoStaticsCleanup]
        static bool s_LegacyInputUnavailable = false;
        [NoAutoStaticsCleanup]
        static bool s_LegacyInputUnavailableWarned = false;

        // Tracks whether the developer has assigned a custom IsAnyKeyPressedProvider,
        // IsCursorMovingProvider, or IsUserActiveProvider. Cached at setter time so
        // UpdateIdleTime doesn't pay reflection (Delegate.Method.DeclaringType) every frame.
        [NoAutoStaticsCleanup]
        static bool s_AnyCustomProviderAssigned = false;

        // Cached default delegate instances so identity comparisons in
        // RefreshCustomProviderFlag don't create a new delegate on every setter call.
        [NoAutoStaticsCleanup]
        static readonly Func<bool> s_DefaultIsAnyKeyPressed = DefaultIsAnyKeyPressed;
        [NoAutoStaticsCleanup]
        static readonly Func<bool> s_DefaultIsCursorMoving = DefaultIsCursorMoving;
        [NoAutoStaticsCleanup]
        static readonly Func<bool> s_DefaultIsUserActive = DefaultIsUserActive;

        [NoAutoStaticsCleanup]
        static Func<bool> s_IsAnyKeyPressedProvider = s_DefaultIsAnyKeyPressed;
        [NoAutoStaticsCleanup]
        static Func<bool> s_IsCursorMovingProvider = s_DefaultIsCursorMoving;
        [NoAutoStaticsCleanup]
        static Func<bool> s_IsUserActiveProvider = s_DefaultIsUserActive;

        /// <summary>
        /// Provider used to determine if any key or button is pressed. The default (legacy Input.anyKey) covers keyboard keys, mouse buttons, and controller buttons. Controller analog sticks and triggers are project-defined and not detected by the default; assign a custom provider (for example, backed by the Input System) to include them.
        /// </summary>
        public static Func<bool> IsAnyKeyPressedProvider
        {
            get => s_IsAnyKeyPressedProvider;
            set
            {
                s_IsAnyKeyPressedProvider = value ?? s_DefaultIsAnyKeyPressed;
                RefreshCustomProviderFlag();
            }
        }

        /// <summary>
        /// Provider used to determine if the pointer was active this frame (cursor movement or an active touch). Assign to override the default (legacy Input.mousePositionDelta and Input.touchCount).
        /// </summary>
        public static Func<bool> IsCursorMovingProvider
        {
            get => s_IsCursorMovingProvider;
            set
            {
                s_IsCursorMovingProvider = value ?? s_DefaultIsCursorMoving;
                RefreshCustomProviderFlag();
            }
        }

        /// <summary>
        /// Optional developer-defined activity condition, checked in addition to key and cursor input. Return true to signal user activity from a source not covered by the built-in checks (for example, gamepad analog sticks via the Input System, or application-specific activity). Defaults to a condition that reports no activity.
        /// </summary>
        public static Func<bool> IsUserActiveProvider
        {
            get => s_IsUserActiveProvider;
            set
            {
                s_IsUserActiveProvider = value ?? s_DefaultIsUserActive;
                RefreshCustomProviderFlag();
            }
        }

        static void RefreshCustomProviderFlag()
        {
            s_AnyCustomProviderAssigned =
                !ReferenceEquals(s_IsAnyKeyPressedProvider, s_DefaultIsAnyKeyPressed) ||
                !ReferenceEquals(s_IsCursorMovingProvider, s_DefaultIsCursorMoving) ||
                !ReferenceEquals(s_IsUserActiveProvider, s_DefaultIsUserActive);
        }

        static bool DefaultIsAnyKeyPressed()
        {
            try
            {
                // Covers keyboard, mouse buttons, and controller buttons (all map to
                // KeyCodes). Controller analog sticks/triggers use project-defined
                // axis names that can't be enumerated through legacy Input, so they
                // are intentionally left to a custom IsAnyKeyPressedProvider.
                return UnityEngine.Input.anyKey;
            }
            catch (System.InvalidOperationException)
            {
                MarkLegacyInputUnavailable();
                return false;
            }
        }

        static bool DefaultIsCursorMoving()
        {
            try
            {
                return UnityEngine.Input.mousePositionDelta.sqrMagnitude > 0f;
            }
            catch (System.InvalidOperationException)
            {
                MarkLegacyInputUnavailable();
                return false;
            }
        }

        // No extra activity source by default; developers opt in via IsUserActiveProvider.
        static bool DefaultIsUserActive() => false;

        static void MarkLegacyInputUnavailable()
        {
            s_LegacyInputUnavailable = true;
            if (!s_LegacyInputUnavailableWarned)
            {
                s_LegacyInputUnavailableWarned = true;
                Debug.LogWarning("[AdaptivePerformance] Legacy Input Manager is disabled. Idle-time-based Battery Mode scaling is now inactive. Assign AdaptivePerformanceIdleTimeTracker.IsAnyKeyPressedProvider and IsCursorMovingProvider with implementations backed by your input system (e.g. Unity Input System) to re-enable idle detection.");
            }
        }

        // Defense-in-depth null guards. The setters null-coalesce to the cached default
        // delegates, so these fields shouldn't be null in normal use; ?? false guards
        // against external mutation (reflection, serialization edge cases) so this
        // property never throws NRE.
        /// <summary>
        /// Whether any key or button (keyboard, mouse, or controller button) is currently pressed. Uses <see cref="IsAnyKeyPressedProvider"/>.
        /// </summary>
        public static bool IsAnyKeyPressed => s_IsAnyKeyPressedProvider?.Invoke() ?? false;

        /// <summary>
        /// Whether the pointer was active this frame (cursor movement or an active touch). Uses <see cref="IsCursorMovingProvider"/>.
        /// </summary>
        public static bool IsCursorMoving => s_IsCursorMovingProvider?.Invoke() ?? false;

        /// <summary>
        /// Whether the developer-defined activity condition reports activity this frame. Uses <see cref="IsUserActiveProvider"/>.
        /// </summary>
        public static bool IsUserActive => s_IsUserActiveProvider?.Invoke() ?? false;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdaptivePerformanceIdleTimeTracker"/> class.
        /// </summary>
        public AdaptivePerformanceIdleTimeTracker()
        {
        }

        internal void UpdateIdleTime(float deltaTime)
        {
            // Freeze m_PlayerIdleTime at 0 if legacy Input is unavailable AND the developer
            // hasn't supplied a custom provider. Both true and false defaults would corrupt
            // idle accounting (true -> resets every frame; false -> accumulates forever),
            // so battery mode's idle-driven scaling becomes a no-op until a real provider
            // is wired up. The rest of battery mode still works.
            if (s_LegacyInputUnavailable && !s_AnyCustomProviderAssigned)
            {
                m_PlayerIdleTime = 0;
                return;
            }

            if (IsAnyKeyPressed || IsCursorMoving || IsUserActive)
            {
                m_PlayerIdleTime = 0;
            }
            else
            {
                m_PlayerIdleTime += deltaTime;
            }
        }

        internal void ResetIdleTime()
        {
            m_PlayerIdleTime = 0 ;
        }

        internal float GetIdleTime()
        {
            return m_PlayerIdleTime;
        }
    }
}

