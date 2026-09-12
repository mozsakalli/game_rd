// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Unity.Collections;

namespace UnityEditor.Search
{
    interface IQueryEnumerable<T> : IEnumerable<T>
    {
        void SetPayload(IEnumerable<T> payload);

        bool fastYielding { get; }
        IEnumerator<T> FastYieldingEnumerator();
    }

    interface IQueryEnumerableFactory
    {
        IQueryEnumerable<T> Create<T>(IQueryNode root, QueryEngine<T> engine, ICollection<QueryError> errors, bool fastYielding);
    }

    [AttributeUsage(AttributeTargets.Class)]
    class EnumerableCreatorAttribute : Attribute
    {
        public QueryNodeType nodeType { get; }

        public EnumerableCreatorAttribute(QueryNodeType nodeType)
        {
            this.nodeType = nodeType;
        }
    }

    static partial class EnumerableCreator
    {
        internal partial class LazyInitStatics
        {
            public Dictionary<QueryNodeType, IQueryEnumerableFactory> enumerableFactories { get; } = new();

            public LazyInitStatics()
            {
                #pragma warning disable UAC2001 // Avoid Linq
                var factoryTypes = TypeCache.GetTypesWithAttribute<EnumerableCreatorAttribute>().Where(t => typeof(IQueryEnumerableFactory).IsAssignableFrom(t));
#pragma warning restore UAC2001
                foreach (var factoryType in factoryTypes)
                {
                    var enumerableCreatorAttribute = factoryType.GetCustomAttributes(typeof(EnumerableCreatorAttribute), false).FirstOrDefault();
                    if (enumerableCreatorAttribute == null)
                        continue;

                    var nodeType = ((EnumerableCreatorAttribute)enumerableCreatorAttribute).nodeType;
                    var factory = Activator.CreateInstance(factoryType) as IQueryEnumerableFactory;
                    if (factory == null)
                        continue;

                    if (enumerableFactories.ContainsKey(nodeType))
                    {
                        Debug.LogWarning($"Factory for node type {nodeType} already exists.");
                        continue;
                    }
                    enumerableFactories.Add(nodeType, factory);
                }
            }
        }

        [AutoStaticsCleanupOnCodeReload]
        private static Lazy<LazyInitStatics> s_LazyInitStatics = new(() => new LazyInitStatics());

        static Dictionary<QueryNodeType, IQueryEnumerableFactory> s_EnumerableFactories => s_LazyInitStatics.Value.enumerableFactories;

        public static IQueryEnumerable<T> Create<T>(IQueryNode root, QueryEngine<T> engine, ICollection<QueryError> errors, bool fastYielding)
        {
            return s_EnumerableFactories.TryGetValue(root.type, out var factory) ? factory.Create<T>(root, engine, errors, fastYielding) : null;
        }
    }
}
