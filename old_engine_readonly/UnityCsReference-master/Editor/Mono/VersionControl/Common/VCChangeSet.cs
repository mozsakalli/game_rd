// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Scripting.LifecycleManagement;

namespace UnityEditor.VersionControl
{
    public partial class ChangeSet
    {
        // Public mutable static: reset to the "-1" sentinel on each code reload to preserve
        // the historical domain-reload semantics (can't be readonly without breaking public API).
        [AutoStaticsCleanupOnCodeReload]
        public static string defaultID = "-1";

        public ChangeSet()
        {
            InternalCreate();
        }

        public ChangeSet(string description)
        {
            InternalCreateFromString(description);
        }

        public ChangeSet(string description, string revision)
        {
            InternalCreateFromStringString(description, revision);
        }

        public ChangeSet(ChangeSet other)
        {
            InternalCopyConstruct(other);
        }

#pragma warning disable UA5000 // The Avoid Finalizer Analyzer produces compile errors for any new finalizers. This pre-existing finalizer declaration has been suppressed, but should be rewritten if possible.
        ~ChangeSet()
        {
            Dispose();
        }
#pragma warning restore UA5000
    }
}
