// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using Unity.Scripting.LifecycleManagement;
// EntityId lives in namespace UnityEngine (UnityEngineObject.bindings.cs:141). The
// test-resources compile context (UNITY_NATIVE_TEST_RESOURCES) provides a stub in the
// same namespace via Runtime/Testing/ScriptWithManagedRefTestFixture.Resources_cs, so
// the bare `EntityId` field type below resolves identically in both compile contexts.
using UnityEngine;

namespace UnityEngine.Serialization;

internal static unsafe partial class SerializationBackendManagedCommands
{
    // Shuffle-path consumer for linear collections of value-type elements
    // whose per-element body is purely DC + FBP and fits in a single segment.
    // The body bytes are identical to what the per-element recursion path
    // would walk; we just walk them once per element with a fixed per-element
    // destination and skip all the FBP segment-claim / staging bookkeeping.
    //
    // The build side gates shuffle eligibility on elementWireSize <= kManagedBlockMaxPayloadSize,
    // so a fresh window always holds at least one element. Each batch stages rather than
    // flushing, so the count rides the first batch and the last coalesces with the surrounding
    // flow — one flush per window instead of one P/Invoke per element.
    private static unsafe void ConsumeLinearCollectionShufflePath(
        NativeBufferContext* ctx,
        byte[] dataAsBytes, int count,
        long stride, int elementWireSize,
        byte* body, int bodyLen,
        ref BufferDataStager bufferDataStager)
    {
        Unsafe.WriteUnaligned(bufferDataStager.Reserve(4), count);
        if (count == 0)
            return;

        fixed (byte* dataPtr = dataAsBytes)
        {
            byte* srcCur      = dataPtr;
            int   elementsLeft = count;

            while (elementsLeft > 0)
            {
                // The staged count (or a prior batch's tail) can leave < one element of room;
                // flush to open a fresh window. The build-side gate (elementWireSize <=
                // kManagedBlockMaxPayloadSize) then makes batch >= 1.
                if (bufferDataStager.StagingRoom < elementWireSize)
                    bufferDataStager.FlushStaged(kManagedBlockMaxPayloadSize);

                int batch = bufferDataStager.StagingRoom / elementWireSize;
                if (batch > elementsLeft)
                    batch = elementsLeft;

                byte* dst        = bufferDataStager.StagingPtr;
                int   batchBytes = batch * elementWireSize;

                // Pre-zero the batch's wire window. The transposed walker uses
                // width-correct stores (byte/ushort/int/long matching each DC
                // opcode's wire-slot size); padding bytes between slots — which
                // the prior int-store version implicitly zeroed via 4-byte
                // spillover from DC1/DC2 stores — get their zeros from this
                // memset instead. One memset per batch (<= writerAvailable,
                // typically a single page) is negligible against the
                // count*K -> K body-walk reduction below.
                Unsafe.InitBlockUnaligned(dst, 0, (uint)batchBytes);

                ExecuteShuffleBatch(srcCur, dst, batch, stride, elementWireSize, body, bodyLen);

                // Stage the batch; it flushes with the next window-full batch or, for the last
                // batch, with the surrounding flow.
                bufferDataStager.Stage(batchBytes);
                srcCur       += (long)batch * stride;
                elementsLeft -= batch;
            }
        }
    }

    // Transposed walker for shuffle-path bodies. Mirrors the DC opcode
    // dispatch in ExecuteWriteCommands but flips the loop nesting from
    //   for each element: walk body, dispatch every DC group
    // to
    //   walk body once: for each DC group, for each entry, copy across all
    //   `batch` elements with strided pointer arithmetic
    // so the switch dispatch + ConsumeDirectCopyGroup header read +
    // (entryOffset + 3) & ~3L re-align run K times instead of K * batch
    // times. The element loop becomes the innermost — predictable counted
    // iteration over a strided memory copy with constant offsets, which
    // also gives the JIT a fighting chance to vectorise even though stride
    // and elementWireSize are runtime values.
    //
    // Width-correct stores. The per-element predecessor wrote 4 bytes for
    // every DC1 / DC2 entry — only the low N bytes carried meaning, the
    // upper 4-N spilled into adjacent slots and were either overwritten by
    // subsequent entries within the same element, or by the first entries
    // of the next element. Per-element ordering kept that overlap-and-fixup
    // pattern coherent. Transposing
    // breaks it: an entry's spillover for element e now lands in element
    // e+1 *after* element e+1's matching entry has already written its
    // value, with no later write to fix it up. So the transposed walker
    // stores exactly N bytes per DC<N> opcode (byte/ushort/int/long); the
    // intra-element padding that the int-store spillover used to zero
    // comes from the caller's one-shot InitBlockUnaligned of the batch's
    // wire window. Net wire bytes are byte-for-byte identical; the
    // intermediate buffer state along the way differs, but only in the
    // padding bytes which both versions end up with as zero.
    //
    // FBP entries exist in the body for wire-format consistency with the
    // per-element recursion path; we skip them. Any non-DC, non-FBP opcode
    // indicates the build side incorrectly tagged a body as shuffle-
    // eligible — fail fast.
    private static unsafe void ExecuteShuffleBatch(
        byte* srcBase, byte* dstBase,
        int batch,
        long srcStride, int dstStride,
        byte* body, int bodyLen)
    {
        byte* pos    = body;
        byte* endPos = body + bodyLen;

        while (pos < endPos)
        {
            var opCode = (RttiDataType)pos[0];

            switch (opCode)
            {
                case RttiDataType.FixedBlockPrefix:
                    pos += sizeof(ManagedCommandFixedBlockPrefix);
                    continue;

                // ---- Compact aligned ----
                case RttiDataType.DirectCopy1:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(d + (long)i * dstStride) = *(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy2:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        nint fieldOffset = (nint)entry->fieldOffset * 2;
                        nint destOffset  = (nint)entry->destOffset  * 2;
                        byte* s = srcBase + fieldOffset;
                        byte* d = dstBase + destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(ushort*)(d + (long)i * dstStride) = *(ushort*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        nint fieldOffset = (nint)entry->fieldOffset * 4;
                        nint destOffset  = (nint)entry->destOffset  * 4;
                        byte* s = srcBase + fieldOffset;
                        byte* d = dstBase + destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(int*)(d + (long)i * dstStride) = *(int*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        nint fieldOffset = (nint)entry->fieldOffset * 8;
                        nint destOffset  = (nint)entry->destOffset  * 8;
                        byte* s = srcBase + fieldOffset;
                        byte* d = dstBase + destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(long*)(d + (long)i * dstStride) = *(long*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                // ---- Compact unaligned ----
                case RttiDataType.DirectCopy2_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<ushort>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<int>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<long>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                // ---- Large aligned ----
                case RttiDataType.DirectCopy1_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(d + (long)i * dstStride) = *(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy2_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        uint fieldOffset = entry->fieldOffset * 2;
                        uint destOffset  = entry->destOffset  * 2;
                        byte* s = srcBase + fieldOffset;
                        byte* d = dstBase + destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(ushort*)(d + (long)i * dstStride) = *(ushort*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        uint fieldOffset = entry->fieldOffset * 4;
                        uint destOffset  = entry->destOffset  * 4;
                        byte* s = srcBase + fieldOffset;
                        byte* d = dstBase + destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(int*)(d + (long)i * dstStride) = *(int*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        uint fieldOffset = entry->fieldOffset * 8;
                        uint destOffset  = entry->destOffset  * 8;
                        byte* s = srcBase + fieldOffset;
                        byte* d = dstBase + destOffset;
                        for (int i = 0; i < batch; ++i)
                            *(long*)(d + (long)i * dstStride) = *(long*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                // ---- Large unaligned ----
                case RttiDataType.DirectCopy2_L_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<ushort>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4_L_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<int>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8_L_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->fieldOffset;
                        byte* d = dstBase + entry->destOffset;
                        for (int i = 0; i < batch; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<long>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"Unexpected opcode {opCode} in shuffle-path body. The build side gates "
                        + "shuffle eligibility on a DC-only body — anything else here is a bug.");
            }

            // Compact groups with an odd entry count leave pos 2 bytes short of
            // a 4-byte boundary; re-align so the next header (FBP or DC group)
            // lands at the alignment its uint fields need.
            long entryOffset = pos - body;
            long aligned     = (entryOffset + 3) & ~3L;
            pos = body + aligned;
        }
    }



    // Read mirror of ExecuteShuffleBatch. Walks the FBP-bracketed DC-only
    // body once, then for each DC entry runs an inner loop across all
    // `count` elements with strided pointer arithmetic — moving the switch
    // dispatch + ConsumeDirectCopyGroup header read + 4-byte re-align cost
    // from O(K * count) down to O(K). Width-correct loads and stores
    // throughout (read N from wire-side `srcBase + destOffset`, store N
    // into managed-side `dstBase + fieldOffset`); the read direction
    // already had no spillover, so the transposition is straight loop
    // inversion with no semantic change. Caller pins the managed array
    // with `fixed`, so raw pointer arithmetic against `dstBase` is safe
    // for the duration of the call. FBP entries are skipped; any non-DC,
    // non-FBP opcode trips the InvalidOperationException because the
    // build side guarantees DC-only bodies for the shuffle flag.
    private static unsafe void ExecuteReadShuffleBatch(
        byte* srcBase, byte* dstBase,
        int count,
        int srcStride, long dstStride,
        byte* body, int bodyLen)
    {
        byte* pos    = body;
        byte* endPos = body + bodyLen;

        while (pos < endPos)
        {
            var opCode = (RttiDataType)pos[0];

            switch (opCode)
            {
                case RttiDataType.FixedBlockPrefix:
                    pos += sizeof(ManagedCommandFixedBlockPrefix);
                    continue;

                // ---- Compact aligned ----
                case RttiDataType.DirectCopy1:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(d + (long)i * dstStride) = *(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy2:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        nint fieldOffset = (nint)entry->fieldOffset * 2;
                        nint destOffset  = (nint)entry->destOffset  * 2;
                        byte* s = srcBase + destOffset;
                        byte* d = dstBase + fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(ushort*)(d + (long)i * dstStride) = *(ushort*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        nint fieldOffset = (nint)entry->fieldOffset * 4;
                        nint destOffset  = (nint)entry->destOffset  * 4;
                        byte* s = srcBase + destOffset;
                        byte* d = dstBase + fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(int*)(d + (long)i * dstStride) = *(int*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        nint fieldOffset = (nint)entry->fieldOffset * 8;
                        nint destOffset  = (nint)entry->destOffset  * 8;
                        byte* s = srcBase + destOffset;
                        byte* d = dstBase + fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(long*)(d + (long)i * dstStride) = *(long*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                // ---- Compact unaligned ----
                case RttiDataType.DirectCopy2_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<ushort>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<int>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<long>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                // ---- Large aligned ----
                case RttiDataType.DirectCopy1_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(d + (long)i * dstStride) = *(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy2_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        uint fieldOffset = entry->fieldOffset * 2;
                        uint destOffset  = entry->destOffset  * 2;
                        byte* s = srcBase + destOffset;
                        byte* d = dstBase + fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(ushort*)(d + (long)i * dstStride) = *(ushort*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        uint fieldOffset = entry->fieldOffset * 4;
                        uint destOffset  = entry->destOffset  * 4;
                        byte* s = srcBase + destOffset;
                        byte* d = dstBase + fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(int*)(d + (long)i * dstStride) = *(int*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8_L:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        uint fieldOffset = entry->fieldOffset * 8;
                        uint destOffset  = entry->destOffset  * 8;
                        byte* s = srcBase + destOffset;
                        byte* d = dstBase + fieldOffset;
                        for (int i = 0; i < count; ++i)
                            *(long*)(d + (long)i * dstStride) = *(long*)(s + (long)i * srcStride);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                // ---- Large unaligned ----
                case RttiDataType.DirectCopy2_L_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<ushort>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy4_L_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<int>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }
                case RttiDataType.DirectCopy8_L_Unaligned:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyLargeEntry>(ref pos, out var end);
                    do
                    {
                        byte* s = srcBase + entry->destOffset;
                        byte* d = dstBase + entry->fieldOffset;
                        for (int i = 0; i < count; ++i)
                            Unsafe.WriteUnaligned(d + (long)i * dstStride, Unsafe.ReadUnaligned<long>(s + (long)i * srcStride));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"Unexpected opcode {opCode} in shuffle-path body. The build side gates "
                        + "shuffle eligibility on a DC-only body — anything else here is a bug.");
            }

            long entryOffset = pos - body;
            long aligned     = (entryOffset + 3) & ~3L;
            pos = body + aligned;
        }
    }

}
