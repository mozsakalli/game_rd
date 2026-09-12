// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Linq;
using JetBrains.Annotations;
using UnityEditorInternal;
using Unity.Scripting.LifecycleManagement;

namespace UnityEditor.MPE
{
    enum DataServiceEvent
    {
        AUTO_REFRESH
    }

    static class DataService
    {
        [NoAutoStaticsCleanup] // refresh gate re-enabled per process by the AfterDomainReload role provider; value type, safe to persist
        internal static bool s_ImportRefreshEnabled = false;
        [NoAutoStaticsCleanup] // transient value-type guard, self-corrected on next refresh; safe to persist across code reload
        internal static bool s_AboutToRefresh = false;
        [NoAutoStaticsCleanup] // asset-path accumulator (strings only, no user references); nulling would break Concat in OnPostprocessAllAssets
        internal static string[] s_ImportedAssets = Array.Empty<string>();

        [UsedImplicitly]
        private class AssetEvents : AssetPostprocessor
        {
            [UsedImplicitly]
            internal static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                if (!s_ImportRefreshEnabled)
                    return;

                #pragma warning disable UAC2001 // Avoid Linq
                s_ImportedAssets = s_ImportedAssets.Concat(importedAssets).Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths).Distinct()
#pragma warning restore UAC2001
                    .ToArray();

                if (s_AboutToRefresh)
                    return;

                s_AboutToRefresh = true;
                EditorApplication.update -= EmitRefresh;
                EditorApplication.update += EmitRefresh;
            }
        }

        private static void EmitRefresh()
        {
            EditorApplication.update -= EmitRefresh;

            EventService.Emit(nameof(DataServiceEvent.AUTO_REFRESH), s_ImportedAssets);
            s_AboutToRefresh = false;
            s_ImportedAssets = Array.Empty<string>();
        }

        [UsedImplicitly, RoleProvider(ProcessLevel.Main, ProcessEvent.AfterDomainReload)]
        private static void InitializeMaster()
        {
            s_ImportRefreshEnabled = true;
        }

        [UsedImplicitly, RoleProvider(ProcessLevel.Secondary, ProcessEvent.AfterDomainReload)]
        private static void InitializeSlave()
        {
            EventService.RegisterEventHandler(nameof(DataServiceEvent.AUTO_REFRESH), (eventType, data) =>
            {
                #pragma warning disable UAC2001 // Avoid Linq
                string[] paths = data.Cast<string>().ToArray();
#pragma warning restore UAC2001
                Console.WriteLine($"Secondary process need to refresh the following assets: {String.Join(", ", paths)}");
                AssetDatabase.Refresh();
                if (Array.Exists(paths, p => p.EndsWith(".cs")))
                    EditorUtility.RequestScriptReload();
                InternalEditorUtility.RepaintAllViews();
                return paths;
            });
        }
    }
}
