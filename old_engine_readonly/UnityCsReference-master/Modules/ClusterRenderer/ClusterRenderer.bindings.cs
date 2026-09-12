// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;

namespace UnityEngine
{
    ///<summary>A helper class that contains static method to inquire status of Unity Cluster.</summary>
    [NativeHeader("Modules/ClusterRenderer/ClusterNetwork.h")]
    [Obsolete("This type is deprecated and will be removed in a future release.", false)]
    public class ClusterNetwork
    {
        ///<summary>Check whether the current instance is a master node in the cluster network.</summary>
        public static extern bool isMasterOfCluster { get; }
        ///<summary>Check whether the current instance is disconnected from the cluster network.</summary>
        ///<remarks>A **client node** is disconnected when it fails to receive a signal within a timeout period while a **master node** is disconnected when it fails to receive a signal from all its client nodes within a timeout period. Note that a disconnected instance continues running and does not terminate itself.</remarks>
        public static extern bool isDisconnected { get; }
        ///<summary>To acquire or set the node index of the current machine from the cluster network.</summary>
        public static extern int nodeIndex { get; set; }
    }
}
