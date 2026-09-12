// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

#pragma warning disable UAL0010,UAL0011,UAL0012,UAL0013,UAL0014 // AutoStaticsCleanup: Burst not yet converted
namespace Unity.Burst.Editor
{
    internal partial class BurstDisassembler
    {
        internal class LLVMIRInstructionInfo
        {
            internal static bool GetLLVMIRInfo(string instructionName, out string instructionInfo)
            {
                var returnValue = true;

                switch (instructionName)
                {
                    default:
                        instructionInfo = string.Empty;
                        break;
                }

                return returnValue;
            }
        }
    }
}
#pragma warning restore UAL0010,UAL0011,UAL0012,UAL0013,UAL0014
