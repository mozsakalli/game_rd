// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.AdaptivePerformance
{
    /// <summary>
    /// Represents an operation mode that applies thermal and performance actions to Adaptive Performance scalers.
    /// </summary>
    /// <remarks>
    /// Implement this interface to create a custom operation mode, then assign an instance to <see cref="IOperationModeStatus.CurrentOperationalModeProvider"/> to make it the active mode.
    /// Adaptive Performance calls <see cref="ApplyModeActions"/> every frame while the provider is active.
    /// Assigning a custom provider replaces the strategy for the active operation mode but doesn't change the operation mode itself, so the provider uses the scaler settings of whichever mode the Indexer is currently in.
    /// </remarks>
    public interface IAdaptivePerformanceModeProvider
    {
        /// <summary>
        /// The name of the operation mode.
        /// </summary>
        string ModeName { get; }
        /// <summary>
        /// The action the mode applies in response to the device's thermal state.
        /// </summary>
        StateAction ThermalAction { get; }
        /// <summary>
        /// The action the mode applies in response to the device's performance state.
        /// </summary>
        StateAction PerformanceAction { get; }
        /// <summary>
        /// The action the mode applies in response to the device's CPU utilization data.
        /// </summary>
        StateAction CpuUtilizationAction { get; }
        /// <summary>
        /// The action the mode applies in response to the device's GPU utilization data.
        /// </summary>
        StateAction GpuUtilizationAction { get; }
        /// <summary>
        /// Applies the mode's thermal and performance actions to the active scalers. Adaptive Performance calls this method every frame while the mode is active.
        /// </summary>
        void ApplyModeActions();
        /// <summary>
        /// Called when the provider becomes the active operation mode.
        /// </summary>
        void OnOperationModeStart();
        /// <summary>
        /// Called when the provider is disabled or replaced by another operation mode provider.
        /// </summary>
        void OnOperationModeEnd();
    }
}
