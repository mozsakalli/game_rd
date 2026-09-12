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
    // String opcode: read the managed string field and frame it via the shared
    // WriteFramedString, which stages into the writer tail so a string field defers
    // like a fixed DirectCopy segment.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConsumeString(
        NativeBufferContext* ctx, ref byte baseAddr, ref byte* pos,
        ref BufferDataStager bufferDataStager)
    {
        var entry = (ManagedCommandStringEntry*)pos;
        pos += sizeof(ManagedCommandStringEntry);
        // Field offset points at a managed string reference inside the pinned object;
        // Unsafe.As<byte, string> reinterprets that ref as a string ref.
        string str = Unsafe.As<byte, string>(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset)) ?? string.Empty;
        WriteFramedString(ctx, str.AsSpan(), ref bufferDataStager);
    }

    // Write side of the PropertyName opcode. Frames byte-identically to
    // SerializeTraits<PropertyName> so assets round-trip with the native system.

    // Player / game-release: always serializes the decimal id (no editor string table).
    // The native-test fake { int id; } uses this path too.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConsumePropertyNamePlayer(
        NativeBufferContext* ctx, ref byte baseAddr, ref byte* pos,
        ref BufferDataStager bufferDataStager)
    {
        var entry = (ManagedCommandPropertyNameEntry*)pos;
        pos += sizeof(ManagedCommandPropertyNameEntry);
        int id = Unsafe.ReadUnaligned<int>(ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset));
        WriteFramedDecimalInt32(ctx, id, ref bufferDataStager);
    }

    // Editor non-game-release: persists the resolved name. Reads the whole struct so
    // conflictIndex disambiguates the id (matches the native system). Game-release
    // writes the id.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConsumePropertyNameEditor(
        NativeBufferContext* ctx, ref byte baseAddr, ref byte* pos,
        ref BufferDataStager bufferDataStager)
    {
        var entry = (ManagedCommandPropertyNameEntry*)pos;
        byte serializesAsId = entry->serializesAsId;
        pos += sizeof(ManagedCommandPropertyNameEntry);
        ref byte field = ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset);

        if (serializesAsId == 0)
        {
            PropertyName pn = Unsafe.ReadUnaligned<PropertyName>(ref field);
            string s = PropertyNameUtils.StringFromPropertyName(pn);
            // null (unregistered id) → empty string, matching the native system.
            WriteFramedString(ctx, (s ?? string.Empty).AsSpan(), ref bufferDataStager);
        }
        else
        {
            int id = Unsafe.ReadUnaligned<int>(ref field);
            WriteFramedDecimalInt32(ctx, id, ref bufferDataStager);
        }
    }

    // Length-prefixed UTF-8 framing (4-byte SInt32 length + UTF-8 body truncated at the
    // first '\0' + 0..3-byte pad to 4-byte alignment), staged into the writer tail.
    // Shared by the String opcode and the editor PropertyName-name path; the chunked arm
    // handles strings larger than one buffer region.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void WriteFramedString(NativeBufferContext* ctx, ReadOnlySpan<char> chars,
        ref BufferDataStager bufferDataStager)
    {
        int nullIdx = chars.IndexOf('\0');
        if (nullIdx >= 0)
            chars = chars.Slice(0, nullIdx);

        int totalByteCount = Encoding.UTF8.GetByteCount(chars);
        int padBytes = (4 - (totalByteCount & 3)) & 3;
        int totalFramedSize = 4 + totalByteCount + padBytes;

        // Stage the whole framed string when it fits a single window — alongside any
        // already-staged bytes, or in a fresh region after a flush. No flush of its own.
        byte* dst = bufferDataStager.TryReserve(totalFramedSize);
        if (dst != null)
        {
            Unsafe.WriteUnaligned(dst, totalByteCount);
            if (totalByteCount > 0)
                Encoding.UTF8.GetBytes(chars, new Span<byte>(dst + 4, totalByteCount));
            if (padBytes > 0)
                Unsafe.InitBlockUnaligned(dst + 4 + totalByteCount, 0, (uint)padBytes);
            return;
        }

        // Chunked path (oversized name): TryReserve has committed the staged bytes
        // (staged == 0). Stage the length header — it rides the first body chunk's flush
        // rather than crossing on its own — then stream the body region by region, flushing
        // only when a region fills with body still to write.
        Unsafe.WriteUnaligned(bufferDataStager.Reserve(4), totalByteCount);
        if (totalByteCount > 0)
        {
            // flush:false while input remains so the encoder can hold a high surrogate
            // across chunks until its low surrogate arrives in the next call.
            Encoder encoder = s_Utf8Encoder ??= Encoding.UTF8.GetEncoder();
            encoder.Reset();
            ReadOnlySpan<char> remaining = chars;
            while (!remaining.IsEmpty)
            {
                encoder.Convert(remaining, new Span<byte>(bufferDataStager.StagingPtr, bufferDataStager.StagingRoom),
                                flush: false, out int charsUsed, out int bytesUsed, out _);
                bufferDataStager.Stage(bytesUsed);
                remaining = remaining.Slice(charsUsed);
                // Body remains but the encoder stopped: the window is full. Flush to open a
                // fresh region. When the body is done we keep the bytes staged so the pad —
                // and the next field — coalesce onto the surrounding flow's flush.
                if (!remaining.IsEmpty)
                    bufferDataStager.FlushStaged(kManagedBlockMaxPayloadSize);
            }
            // End-of-stream drain: the encoder may still hold one high surrogate, emitted now
            // as a replacement (<= 3 bytes). If the final chunk filled the window exactly it
            // has no room, so flush and retry; otherwise this is a single no-op Convert.
            bool completed;
            do
            {
                encoder.Convert(ReadOnlySpan<char>.Empty, new Span<byte>(bufferDataStager.StagingPtr, bufferDataStager.StagingRoom),
                                flush: true, out _, out int tailBytes, out completed);
                bufferDataStager.Stage(tailBytes);
                if (!completed)
                    bufferDataStager.FlushStaged(kManagedBlockMaxPayloadSize);
            } while (!completed);
        }

        if (padBytes > 0)
            Unsafe.InitBlockUnaligned(bufferDataStager.Reserve(padBytes), 0, (uint)padBytes);
    }

    // Decimal-ASCII Int32 (== native IntToString) in the String wire shape. Framed
    // payload ≤16 bytes always fits after a flush, so no chunked arm. Not inlined:
    // IL2CPP would accumulate the stackalloc into the caller's frame (alloca is only
    // reclaimed on return).
    private static unsafe void WriteFramedDecimalInt32(NativeBufferContext* ctx, int value,
        ref BufferDataStager bufferDataStager)
    {
        // Build digits least-significant-first in a stack scratch, then emit in order.
        // long magnitude so Int32.MinValue is representable.
        bool negative = value < 0;
        long magnitude = negative ? -(long)value : value;
        byte* rev = stackalloc byte[10];             // up to 10 digits
        int d = 0;
        do
        {
            rev[d++] = (byte)('0' + (int)(magnitude % 10));
            magnitude /= 10;
        }
        while (magnitude > 0);

        int n = d + (negative ? 1 : 0);
        int padBytes = (4 - (n & 3)) & 3;
        int totalFramedSize = 4 + n + padBytes;

        // Framed payload <= 16 bytes always fits a window, so Reserve never spills.
        byte* dst = bufferDataStager.Reserve(totalFramedSize);
        Unsafe.WriteUnaligned(dst, n);
        int w = 4;
        if (negative)
            dst[w++] = (byte)'-';
        for (int i = d - 1; i >= 0; i--)
            dst[w++] = rev[i];
        if (padBytes > 0)
            Unsafe.InitBlockUnaligned(dst + 4 + n, 0, (uint)padBytes);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe string DecodeStringBody(byte* bytes, int length)
    {
        int firstZero = new ReadOnlySpan<byte>(bytes, length).IndexOf((byte)0);
        int effective = firstZero < 0 ? length : firstZero;

        if (effective == 0)
            return string.Empty;

        // Default replacement fallback: malformed subsequences become U+FFFD.
        return Encoding.UTF8.GetString(bytes, effective);
    }

    // Read-path mirror of ConsumeString. Consumes a ManagedCommandStringEntry
    // header from the entry stream, reads the framed wire payload (SInt32 length
    // prefix + UTF-8 body + 4-byte alignment padding), decodes the body via
    // DecodeStringBody, and assigns the result to the field at entry->fieldOffset.
    //
    // Two body paths:
    //   - Spill-buffer path (length <= ctx->stackBufferSize): EnsureReadable
    //     makes 'length' bytes contiguous at ctx->readerPtr (either already in
    //     the CachedReader's window or copied into the native stackBuffer).
    //     Decode happens in-place — zero managed allocation.
    //   - Large-string path (length > ctx->stackBufferSize): allocate byte[length],
    //     bulk-read via InvokeReadBytesDirect, then decode. The allocation is
    //     immediately collectible after the fixed block exits.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConsumeStringRead(
        NativeReadBufferContext* ctx, ref byte baseAddr, ref byte* pos)
    {
        var entry = (ManagedCommandStringEntry*)pos;
        pos += sizeof(ManagedCommandStringEntry);

        // Length prefix: 4-byte SInt32, little-endian.
        if (ctx->readerAvailable < 4)
            InvokeEnsureReadable(ctx, 4);
        int length = Unsafe.ReadUnaligned<int>(ctx->readerPtr);
        ctx->readerPtr      += 4;
        ctx->readerAvailable -= 4;

        // Reject negative wire lengths explicitly. Without this guard the
        // downstream ReadOnlySpan ctor in DecodeStringBody throws an opaque
        // ArgumentOutOfRangeException; this surfaces corruption at the
        // detection site with a meaningful message.
        if (length < 0)
            throw new InvalidOperationException(
                $"Managed string deserialization read a negative length prefix ({length}). The serialized data is corrupted.");

        int padBytes = (4 - (length & 3)) & 3;

        string result;
        if (length == 0)
        {
            // Wire-format invariant: length=0 → string.Empty (matches how the
            // writer encodes both null and empty source strings).
            result = string.Empty;
        }
        else if (length <= ctx->stackBufferSize)
        {
            // Spill-buffer path: decode in place. Zero allocation; zero P/Invoke
            // when the refill window already covers 'length' bytes (ensureReadable
            // no-ops).
            if (ctx->readerAvailable < length)
                InvokeEnsureReadable(ctx, length);
            result = DecodeStringBody(ctx->readerPtr, length);
            ctx->readerPtr      += length;
            ctx->readerAvailable -= length;
        }
        else
        {
            // Large-string path: body exceeds the spill buffer, so allocate a
            // one-shot byte[] sized to this string. GC'd when the local exits —
            // no pooling, no retention.
            byte[] buf = new byte[length];
            fixed (byte* bufPtr = buf)
            {
                InvokeReadBytesDirect(ctx, bufPtr, length);
                result = DecodeStringBody(bufPtr, length);
            }
        }

        Unsafe.As<byte, string>(
            ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset)) = result;

        // Skip 0-3 bytes of alignment padding.
        if (padBytes > 0)
        {
            if (ctx->readerAvailable < padBytes)
                InvokeEnsureReadable(ctx, padBytes);
            ctx->readerPtr      += padBytes;
            ctx->readerAvailable -= padBytes;
        }
    }

    // Reads a length-prefixed UTF-8 string. Editor PropertyName name read path only.
    private static unsafe string ReadFramedString(NativeReadBufferContext* ctx)
    {
        // Length prefix: 4-byte SInt32, little-endian.
        if (ctx->readerAvailable < 4)
            InvokeEnsureReadable(ctx, 4);
        int length = Unsafe.ReadUnaligned<int>(ctx->readerPtr);
        ctx->readerPtr      += 4;
        ctx->readerAvailable -= 4;

        if (length < 0)
            throw new InvalidOperationException(
                $"Managed PropertyName deserialization read a negative length prefix ({length}). The serialized data is corrupted.");

        int padBytes = (4 - (length & 3)) & 3;

        string result;
        if (length == 0)
        {
            result = string.Empty;
        }
        else if (length <= ctx->stackBufferSize)
        {
            if (ctx->readerAvailable < length)
                InvokeEnsureReadable(ctx, length);
            result = DecodeStringBody(ctx->readerPtr, length);
            ctx->readerPtr      += length;
            ctx->readerAvailable -= length;
        }
        else
        {
            byte[] buf = new byte[length];
            fixed (byte* bufPtr = buf)
            {
                InvokeReadBytesDirect(ctx, bufPtr, length);
                result = DecodeStringBody(bufPtr, length);
            }
        }

        // Skip 0-3 bytes of alignment padding.
        if (padBytes > 0)
        {
            if (ctx->readerAvailable < padBytes)
                InvokeEnsureReadable(ctx, padBytes);
            ctx->readerPtr      += padBytes;
            ctx->readerAvailable -= padBytes;
        }
        return result;
    }

    // Reads [SInt32 len][ascii digits][0..3 pad] and parses the decimal straight from the
    // wire into an Int32 — no managed string, no int.Parse. Avoids a per-field string
    // allocation, whose GC cost dominates on Mono/IL2CPP. long accumulator so
    // Int32.MinValue ("-2147483648") round-trips.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int ReadFramedDecimalInt32(NativeReadBufferContext* ctx)
    {
        if (ctx->readerAvailable < 4)
            InvokeEnsureReadable(ctx, 4);
        int length = Unsafe.ReadUnaligned<int>(ctx->readerPtr);
        ctx->readerPtr      += 4;
        ctx->readerAvailable -= 4;

        // An Int32 decimal is at most 11 bytes ("-2147483648"). Anything longer is corrupt;
        // rejecting it also keeps the digit loop inside the spill buffer (the reader caps a
        // fill at stackBufferSize, so an unbounded length would read out of bounds).
        if (length < 0 || length > 11)
            throw new InvalidOperationException(
                $"Managed PropertyName deserialization read an invalid decimal length prefix ({length}). The serialized data is corrupted.");

        // Writer always emits ≥1 digit (id 0 → "0"), so length 0 only arises from corruption; treat as 0.
        long magnitude = 0;
        bool negative = false;
        if (length > 0)
        {
            if (ctx->readerAvailable < length)
                InvokeEnsureReadable(ctx, length);
            byte* p = ctx->readerPtr;
            int i = 0;
            if (p[0] == (byte)'-')
            {
                negative = true;
                i = 1;
            }
            for (; i < length; i++)
                magnitude = magnitude * 10 + (p[i] - (byte)'0');
            ctx->readerPtr      += length;
            ctx->readerAvailable -= length;
        }

        int padBytes = (4 - (length & 3)) & 3;
        if (padBytes > 0)
        {
            if (ctx->readerAvailable < padBytes)
                InvokeEnsureReadable(ctx, padBytes);
            ctx->readerPtr      += padBytes;
            ctx->readerAvailable -= padBytes;
        }
        return negative ? (int)(-magnitude) : (int)magnitude;
    }

    // Read side of the PropertyName opcode. Reconstructs the PropertyName exactly as
    // SerializeTraits<PropertyName> does: id off the wire, or the name resolved in the editor.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConsumePropertyNameRead(
        NativeReadBufferContext* ctx, ref byte baseAddr, ref byte* pos)
    {
        var entry = (ManagedCommandPropertyNameEntry*)pos;
        pos += sizeof(ManagedCommandPropertyNameEntry);
        ref byte field = ref Unsafe.AddByteOffset(ref baseAddr, entry->fieldOffset);

        PropertyName pn;
        // serializesAsId == 0 means the editor persisted the resolved name; otherwise the id.
        if (entry->serializesAsId == 0)
            pn = new PropertyName(ReadFramedString(ctx));               // == PropertyNameFromString(s)
        else
            pn = new PropertyName(ReadFramedDecimalInt32(ctx));
        // Write the whole struct (8 B editor, 4 B player). In the editor the ctor sets
        // conflictIndex — resolved from the name, or zeroed from an id — matching the native read.
        Unsafe.WriteUnaligned(ref field, pn);
    }

}
