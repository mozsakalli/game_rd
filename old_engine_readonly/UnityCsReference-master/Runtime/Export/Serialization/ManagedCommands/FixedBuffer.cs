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
    // Mirrors ConsumeLinearCollection's trivially-copyable arm, but sourced from
    // an inline buffer at baseAddr + fieldOffset — no array reference, null check,
    // or reflection.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConsumeFixedBuffer(
        ref byte baseAddr, ref byte* pos, ref BufferDataStager bufferDataStager)
    {
        var header = (FixedBufferHeader*)pos;
        pos += sizeof(FixedBufferHeader);

        int count       = (int)header->elementCount;
        int totalBytes  = count * (int)header->elementSize;
        int padBytes    = (4 - (totalBytes & 3)) & 3;

        // baseAddr is pinned by the ExecuteWriteCommands caller, so this pointer stays valid.
        byte* dataPtr = (byte*)Unsafe.AsPointer(
            ref Unsafe.AddByteOffset(ref baseAddr, header->fieldOffset));

        // 4-byte count + padded payload, so the record keeps the staging cursor 4-byte aligned.
        int record = 4 + totalBytes + padBytes;

        // Stage the framed record when it fits a window, so a run of fixed buffers (and
        // adjacent DirectCopy segments) commits in one flush — the batching the DirectCopy
        // segment path relies on.
        byte* dst = bufferDataStager.TryReserve(record);
        if (dst != null)
        {
            Unsafe.WriteUnaligned(dst, count);
            Buffer.MemoryCopy(dataPtr, dst + 4, totalBytes, totalBytes);
            if (padBytes > 0)
                Unsafe.InitBlockUnaligned(dst + 4 + totalBytes, 0, (uint)padBytes);
            return;
        }

        // Payload exceeds a whole window: TryReserve has committed the staged bytes
        // (staged == 0). Frame the count, hand the inline source to FlushBuffer's spill
        // arm so it crosses any number of cache-writer blocks in one flush, then the tail
        // pad (Bulk requests a full window, and the pad's Reserve threads its own size).
        Unsafe.WriteUnaligned(bufferDataStager.Reserve(4), count);
        bufferDataStager.FlushStaged(kManagedBlockMaxPayloadSize);
        bufferDataStager.Bulk(dataPtr, totalBytes);
        if (padBytes > 0)
            Unsafe.InitBlockUnaligned(bufferDataStager.Reserve(padBytes), 0, (uint)padBytes);
    }


    // Read-path mirror of ConsumeFixedBuffer. Truncating on overflow and leaving
    // trailing inline bytes untouched on underflow matches the native
    // Transfer_Blittable_FixedBufferField semantic (Blittable.h), so wire bytes
    // round-trip even when the inline buffer width changed between the asset and
    // the current class.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConsumeFixedBufferRead(
        NativeReadBufferContext* ctx,
        ref byte baseAddr,
        ref byte* pos)
    {
        var header = (FixedBufferHeader*)pos;
        pos += sizeof(FixedBufferHeader);

        if (ctx->readerAvailable < 4)
            InvokeEnsureReadable(ctx, 4);
        int wireCount = Unsafe.ReadUnaligned<int>(ctx->readerPtr);
        ctx->readerPtr      += 4;
        ctx->readerAvailable -= 4;

        // A negative length would sign-extend into an over-read in InvokeReadBytesDirect.
        if (wireCount < 0)
            throw new InvalidOperationException(
                $"Managed fixed-buffer deserialization read a negative length prefix ({wireCount}). The serialized data is corrupted.");

        int elementSize = header->elementSize;
        int capacity    = (int)header->elementCount;
        int copyCount   = wireCount < capacity ? wireCount : capacity;
        int copyBytes   = copyCount * elementSize;
        long wireBytesL = (long)wireCount * (long)elementSize;
        int  alignBytes = (int)((4 - (wireBytesL & 3)) & 3);

        if (copyBytes > 0)
        {
            byte* dstPtr = (byte*)Unsafe.AsPointer(
                ref Unsafe.AddByteOffset(ref baseAddr, header->fieldOffset));

            // Stream straight into the inline buffer: ReadBytesDirect drains any
            // already-buffered bytes and reads the remainder in a single call,
            // spanning any number of cache-reader blocks at any size.
            InvokeReadBytesDirect(ctx, dstPtr, copyBytes);
        }

        // Discard any wire overflow (assets where the buffer shrunk between
        // versions). Chunked because a single ensureReadable refill is capped at
        // stackBufferSize and a long buffer can exceed it.
        long discardBytes = wireBytesL - copyBytes;
        while (discardBytes > 0)
        {
            int chunk = discardBytes > ctx->stackBufferSize
                ? ctx->stackBufferSize
                : (int)discardBytes;
            if (ctx->readerAvailable < chunk)
                InvokeEnsureReadable(ctx, chunk);
            ctx->readerPtr      += chunk;
            ctx->readerAvailable -= chunk;
            discardBytes        -= chunk;
        }

        if (alignBytes > 0)
        {
            if (ctx->readerAvailable < alignBytes)
                InvokeEnsureReadable(ctx, alignBytes);
            ctx->readerPtr      += alignBytes;
            ctx->readerAvailable -= alignBytes;
        }
    }

}
