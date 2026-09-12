// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace UnityEngine.SubsystemsImplementation
{
    public static partial class SubsystemDescriptorStore
    {
        ///<exclude />
        public static void RegisterDescriptor(SubsystemDescriptorWithProvider descriptor)
        {
            descriptor.ThrowIfInvalid();
            RegisterDescriptor(descriptor, s_StandaloneDescriptors);
        }

        internal static void GetAllSubsystemDescriptors(List<ISubsystemDescriptor> descriptors)
        {
            descriptors.Clear();
            int numDescriptors = s_IntegratedDescriptors.Count + s_StandaloneDescriptors.Count + s_DeprecatedDescriptors.Count;
            if (descriptors.Capacity < numDescriptors)
                descriptors.Capacity = numDescriptors;

            AddDescriptorSubset(s_IntegratedDescriptors, descriptors);
            AddDescriptorSubset(s_StandaloneDescriptors, descriptors);
            AddDescriptorSubset(s_DeprecatedDescriptors, descriptors);
        }

        static void AddDescriptorSubset<TBaseTypeInList>(List<TBaseTypeInList> copyFrom, List<ISubsystemDescriptor> copyTo)
            where TBaseTypeInList : ISubsystemDescriptor
        {
            foreach (var descriptor in copyFrom)
                copyTo.Add(descriptor);
        }

        internal static void GetSubsystemDescriptors<T>(List<T> descriptors)
            where T : ISubsystemDescriptor
        {
            descriptors.Clear();
            AddDescriptorSubset(s_IntegratedDescriptors, descriptors);
            AddDescriptorSubset(s_StandaloneDescriptors, descriptors);
            AddDescriptorSubset(s_DeprecatedDescriptors, descriptors);
        }

        static void AddDescriptorSubset<TBaseTypeInList, TQueryType>(List<TBaseTypeInList> copyFrom, List<TQueryType> copyTo)
            where TBaseTypeInList : ISubsystemDescriptor
            where TQueryType : ISubsystemDescriptor
        {
            foreach (var descriptor in copyFrom)
            {
                if (descriptor is TQueryType concreteDescriptor)
                    copyTo.Add(concreteDescriptor);
            }
        }

        internal static void RegisterDescriptor<TDescriptor, TBaseTypeInList>(TDescriptor descriptor, List<TBaseTypeInList> storeInList)
            where TDescriptor : TBaseTypeInList
            where TBaseTypeInList : ISubsystemDescriptor
        {
            for (int finderIndex = 0; finderIndex < storeInList.Count; ++finderIndex)
            {
                if (storeInList[finderIndex].id != descriptor.id)
                    continue;

                Debug.LogWarning($"Registering subsystem descriptor with duplicate ID '{descriptor.id}' - overwriting previous entry.");
                storeInList[finderIndex] = descriptor;
                return;
            }

            ReportSingleSubsystemAnalytics(descriptor.id);
            storeInList.Add(descriptor);
        }

        [NoAutoStaticsCleanup] // cleared manually by ClearManagedDescriptors; must not auto-clear before native ptrs are invalidated
        static List<IntegratedSubsystemDescriptor> s_IntegratedDescriptors = new List<IntegratedSubsystemDescriptor>();
        [AutoStaticsCleanupOnCodeReload]
        static List<SubsystemDescriptorWithProvider> s_StandaloneDescriptors = new List<SubsystemDescriptorWithProvider>();
#pragma warning disable CS0618
        [AutoStaticsCleanupOnCodeReload]
        static List<SubsystemDescriptor> s_DeprecatedDescriptors = new List<SubsystemDescriptor>();
#pragma warning restore CS0618
    }
}
