// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

// Contains code shared by buildsystem programs and player build programs
using System;
using NiceIO;

namespace UnityEditorInternal
{
    internal class BuildEditorShared
    {
        public static NPath GetModulePlatformResourcesDirectory(NPath platformBuildDirectory)
        {
            return platformBuildDirectory.Combine("Modules");
        }
    }

    [Serializable]
    internal class ModulePlatformResources
    {
        [Serializable]
        internal class ModuleDependency
        {
            public string Name = null;

            /// <summary>
            /// In case of Install To Builds folder option, this can be used to reference source directly.
            /// </summary>
            public string SourceLocation = null;

            /// <summary>
            /// Optional inclusion condition. When null or empty, the dependency is included whenever
            /// the owning module is used. When non-empty, the dependency is included only when at least
            /// one of these features (managed API method references) is used by the application.
            /// </summary>
            public string[] Features = null;
        }

        [Serializable]
        internal class ModuleInformation
        {
            public string Name = null;
            public ModuleDependency[] Dependencies = null;

            public override string ToString()
            {
                return $"{Name} ({Dependencies?.Length})";
            }
        }

        public ModuleInformation[] Values = null;
    }
}
