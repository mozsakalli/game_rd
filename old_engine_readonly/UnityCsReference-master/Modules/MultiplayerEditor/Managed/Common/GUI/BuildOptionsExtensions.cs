// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.Multiplayer.Internal;
using UnityEditor.Build.Profile;
using Unity.Multiplayer.Internal;
using UnityEditor.PackageManager;

namespace Unity.Multiplayer.Editor
{
    internal static partial class BuildOptionsExtensions
    {
        [AutoStaticsCleanupOnCodeReload] // holds interface implementations; may reference user-derived types
        private static List<IMultiplayerBuildOptionsSection> s_BuildOptionsSections;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            Events.registeredPackages += Reinitialize;
            if(!DedicatedServerMigrationUtility.ShouldEnableDedicatedServer())
            {
                return;
            }
            
            s_BuildOptionsSections = new List<IMultiplayerBuildOptionsSection>();

            foreach(var t in TypeCache.GetTypesDerivedFrom<IMultiplayerBuildOptionsSection>())
            {
                s_BuildOptionsSections.Add((IMultiplayerBuildOptionsSection)Activator.CreateInstance(t));
            }
            s_BuildOptionsSections.Sort((a, b) => a.Order.CompareTo(b.Order));

            EditorMultiplayerManager.drawingMultiplayerBuildOptionsForBuildProfile += OnDrawingBuildOptions;
        }

        private static void Reinitialize(PackageRegistrationEventArgs args)
        {
            Events.registeredPackages -= Reinitialize;
            EditorMultiplayerManager.drawingMultiplayerBuildOptionsForBuildProfile -= OnDrawingBuildOptions;
            Init();
        }

        private static void OnDrawingBuildOptions(BuildProfile profile)
        {
            foreach (var section in s_BuildOptionsSections)
            {
                section.DrawBuildOptions(profile);
            }
        }
    }
}
