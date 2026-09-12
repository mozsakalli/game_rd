// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.AdaptivePerformance
{
    /// <summary>
    /// The default operation mode that prioritizes the highest achievable frame rate.
    /// </summary>
    /// <remarks>
    /// Normal mode adjusts scalers based only on the device's thermal and performance state. It's the active mode unless you switch to another mode through <see cref="IOperationModeStatus.CurrentOperationalModeProvider"/>.
    /// </remarks>
    [System.Serializable]
    public class AdaptivePerformanceNormalModeProvider : IAdaptivePerformanceModeProvider
    {
        internal const string kModeName = nameof(OperationMode.NormalMode);
        private ThermalStateTracker m_ThermalStateTracker;
        private PerformanceStateTracker m_PerformanceStateTracker;
        internal PerformanceStateTracker PerformanceStateTracker
        {
            get => m_PerformanceStateTracker;
            set => m_PerformanceStateTracker = value;
        }
        private UtilizationStateTracker m_CpuUtilizationTracker;
        private UtilizationStateTracker m_GpuUtilizationTracker;
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
        /// <summary>
        /// The Indexer that this mode adjusts scalers through.
        /// </summary>
        public AdaptivePerformanceIndexer Indexer { get; internal set; }
        /// <summary>
        /// The active Adaptive Performance settings.
        /// </summary>
        public IAdaptivePerformanceSettings Settings => Holder.Instance.Settings;
        /// <summary>
        /// Called when normal mode becomes the active operation mode.
        /// </summary>
        public void OnOperationModeStart()
        {
            Settings.IndexerOperationMode = OperationMode.NormalMode;
        }

        /// <summary>
        /// Called when normal mode is replaced by another operation mode.
        /// </summary>
        public void OnOperationModeEnd()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AdaptivePerformanceNormalModeProvider"/> class.
        /// </summary>
        public AdaptivePerformanceNormalModeProvider()
        {
            m_ThermalStateTracker = new ThermalStateTracker();
            m_PerformanceStateTracker = new PerformanceStateTracker(120);
            m_CpuUtilizationTracker = new UtilizationStateTracker(
                () => Holder.Instance?.PerformanceStatus.PerformanceMetrics.CpuUtilization ?? -1f);
            m_GpuUtilizationTracker = new UtilizationStateTracker(
                 () => Holder.Instance?.PerformanceStatus.PerformanceMetrics.GpuUtilization ?? -1f);
        }

        internal StateAction MostPressingAction(StateAction action1, StateAction action2, StateAction action3)
        {
            if (action1 == StateAction.FastDecrease ||
                action2 == StateAction.FastDecrease ||
                action3 == StateAction.FastDecrease)
            {
                return StateAction.FastDecrease;
            }

            if (action1 == StateAction.Decrease ||
                action2 == StateAction.Decrease ||
                action3 == StateAction.Decrease)
            {
                return StateAction.Decrease;
            }

            if (action1 == StateAction.Increase ||
                action2 == StateAction.Increase ||
                action3 == StateAction.Increase)
            {
                return StateAction.Increase;
            }

            return StateAction.Stale;
        }

        /// <summary>
        /// Applies normal mode's thermal and performance actions to the active scalers. Adaptive Performance calls this method every frame while normal mode is active.
        /// </summary>
        public void ApplyModeActions()
        {
            var thermalAction = m_ThermalStateTracker.Update();
            var performanceAction = m_PerformanceStateTracker.Update();
            var cpuUtilizationAction = m_CpuUtilizationTracker.Update();
            var gpuUtilizationAction = m_GpuUtilizationTracker.Update();

            ThermalAction = thermalAction;
            PerformanceAction = performanceAction;
            CpuUtilizationAction = cpuUtilizationAction;
            GpuUtilizationAction = gpuUtilizationAction;
            StateAction combinedPerformanceAction = MostPressingAction(PerformanceAction, CpuUtilizationAction, GpuUtilizationAction);

            // Enforce minimum wait time between any scaler changes
            Indexer.AdjustScalersBasedOnStateAction(thermalAction, combinedPerformanceAction, true, null);
        }
    }
}
