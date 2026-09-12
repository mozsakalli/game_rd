// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.AdaptivePerformance
{
    /// <summary>
    /// The built-in operation mode that prioritizes battery life over performance.
    /// </summary>
    /// <remarks>
    /// Battery mode reduces the target frame rate and enables battery-oriented scalers such as <see cref="AdaptiveOnDemandRendering"/>, <see cref="AdaptivePhysics"/>, and <see cref="AdaptiveFramerate"/>.
    /// It also lowers quality further while the player is idle. Refer to <see cref="AdaptivePerformanceIdleTimeTracker"/> for idle detection.
    /// Where the active provider supports it, battery mode requests the device's energy-efficiency mode through <see cref="Provider.IDevicePerformanceLevelControl.SetEnergyEfficiencyMode"/>.
    /// </remarks>
    [System.Serializable]
    public class AdaptivePerformanceBatteryModeProvider : IAdaptivePerformanceModeProvider
    {
        internal const string kModeName = nameof(OperationMode.BatteryMode);
        /// <summary>
        /// The name of the operation mode.
        /// </summary>
        public string ModeName => kModeName;
        /// <summary>
        /// The action the mode applies in response to the device's thermal state.
        /// </summary>
        public StateAction ThermalAction { get; private set; }
        /// <summary>
        /// The action the mode applies in response to the device's performance state.
        /// </summary>
        public StateAction PerformanceAction { get; private set; }
        /// <summary>
        /// The action the mode applies in response to the device's CPU utilization data.
        /// </summary>
        public StateAction CpuUtilizationAction { get; private set; }
        /// <summary>
        /// The action the mode applies in response to the device's GPU utilization data.
        /// </summary>
        public StateAction GpuUtilizationAction { get; private set; }

        private ThermalStateTracker m_ThermalStateTracker = new ThermalStateTracker();
        /// <summary>
        /// The Indexer that this mode adjusts scalers through.
        /// </summary>
        public AdaptivePerformanceIndexer Indexer { get; internal set; }
        /// <summary>
        /// The active Adaptive Performance settings.
        /// </summary>
        public IAdaptivePerformanceSettings Settings => Holder.Instance.Settings;
        private PerformanceStateTracker m_PerformanceStateTracker = new PerformanceStateTracker(120);
        private AdaptivePerformanceIdleTimeTracker m_IdleTimeTracker = new AdaptivePerformanceIdleTimeTracker();
        private AdaptivePerformanceScaler[] m_ScalersExcludedFromAdjustment = new AdaptivePerformanceScaler[3];
        private float m_StartingCpuFrameTime = -1f;
        private float m_StartingGpuFrameTime = -1f;

        private const int k_CaptureWindowSize = 100;
        private int m_FrameCount = 0;

        // Store original scaler references and their enabled states to restore on mode end.
        // The m_Created* flags track whether this provider created the scaler instance itself
        // (vs. found one already registered with the indexer). They're used on mode end to
        // know whether to destroy the ScriptableObject if the user didn't have it enabled
        // originally — without this, every Battery -> Normal -> Battery cycle would leak a
        // ScriptableObject (they're not GC-collected; must be explicitly destroyed).
        private AdaptiveOnDemandRendering m_AdaptiveOnDemandRenderingScaler;
        private bool m_OriginalAdaptiveOnDemandRenderingEnabled;
        private bool m_CreatedAdaptiveOnDemandRenderingScaler;
        private AdaptivePhysics m_AdaptivePhysicsScaler;
        private bool m_OriginalAdaptivePhysicsEnabled;
        private bool m_CreatedAdaptivePhysicsScaler;
        private AdaptiveFramerate m_AdaptiveFramerateScaler;
        private bool m_OriginalAdaptiveFramerateEnabled;
        private bool m_CreatedAdaptiveFramerateScaler;

        // Application.targetFrameRate captured before battery mode forces 30 fps, so it can
        // be restored on mode end. AdaptiveFramerate.OnEnabled records its own m_DefaultFPS,
        // but activation is deferred to the indexer's next Update — which runs AFTER
        // OnOperationModeStart already called SetFrameRate(30) — so the scaler captures 30 as
        // its default and its OnDisabled would otherwise pin the app at 30 fps afterwards.
        private int m_SavedTargetFrameRate = -1;

        [SerializeField]
        float m_SavingTarget = 0.1f;

        [SerializeField]
        [Min(1)]
        int m_IdleTimeThresholdInSeconds = 60;

        /// <summary>
        /// The fraction of frame time that battery mode tries to save once the target frame rate is met, in the range [0, 1].
        /// </summary>
        /// <remarks>
        /// When the application meets its target frame rate, battery mode keeps lowering quality until CPU and GPU frame times drop by this fraction of the captured baseline. For example, a value of `0.1` targets a 10% reduction. The default value is `0.1`.
        /// </remarks>
        public float SavingTarget
        {
            get { return m_SavingTarget; }
            set { m_SavingTarget = value; }
        }

        /// <summary>
        /// The number of seconds the player must be idle before battery mode applies additional idle-based scaling.
        /// </summary>
        /// <remarks>
        /// Idle detection depends on the input backend. Refer to <see cref="AdaptivePerformanceIdleTimeTracker"/> for details. The default value is `60`.
        /// </remarks>
        public int IdleTimeThresholdInSeconds
        {
            get { return m_IdleTimeThresholdInSeconds; }
            set { m_IdleTimeThresholdInSeconds = value; }
        }

        /// <summary>
        /// Called when battery mode becomes the active operation mode. Prepares the Indexer and enables the battery-oriented scalers.
        /// </summary>
        public void OnOperationModeStart()
        {
            if (Settings == null || Indexer == null)
                return;
            Holder.Instance.Subsystem?.PerformanceLevelControl?.SetEnergyEfficiencyMode(true);
            Settings.IndexerOperationMode = OperationMode.BatteryMode;
            m_IdleTimeTracker.ResetIdleTime();
            Indexer.ResetIdleScale();

            m_AdaptiveOnDemandRenderingScaler = GetOrCreateScaler<AdaptiveOnDemandRendering>(out m_CreatedAdaptiveOnDemandRenderingScaler);
            m_OriginalAdaptiveOnDemandRenderingEnabled = m_AdaptiveOnDemandRenderingScaler.Enabled;
            m_AdaptiveOnDemandRenderingScaler.Enabled = true;
            m_ScalersExcludedFromAdjustment[0] = m_AdaptiveOnDemandRenderingScaler;

            m_AdaptivePhysicsScaler = GetOrCreateScaler<AdaptivePhysics>(out m_CreatedAdaptivePhysicsScaler);
            m_OriginalAdaptivePhysicsEnabled = m_AdaptivePhysicsScaler.Enabled;
            m_AdaptivePhysicsScaler.Enabled = true;
            m_ScalersExcludedFromAdjustment[1] = m_AdaptivePhysicsScaler;

            m_AdaptiveFramerateScaler = GetOrCreateScaler<AdaptiveFramerate>(out m_CreatedAdaptiveFramerateScaler);
            m_OriginalAdaptiveFramerateEnabled = m_AdaptiveFramerateScaler.Enabled;
            m_AdaptiveFramerateScaler.Enabled = true;
            m_SavedTargetFrameRate = Application.targetFrameRate;
            m_AdaptiveFramerateScaler.SetFrameRate(30);
            m_ScalersExcludedFromAdjustment[2] = m_AdaptiveFramerateScaler;
            ResetSavingsBaseline();
        }

        /// <summary>
        /// Called when battery mode is replaced by another operation mode. Restores the scalers it enabled and releases the device's energy-efficiency hint.
        /// </summary>
        public void OnOperationModeEnd()
        {
            if (Settings == null || Indexer == null)
                return;

            // Release hardware power-efficiency hint (e.g. APerformanceHint_setPreferPowerEfficiency(false) on Android ADPF).
            // Without this, the device's CPU/GPU hint sessions remain biased toward power efficiency even after
            // returning to Normal Mode, throttling performance indefinitely.
            Holder.Instance.Subsystem?.PerformanceLevelControl?.SetEnergyEfficiencyMode(false);

            Indexer?.ResetIdleScale();

            // Fully reset each scaler the battery provider force-enabled in OnOperationModeStart.
            // This restores the indexer's bookkeeping AND the scaler's output state without abusing
            // the activation lifecycle (the previous Deactivate()/ResetLevel() approach left the
            // indexer's m_AppliedScalers list desynced and ran OnDisabled() without a paired OnEnabled()).
            ResetForcedScaler(ref m_AdaptiveOnDemandRenderingScaler, m_OriginalAdaptiveOnDemandRenderingEnabled, ref m_CreatedAdaptiveOnDemandRenderingScaler);
            ResetForcedScaler(ref m_AdaptivePhysicsScaler, m_OriginalAdaptivePhysicsEnabled, ref m_CreatedAdaptivePhysicsScaler);
            ResetForcedScaler(ref m_AdaptiveFramerateScaler, m_OriginalAdaptiveFramerateEnabled, ref m_CreatedAdaptiveFramerateScaler);

            // Restore the frame rate that was in effect before battery mode forced 30 fps.
            // Must run after ResetForcedScaler: AdaptiveFramerate.OnDisabled (fired by
            // RemoveScaler) restores its polluted m_DefaultFPS (30), so override it here.
            Application.targetFrameRate = m_SavedTargetFrameRate;
        }

        // Switching the profile will likely override the scaler settings in the indexer.
        // Re-apply the battery-mode force-enable after a scaler profile switch.
        internal void OnScalerProfileChanged()
        {
            if (Settings == null || Indexer == null)
                return;

            if (m_AdaptiveOnDemandRenderingScaler != null)
            {
                m_OriginalAdaptiveOnDemandRenderingEnabled = m_AdaptiveOnDemandRenderingScaler.BatteryModeSetting.enabled;
                m_AdaptiveOnDemandRenderingScaler.Enabled = true;
                m_ScalersExcludedFromAdjustment[0] = m_AdaptiveOnDemandRenderingScaler;
            }
            if (m_AdaptivePhysicsScaler != null)
            {
                m_OriginalAdaptivePhysicsEnabled = m_AdaptivePhysicsScaler.BatteryModeSetting.enabled;
                m_AdaptivePhysicsScaler.Enabled = true;
                m_ScalersExcludedFromAdjustment[1] = m_AdaptivePhysicsScaler;
            }
            if (m_AdaptiveFramerateScaler != null)
            {
                m_OriginalAdaptiveFramerateEnabled = m_AdaptiveFramerateScaler.BatteryModeSetting.enabled;
                m_AdaptiveFramerateScaler.Enabled = true;
                m_AdaptiveFramerateScaler.SetFrameRate(30);
                m_ScalersExcludedFromAdjustment[2] = m_AdaptiveFramerateScaler;
            }
        }

        // Resets a scaler that the battery provider force-enabled in OnOperationModeStart.
        // RemoveScaler fires OnDisabled once to restore captured defaults; we then park the
        // scaler in m_DisabledScalers via AddDisabledScaler. If originalEnabled is true the
        // indexer's next Update (ActivateEnabledScalers) re-enables it under the incoming
        // mode — calling EnableScaler here instead would fire OnEnabled while
        // IndexerOperationMode is still BatteryMode (OnOperationModeStart of the new mode
        // hasn't run yet), causing scalers like AdaptiveFramerate to capture BatteryMode
        // bounds and pin the device at e.g. 30 fps after returning to NormalMode.
        // AddDisabledScaler is used (not AddScaler) to avoid a second OnDisabled via the
        // DeactivateDisabledScalers migration path, which would bypass warmup guards.
        // If we own the instance, destroy the ScriptableObject — the user never configured
        // it; leaving it would leak across Battery -> Normal -> Battery cycles.
        void ResetForcedScaler<T>(ref T scaler, bool originalEnabled, ref bool weCreatedIt)
            where T : AdaptivePerformanceScaler
        {
            if (scaler == null)
                return;

            scaler.RemoveScaler();
            scaler.Enabled = originalEnabled;

            if (weCreatedIt)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(scaler);
                else
                    UnityEngine.Object.DestroyImmediate(scaler);
                scaler = null;
                weCreatedIt = false;
            }
            else if (Indexer != null)
            {
                Indexer.AddDisabledScaler(scaler);
            }
        }

        T GetOrCreateScaler<T>(out bool created) where T : AdaptivePerformanceScaler
        {
            var scaler = Indexer.GetScalerByType<T>();
            if (scaler != null)
            {
                created = false;
                return scaler;
            }

            // Create new scaler instance if not found in indexer.
            scaler = ScriptableObject.CreateInstance<T>();
            scaler.InitializeScaler();
            created = true;
            return scaler;
        }

        /// <summary>
        /// Applies battery mode's idle, thermal, and performance actions to the active scalers. Adaptive Performance calls this method every frame while battery mode is active.
        /// </summary>
        public void ApplyModeActions()
        {
            if (Settings == null || Indexer == null)
                return;

            m_IdleTimeTracker.UpdateIdleTime(Time.deltaTime);
            var idleTimeThreshold = Mathf.Max(1, IdleTimeThresholdInSeconds);
            var idleTime = m_IdleTimeTracker.GetIdleTime();
            var idleScale = Mathf.Max(1, Mathf.CeilToInt(idleTime / idleTimeThreshold));
            Indexer.UpdateIdleState(idleScale, idleTime);
            var effectiveTargetFrameRate = GetEffectiveTargetFrameRate();
            var targetFrameTime = effectiveTargetFrameRate > 0f ? 1f / effectiveTargetFrameRate : 0f;
            var frameTiming = Holder.Instance.PerformanceStatus.FrameTiming;

            // If we meet the frame target, keep reducing quality until the requested savings target is reached.
            // Sampling starts when the targeted fps is met consistently for a fixed number of frames to avoid
            // capturing a baseline during a temporary performance spike. This is useful during menu displaying.
            // Developers could activate this mode when displaying menus and set a saving target.
            if(frameTiming.AverageFrameTime <= targetFrameTime || Holder.Instance.PerformanceStatus.PerformanceMetrics.LowPowerMode) {
                if(m_FrameCount <= k_CaptureWindowSize)
                {
                    if(m_FrameCount == k_CaptureWindowSize)
                    {
                        CaptureSavingsBaseline(frameTiming);
                    }
                    m_FrameCount++;
                }
                else{
                    var batterySavingRequested = !HasReachedSavingTarget(frameTiming);
                    PerformanceAction = batterySavingRequested
                        ? StateAction.Decrease
                        : StateAction.Stale;
                }
            }
            else
            {
                ResetSavingsBaseline();
                PerformanceAction = m_PerformanceStateTracker.Update();
            }
            // If we are still having thermal issues in low battery mode, we should lower settings to cool the device first, so
            // thermal action and performance action take the normal mode priority, but we keep lowering performance until we meet estimated target
            // if we are in low power mode even if there is no thermal issue.
            ThermalAction = m_ThermalStateTracker.Update();
            Indexer.AdjustScalersBasedOnStateAction(ThermalAction, PerformanceAction, false, m_ScalersExcludedFromAdjustment);
        }

        internal void ResetSavingsBaseline()
        {
            m_FrameCount = 0;
            m_StartingCpuFrameTime = -1f;
            m_StartingGpuFrameTime = -1f;
        }

        void CaptureSavingsBaseline(FrameTiming frameTiming)
        {
            if (m_StartingCpuFrameTime < 0f)
            {
                if (frameTiming.AverageCpuFrameTime > 0f)
                    m_StartingCpuFrameTime = frameTiming.AverageCpuFrameTime;
                else
                    // If frame timing is unavailable, set a large baseline so savings target is always met
                    m_StartingCpuFrameTime = float.MaxValue;
            }

            if (m_StartingGpuFrameTime < 0f)
            {
                if (frameTiming.AverageGpuFrameTime > 0f)
                    m_StartingGpuFrameTime = frameTiming.AverageGpuFrameTime;
                else
                    // If frame timing is unavailable, set a large baseline so savings target is always met
                    m_StartingGpuFrameTime = float.MaxValue;
            }

            Debug.Log("starting cpu frame is " + m_StartingCpuFrameTime + "starting gpu time frame is " + m_StartingGpuFrameTime);
        }

        bool HasReachedSavingTarget(FrameTiming frameTiming)
        {
            var cpuTimingAvailable = frameTiming.AverageCpuFrameTime > 0f;
            var gpuTimingAvailable = frameTiming.AverageGpuFrameTime > 0f;

            // If timing is unavailable or baseline not captured yet, consider target reached to avoid infinite decrease
            var cpuTargetReached = !cpuTimingAvailable || m_StartingCpuFrameTime <= 0f || frameTiming.AverageCpuFrameTime <= m_StartingCpuFrameTime * (1f - SavingTarget);
            var gpuTargetReached = !gpuTimingAvailable || m_StartingGpuFrameTime <= 0f || frameTiming.AverageGpuFrameTime <= m_StartingGpuFrameTime * (1f - SavingTarget);

            return cpuTargetReached && gpuTargetReached;
        }

        private float GetEffectiveTargetFrameRate()
        {
            return AdaptivePerformanceManager.EffectiveTargetFrameRate();
        }


    }
}
