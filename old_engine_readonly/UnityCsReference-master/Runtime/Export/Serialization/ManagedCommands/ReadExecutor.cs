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
    // Inner loop shared by SerializationBufferToObjects (top-level) and
    // ConsumeLinearCollectionRead (per-element recursion). Each segment's DC
    // destOffsets restart at 0; segments are laid out contiguously in the
    // refill window we receive from EnsureReadable. The cursor advances past a
    // completed segment at the next segment boundary (leading FBP(N), a
    // variable-sized entry, or end-of-stream — see CommitReadSegment) so the
    // next segment's destOffsets land on the right slice.
    //
    // currentSegmentSize is threaded by ref so a recursion frame opened inside
    // a segment (for nested per-element bodies) sees the parent's outstanding
    // segment size and doesn't drop or double-advance the read cursor.
    private static unsafe void ExecuteReadCommands(
        NativeReadBufferContext* ctx,
        ref byte baseAddrParam,
        byte* entryBase, int entryBufSize,
        IntPtr transfer,
        ref int currentSegmentSize,
        int   repeatCount,
        long  repeatStride,
        IntPtr fuidCtxForElements = default)
    {
        byte* endPos = entryBase + entryBufSize;

        // repeatCount==1, repeatStride==0 (single-instance callers) folds away under the JIT.
        for (int elem = 0; elem < repeatCount; ++elem)
        {
            if (fuidCtxForElements != IntPtr.Zero)
                SetFUIDCurrentArrayIndex(fuidCtxForElements, elem);
            ref byte baseAddr = ref Unsafe.AddByteOffset(ref baseAddrParam, (nint)((long)elem * repeatStride));
            byte* pos = entryBase;

            while (pos < endPos)
            {
            // Refresh segment-local read cursor each iteration. ctx->readerPtr is
            // stable within a segment, but variable-sized entries (LinearCollection,
            // String/VRT) and the leading FBP(N>0) of the next segment move it, so we
            // re-snapshot before reading any DC entry. (No trailing FBP(0) in the stream:
            // segments commit via CommitReadSegment at the next FBP(N)/variable entry.)
            byte* input = ctx->readerPtr;
            var opCode = (RttiDataType)pos[0];

            switch (opCode)
            {
                // ---- Compact aligned ----
                case RttiDataType.DirectCopy1:
                {
                    var entry = ConsumeDirectCopyGroup<DirectCopyCompactEntry>(ref pos, out var end);
                    do
                    {
                        Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset) = *(input + entry->destOffset);
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
                        nint destOffset = (nint)entry->destOffset * 2;
                        Unsafe.As<byte, ushort>(ref Unsafe.AddByteOffset(ref baseAddr, fieldOffset)) = *(ushort*)(input + destOffset);
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
                        nint destOffset = (nint)entry->destOffset * 4;
                        Unsafe.As<byte, int>(ref Unsafe.AddByteOffset(ref baseAddr, fieldOffset)) = *(int*)(input + destOffset);
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
                        nint destOffset = (nint)entry->destOffset * 8;
                        // Unaligned: SelectDirectCopyOpCode's destOffset%8 gate only proves
                        // segment-relative alignment, not absolute address alignment. The
                        // segment base (input = ctx->readerPtr) can be 4-byte aligned (e.g.
                        // inside per-element bodies after a linear collection's 4B count
                        // prefix), which SIGBUS'd on armv7 with a typed 8B load. Build-side
                        // fix pending.
                        Unsafe.As<byte, long>(ref Unsafe.AddByteOffset(ref baseAddr, fieldOffset)) = Unsafe.ReadUnaligned<long>(input + destOffset);
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
                        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset), Unsafe.ReadUnaligned<ushort>(input + entry->destOffset));
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
                        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset), Unsafe.ReadUnaligned<int>(input + entry->destOffset));
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
                        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset), Unsafe.ReadUnaligned<long>(input + entry->destOffset));
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
                        Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset) = *(input + entry->destOffset);
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
                        uint destOffset = entry->destOffset * 2;
                        Unsafe.As<byte, ushort>(ref Unsafe.AddByteOffset(ref baseAddr, (nint)fieldOffset)) = *(ushort*)(input + destOffset);
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
                        uint destOffset = entry->destOffset * 4;
                        Unsafe.As<byte, int>(ref Unsafe.AddByteOffset(ref baseAddr, (nint)fieldOffset)) = *(int*)(input + destOffset);
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
                        uint destOffset = entry->destOffset * 8;
                        // Unaligned: see DirectCopy8 above for the segment-base alignment caveat.
                        Unsafe.As<byte, long>(ref Unsafe.AddByteOffset(ref baseAddr, (nint)fieldOffset)) = Unsafe.ReadUnaligned<long>(input + destOffset);
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
                        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset), Unsafe.ReadUnaligned<ushort>(input + entry->destOffset));
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
                        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset), Unsafe.ReadUnaligned<int>(input + entry->destOffset));
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
                        Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset), Unsafe.ReadUnaligned<long>(input + entry->destOffset));
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                case RttiDataType.FixedBlockPrefix:
                {
                    var prefix = (ManagedCommandFixedBlockPrefix*)pos;
                    pos += sizeof(ManagedCommandFixedBlockPrefix);

                    // Commit the prior segment, then open the new one. The read cursor
                    // advances only at a segment boundary — DC entries index off
                    // ctx->readerPtr + entry->destOffset within the open segment.
                    CommitReadSegment(ctx, ref currentSegmentSize);

                    currentSegmentSize = prefix->payloadSize;
                    if (ctx->readerAvailable < currentSegmentSize)
                        InvokeEnsureReadable(ctx, currentSegmentSize);
                    break;
                }

                // Each entry's field-table slot is forwarded to
                // ReadUnityObjectFromBuffer so a resolver-miss becomes an editor
                // fake-null wrapper that keeps the EntityId for re-save, matching
                // the native Transfer_UnityEngineObject path.
                case RttiDataType.UnityObject:
                {
                    var entry = ConsumeDirectCopyGroup<UnityObjectReadEntry>(ref pos, out var end);
                    int count = (int)(end - entry);
                    // The field-table mirrors FixedSegment_UnityObjectRead_Emit's
                    // memcpy on the native side: pos lands at a 4-byte-aligned
                    // offset (4B group header + count * 16B Pack=4 entries), so
                    // direct IntPtr* deref is UB on arm64 / SIGBUS under IL2CPP.
                    byte* fieldTableBase = pos;
                    pos += count * 2 * sizeof(IntPtr);

                    // ≥2 fields: one crossing. count==1 per-field is lighter when there's nothing to amortize.
                    if (count > 1)
                    {
                        s_readUnityObjectsIntoFields(
                            ctx->resolverHandle, ctx->flags,
                            (IntPtr)Unsafe.AsPointer(ref baseAddr),
                            (IntPtr)entry, (IntPtr)fieldTableBase, (IntPtr)input, count);
                        break;
                    }
                    // Per-field: count==1 fast path; also CoreCLR (moving GC + Object return can't cross calli)
                    // and the native-test image.
                    // A packed EntityId with no resolver can bind straight to a resident wrapper,
                    // skipping the crossing. CoreCLR only: entry->klass is a managed-decodable
                    // handle just there.
                    int i = 0;
                    do
                    {
                        ref object fieldRef = ref Unsafe.As<byte, object>(ref Unsafe.AddByteOffset(ref baseAddr, (nint)entry->fieldOffset));
                        byte* src = input + entry->destOffset;

                        object managed = null;

                        if (managed != null)
                        {
                            fieldRef = managed;
                        }
                        else
                        {
                            byte* slotBase = fieldTableBase + i * 2 * sizeof(IntPtr);
                            IntPtr fieldPtr       = Unsafe.ReadUnaligned<IntPtr>(slotBase);
                            IntPtr fieldParentPtr = Unsafe.ReadUnaligned<IntPtr>(slotBase + sizeof(IntPtr));

                            fieldRef = ReadUnityObjectFromBuffer(
                                ctx->resolverHandle, (IntPtr)src, entry->klass, ctx->flags,
                                fieldPtr, fieldParentPtr);
                        }
                        entry++;
                        i++;
                    }
                    while (entry < end);
                    break;
                }

                // [SerializeReference] inline RefId read. Same on-wire shape as the
                // ManagedReference write case (UnityObjectWriteEntry: {fieldOffset,
                // destOffset}) — the build emits one descriptor for both directions
                // because the per-entry layout is identical. The icall reads 8 bytes
                // from the wire, activates the managed-references state (so the
                // `references:` blob is consumed into the registry), and registers a
                // deferred fixup; the existing PerformFixups flow resolves it once
                // the registry blob has been read. transferState / instance come
                // from the read context (set by Transfer_ManagedBlock_StreamedBinaryRead);
                // both are non-null whenever this opcode appears (build only emits
                // it for SR fields in StreamedBinaryRead transfers). SR collection
                // elements stay on the native ManagedRefArrayItemTransferer arm,
                // which calls RegisterFixupRequest per element on its own — no
                // cursor coordination needed on read.
                case RttiDataType.ManagedReference:
                {
                    var entry = ConsumeDirectCopyGroup<UnityObjectWriteEntry>(ref pos, out var end);
                    do
                    {
                        byte* src = input + entry->destOffset;
                        ReadManagedReferenceFromBuffer(ctx->transferState, ctx->instance, (int)entry->fieldOffset, (IntPtr)src);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                case RttiDataType.EntityId:
                {
                    var entry = ConsumeDirectCopyGroup<EntityIdReadEntry>(ref pos, out var end);
                    bool packInLSOI = (ctx->flags & UnityObjectTransferFlags.PackEntityIdInLSOI) != 0;
                    // Remap arm: ≥2 fields in one crossing on all runtimes (value store, no write barrier).
                    int count = (int)(end - entry);
                    if (!packInLSOI && count > 1)
                    {
                        s_readEntityIdsIntoFields(
                            ctx->resolverHandle, ctx->flags,
                            (IntPtr)Unsafe.AsPointer(ref baseAddr),
                            (IntPtr)entry, (IntPtr)input, count);
                        break;
                    }
                    do
                    {
                        byte* src = input + entry->destOffset;
                        ulong entityId = packInLSOI
                            ? UnpackEntityIdFromLsoi(src)
                            : s_readEntityIdFromBuffer(ctx->resolverHandle, (IntPtr)src, ctx->flags);
                        ref byte fieldByteRef = ref Unsafe.AddByteOffset(ref baseAddr, (nint)entry->fieldOffset);
                        Unsafe.WriteUnaligned<ulong>(ref fieldByteRef, entityId);
                        entry++;
                    }
                    while (entry < end);
                    break;
                }

                // Gated on ENABLE_CORECLR alone (matching the emitter): native-test image uses the per-element fallback inside.
                case RttiDataType.UnityObjectArray:
                {
                    // Variable-sized entries read directly from readerPtr, so the
                    // in-progress fixed segment must be committed (cursor advanced
                    // past it) before handing off.
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumeLinearCollectionUnityObjectArrayRead(ctx, ref baseAddr, ref pos);
                    break;
                }

                // EntityId array: value stores, safe on all runtimes, not CoreCLR-gated.
                case RttiDataType.EntityIdArray:
                {
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumeLinearCollectionEntityIdArrayRead(ctx, ref baseAddr, ref pos);
                    break;
                }

                case RttiDataType.Array:
                case RttiDataType.List:
                {
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumeLinearCollectionRead(ctx, ref baseAddr, transfer, ref pos);
                    break;
                }

                case RttiDataType.Dictionary:
                {
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumeDictionaryRead(ctx, ref baseAddr, transfer, ref pos);
                    break;
                }

                case RttiDataType.FixedBuffer:
                {
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumeFixedBufferRead(ctx, ref baseAddr, ref pos);
                    break;
                }

                case RttiDataType.ValueReferenceType:
                {
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumeValueReferenceRead(ctx, ref baseAddr, transfer, ref pos);
                    break;
                }

                case RttiDataType.CallOnAfterDeserializeClass:
                {
                    var header = (CallbackHeader*)pos;
                    pos += sizeof(CallbackHeader);
                    object target = Unsafe.As<byte, object>(
                        ref Unsafe.AddByteOffset(ref baseAddr, header->fieldOffset));
                    if (target is ISerializationCallbackReceiver receiver)
                        receiver.OnAfterDeserialize();
                    break;
                }

                case RttiDataType.CallOnAfterDeserializeStruct:
                {
                    var header = (CallbackHeader*)pos;
                    pos += sizeof(CallbackHeader);
                    if (header->methodFnPtr != IntPtr.Zero)
                    {
                        ref byte structData = ref Unsafe.AddByteOffset(ref baseAddr, header->fieldOffset);
                        ((delegate*<ref byte, void>)header->methodFnPtr)(ref structData);
                    }
                    break;
                }

                case RttiDataType.String:
                {
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumeStringRead(ctx, ref baseAddr, ref pos);
                    break;
                }

                case RttiDataType.NativeValueStruct:
                {
                    var entry = (ManagedCommandNativeValueStructEntry*)pos;
                    pos += sizeof(ManagedCommandNativeValueStructEntry);

                    // The native dispatch reads straight off the CachedReader at the
                    // current cursor, so commit the in-progress fixed segment first to
                    // advance readerPtr past it.
                    CommitReadSegment(ctx, ref currentSegmentSize);

                    // Inline value struct: the storage is inline (no wrapper to
                    // construct), so just hand the field's own address to the native
                    // Transfer dispatcher. baseAddr is caller-pinned.
                    ref byte nvsField = ref Unsafe.AddByteOffset(ref baseAddr, (nint)entry->fieldOffset);
                    IntPtr nvsFieldPtr = (IntPtr)Unsafe.AsPointer(ref nvsField);

                    // Dispatch reads straight off the CachedReader; rewind it first.
                    InvokeSyncReader(ctx);
                    ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)entry->fnPtr)(nvsFieldPtr, transfer, IntPtr.Zero);
                    break;
                }

                case RttiDataType.SimpleNativeType:
                {
                    var entry = (ManagedCommandSimpleNativeTypeReadEntry*)pos;
                    pos += sizeof(ManagedCommandSimpleNativeTypeReadEntry);

                    // The native dispatch reads straight off the CachedReader at the
                    // current cursor, so commit the in-progress fixed segment first to
                    // advance readerPtr past it.
                    CommitReadSegment(ctx, ref currentSegmentSize);

                    // Wrapper field is a reference slot in the host instance. If null,
                    // construct via the entry's runtimeTypeHandle + ctorFunctionPtr so
                    // the wrapper's parameterless ctor runs and allocates the native peer.
                    // Init refuses registration when ctorFunctionPtr would be zero, so we
                    // can rely on a usable m_Ptr after construction.
                    ref object slot = ref Unsafe.As<byte, object>(
                        ref Unsafe.AddByteOffset(ref baseAddr, (nint)entry->fieldOffset));
                    object wrapper = slot;
                    if (wrapper == null)
                    {
                        wrapper = CreateWrapperInstance(entry->runtimeTypeHandle, entry->ctorFunctionPtr);
                        slot = wrapper;
                    }

                    // m_Ptr lives at userData bytes past the wrapper's post-header data
                    // start (offset queried by scripting_field_get_offset at init time
                    // so it's correct for the active scripting backend).
                    IntPtr nativePtr = Unsafe.ReadUnaligned<IntPtr>(ref Unsafe.AddByteOffset(
                        ref Unsafe.As<ObjectWrapper>(wrapper).Data,
                        entry->userData));

                    // Dispatch reads straight off the CachedReader, which EnsureReadable
                    // has pre-fetched past the cursor; rewind it before handing over.
                    InvokeSyncReader(ctx);

                    // Reads the wire bytes into the native peer.
                    ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)entry->fnPtr)(nativePtr, transfer, entry->userData);

                    // Managed post-deserialize hook for wrappers that opted in
                    // (e.g. GUIStyle.InternalOnAfterDeserialize).
                    if (entry->managedPostDispatchFnPtr != IntPtr.Zero)
                        ((delegate*<object, IntPtr, void>)entry->managedPostDispatchFnPtr)(wrapper, nativePtr);
                    break;
                }

                case RttiDataType.PropertyNameId:
                    CommitReadSegment(ctx, ref currentSegmentSize);
                    ConsumePropertyNameRead(ctx, ref baseAddr, ref pos);
                    break;

                case RttiDataType.DirectCopyBlock:
                case RttiDataType.Reference:
                case RttiDataType.DynamicBuffer:
                case RttiDataType.Unknown:
                    throw new NotSupportedException(
                        $"OpCode {opCode} is not implemented for managed command blocks.");
                default:
                    throw new NotSupportedException($"OpCode {opCode} not supported");
            }

            // Match the writer's 4-byte header alignment (see ObjectsToSerializationBuffer
            // for details). Compact groups with an odd entry count leave pos 2 bytes short.
            long entryOffset = pos - entryBase;
            long aligned = (entryOffset + 3) & ~3L;
            pos = entryBase + aligned;
        }
        }

        // Commit the last open segment once the stream is exhausted. Earlier segments
        // (including each earlier element of a repeatCount walk) were already committed by
        // the leading FBP(N)/variable entry that followed them; nothing follows the final
        // one, so commit it here.
        CommitReadSegment(ctx, ref currentSegmentSize);
    }

}
