// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Assemblies;

namespace UnityEngineInternal
{
    public sealed class APIUpdaterRuntimeServices
    {

        [Obsolete(@"AddComponent(string) has been deprecated. Use GameObject.AddComponent<T>() / GameObject.AddComponent(Type) instead.
API Updater could not automatically update the original call to AddComponent(string name), because it was unable to resolve the type specified in parameter 'name'.
Instead, this call has been replaced with a call to APIUpdaterRuntimeServices.AddComponent() so you can try to test your game in the editor.
In order to be able to build the game, replace this call (APIUpdaterRuntimeServices.AddComponent()) with a call to GameObject.AddComponent<T>() / GameObject.AddComponent(Type).")]
        public static Component AddComponent(GameObject go, string sourceInfo, string name)
        {
            Debug.LogWarningFormat("Performing a potentially slow search for component {0}.", name);

            var type = ResolveType(name, Assembly.GetCallingAssembly(), sourceInfo);
            return type == null
                ? null
                : go.AddComponent(type);
        }

        private static Type ResolveType(string name, Assembly callingAssembly, string sourceInfo)
        {
#pragma warning disable UAC2001 // Avoid Linq
            var foundOnUnityEngine = ComponentsFromUnityEngine.FirstOrDefault(t => (t.Name == name || t.FullName == name) && !IsMarkedAsObsolete(t));
#pragma warning restore UAC2001
            if (foundOnUnityEngine != null)
            {
                Debug.LogWarningFormat("[{1}] Component type '{0}' found in UnityEngine, consider replacing with go.AddComponent<{0}>()", name, sourceInfo);
                return foundOnUnityEngine;
            }

            var candidateType = callingAssembly.GetType(name);
            if (candidateType != null)
            {
                Debug.LogWarningFormat("[{1}] Component type '{0}' found on caller assembly, consider replacing with go.AddComponent<{0}>()", name, sourceInfo);
                return candidateType;
            }

#pragma warning disable RS0030 // Avoid Linq
#pragma warning disable UAC2001 // Avoid Linq
            candidateType = CurrentAssemblies.GetLoadedAssemblies().SelectMany(GetTypesFromAssembly).SingleOrDefault(t => (t.Name == name || t.FullName == name) && typeof(Component).IsAssignableFrom(t));
#pragma warning restore RS0030
#pragma warning restore UAC2001
            if (candidateType != null)
            {
                Debug.LogWarningFormat("[{2}] Component type '{0}' found on assembly {1}, consider replacing with go.AddComponent<{0}>()", name, new AssemblyName(candidateType.Assembly.FullName).Name, sourceInfo);
                return candidateType;
            }

            Debug.LogErrorFormat("[{1}] Component Type '{0}' not found.", name, sourceInfo);
            return null;
        }

        private static IEnumerable<Type> GetTypesFromAssembly(Assembly assembly)
        {
            try
            {
#pragma warning disable RS0030 // GetTypes is flagged by the Banned API Analyzer. This pre-existing usage has been suppressed, but should be rewritten if possible.
                return assembly.GetTypes();
#pragma warning restore RS0030
            }
            catch (ReflectionTypeLoadException e)
            {
                var loaded = new List<Type>(e.Types.Length);
                for (int i = 0; i < e.Types.Length; i++)
                {
                    if (e.Types[i] != null)
                        loaded.Add(e.Types[i]);
                }
                return loaded;
            }
        }

        private static bool IsMarkedAsObsolete(Type t)
        {
            return t.IsDefined(typeof(ObsoleteAttribute), false);
        }

        static APIUpdaterRuntimeServices()
        {
            var componentType = typeof(Component);
#pragma warning disable RS0030 // GetTypes is flagged by the Banned API Analyzer. This pre-existing usage has been suppressed, but should be rewritten if possible.
            var allTypes = componentType.Assembly.GetTypes();
#pragma warning restore RS0030
            ComponentsFromUnityEngine = new List<Type>(allTypes.Length);
            for (int i = 0; i < allTypes.Length; i++)
            {
                var t = allTypes[i];
                if (componentType.IsAssignableFrom(t))
                    ComponentsFromUnityEngine.Add(t);
            }
        }

        [NoAutoStaticsCleanup] // only holds Component types from the engine assembly, which is not reloaded; keeping it avoids re-scanning GetTypes() on every code reload
        private static IList<Type> ComponentsFromUnityEngine;
        }
}
