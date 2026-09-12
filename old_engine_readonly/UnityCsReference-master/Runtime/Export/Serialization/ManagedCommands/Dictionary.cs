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
    // Rounds a byte count up to the entry stream's 4-byte alignment.
    private static uint AlignUp4(uint byteCount) => (byteCount + 3u) & ~3u;

    // Consumes one ManagedCommandDictionary entry. Bridges the live
    // Dictionary<K,V> to a SerializedKeyValue<K,V>[] via the existing managed
    // helper, then walks the per-entry FBP-bracketed body once per entry
    // against the entry-pinned base (same shape as ConsumeLinearCollection's
    // per-element-recursion path).
    //
    // FUID stack bracketing: PushDictionaryFUIDFrame installs the dict's
    // FieldUniqueIdentifierContext for descendant FormatDictionaryFieldUniqueIdentifierForActiveContext
    // calls, then Pop on the finally arm. Editor-only behavior; player builds
    // get inline no-op stubs from the native header (Push always returns false,
    // Pop is a nop), so the try/finally is a cheap pair of icalls + a branch.
    //
    // Null dictionary → write a 0 length prefix and return; matches the
    // legacy auto-empty-on-write behavior in TransferField_Dictionary.
    private static unsafe void ConsumeDictionary(
        NativeBufferContext* ctx, ref byte baseAddr, IntPtr transfer,
        ref byte* output, ref BufferDataStager bufferDataStager, ref byte* pos)
    {
        var header = (DictionaryHeaderWrite*)pos;
        pos += sizeof(DictionaryHeaderWrite);
        byte* nestedStart = pos;
        int   nestedBytes = (int)header->nestedByteCount;

        // Editor-only FUID template lives inline right after the body: it starts at
        // bodyEnd, and the next entry follows it, 4-byte aligned.
        byte* bodyEnd     = nestedStart + nestedBytes;
        int   fuidAdvance = (int)AlignUp4(header->fuidTemplateByteCount);
        IntPtr fuidTemplate = header->fuidTemplateByteCount != 0 ? (IntPtr)bodyEnd : IntPtr.Zero;

        // Field at (baseAddr + fieldOffset) holds a Dictionary<K,V> reference.
        object dictRef = Unsafe.As<byte, object>(
            ref Unsafe.AddByteOffset(ref baseAddr, (nint)header->fieldOffset));
        if (dictRef == null)
        {
            Unsafe.WriteUnaligned(bufferDataStager.Reserve(4), 0);
            pos = bodyEnd + fuidAdvance;
            return;
        }

        bool pushed = SerializationBackendManagedCommands.PushDictionaryFUIDFrame(ctx->fuidContext);
        try
        {
            // Bridge live dict → SerializedKeyValue<K,V>[]. The helper handles
            // duplicate-row merging when the host has stored duplicates and
            // dictionaryIdentifierTemplateUtf8 is non-null; otherwise it just
            // walks the live dict. InvokeGetEntriesTyped uses the build-time
            // interned closed delegate via getEntriesTypedIndex to avoid the
            // per-call dict.GetType() + GetGenericArguments() + ConcurrentDictionary
            // lookup; falls back to the non-typed path when the index is -1.
            Array entries = DictionarySerialization.InvokeGetEntriesTyped(
                header->getEntriesTypedIndex,
                ctx->hostingEntityId, dictRef, fuidTemplate);

            int count = entries?.Length ?? 0;
            // Stage the count; per-entry bodies coalesce after it via the FBP threading below.
            Unsafe.WriteUnaligned(bufferDataStager.Reserve(4), count);

            if (count > 0)
            {
                // Same shape as ConsumeLinearCollection's per-element arm.
                byte[] dataAsBytes = Unsafe.As<byte[]>(entries);
                fixed (byte* dataPtr = dataAsBytes)
                {
                    long stride = (long)header->entryStride;
                    // Push AFTER InvokeGetEntriesTyped so the dict's own duplicate-storage
                    // key (formatted inside that call) never sees the per-entry index.
                    bool pushedEntryIdx = ctx->fuidContext != IntPtr.Zero;
                    if (pushedEntryIdx)
                        PushFUIDArrayIndex(ctx->fuidContext, 0);
                    try
                    {
                        ExecuteWriteCommands(ctx, (IntPtr)dataPtr,
                            (IntPtr)nestedStart, nestedBytes, transfer,
                            ref output, ref bufferDataStager,
                            repeatCount: count, repeatStride: stride,
                            fuidCtxForElements: ctx->fuidContext);
                    }
                    finally
                    {
                        if (pushedEntryIdx)
                            PopFUIDArrayIndex(ctx->fuidContext);
                    }
                }
            }

            // 0..3 byte aggregate alignment pad, staged after the last entry so the count,
            // entry bodies, and pad all coalesce and ride the surrounding flow's flushes
            // (same as ConsumeLinearCollection's per-element arm). The pad comes from the
            // per-entry wire width; 0 means self-aligned entries and no pad.
            int entryWireSize = (int)header->entryWireSize;
            if (entryWireSize > 0)
            {
                int totalWritten = count * entryWireSize;
                int padBytes     = (4 - (totalWritten & 3)) & 3;
                if (padBytes > 0)
                    Unsafe.InitBlockUnaligned(bufferDataStager.Reserve(padBytes), 0, (uint)padBytes);
            }
        }
        finally
        {
            if (pushed)
                SerializationBackendManagedCommands.PopDictionaryFUIDFrame();
        }

        pos = bodyEnd + fuidAdvance;
    }


    // Read-path mirror of ConsumeDictionary. Reads the count prefix and per-entry
    // body (same shape ConsumeLinearCollectionRead's per-element-recursion path
    // produces) into a SerializedKeyValue<K,V>[] staging array, then calls
    // DictionarySerialization.SetEntriesFromSerializedData to populate the live
    // dictionary and store any duplicate-key entries in the Editor cache.
    //
    // FUID bracketing: PushDictionaryFUIDFrame installs the dict's
    // FieldUniqueIdentifierContext so FormatDictionaryFieldUniqueIdentifier
    // (which walks the FUID stack) produces the canonical dict path used as
    // the duplicate-row storage key — must match what the legacy DictionaryField::SetArray
    // produces (DictionaryField.cpp:142-144) so write→read round-trips through
    // the cache are stable.
    private static unsafe void ConsumeDictionaryRead(
        NativeReadBufferContext* ctx,
        ref byte baseAddr,
        IntPtr transfer,
        ref byte* pos)
    {
        var header = (DictionaryHeaderRead*)pos;
        pos += sizeof(DictionaryHeaderRead);
        byte* nestedStart = pos;
        int   nestedBytes = (int)header->nestedByteCount;

        // Editor-only FUID template inline after the body (see ConsumeDictionary).
        byte* bodyEnd     = nestedStart + nestedBytes;
        int   fuidAdvance = (int)AlignUp4(header->fuidTemplateByteCount);
        IntPtr fuidTemplate = header->fuidTemplateByteCount != 0 ? (IntPtr)bodyEnd : IntPtr.Zero;

        // Count prefix — same framing as ConsumeLinearCollectionRead.
        if (ctx->readerAvailable < 4)
            InvokeEnsureReadable(ctx, 4);
        int count = Unsafe.ReadUnaligned<int>(ctx->readerPtr);
        ctx->readerPtr      += 4;
        ctx->readerAvailable -= 4;

        // Allocate the staging entries array. elementTypeHandle was stamped at
        // build time with the SerializedKeyValue<K,V> RuntimeTypeHandle.Value;
        // UnmarshalSystemType handles the Mono/IL2CPP vs CoreCLR backend split.
        Type entryType = UnmarshalSystemType(header->elementTypeHandle);
        Array entries  = Array.CreateInstance(entryType, count);

        bool pushed = PushDictionaryFUIDFrame(ctx->fuidContext);
        try
        {
            if (count > 0)
            {
                // Per-entry recursion: each entry's FBP-bracketed body walked
                // by ExecuteReadCommands with the entry pinned — same shape as
                // ConsumeLinearCollectionRead's per-element-recursion arm.
                byte[] dataAsBytes = Unsafe.As<byte[]>(entries);
                fixed (byte* dataPtr = dataAsBytes)
                {
                    long stride  = (long)header->entryStride;
                    int  segSize = 0;
                    // Push BEFORE ExecuteReadCommands so descendant dict templates
                    // with %d resolve the per-entry index. Pop IMMEDIATELY after —
                    // FormatDictionaryFieldUniqueIdentifier (below) formats the
                    // dict's OWN key and must not see the per-entry index.
                    bool pushedEntryIdx = ctx->fuidContext != IntPtr.Zero;
                    if (pushedEntryIdx)
                        PushFUIDArrayIndex(ctx->fuidContext, 0);
                    try
                    {
                        ExecuteReadCommands(
                            ctx,
                            ref Unsafe.AsRef<byte>(dataPtr),
                            nestedStart, nestedBytes,
                            transfer,
                            ref segSize,
                            repeatCount: count, repeatStride: stride,
                            fuidCtxForElements: ctx->fuidContext);
                    }
                    finally
                    {
                        if (pushedEntryIdx)
                            PopFUIDArrayIndex(ctx->fuidContext);
                    }
                }

                // Skip the tail pad the writer emitted, off the same wire width.
                int entryWireSize = (int)header->entryWireSize;
                if (entryWireSize > 0)
                {
                    int totalBytes = count * entryWireSize;
                    int padBytes   = (4 - (totalBytes & 3)) & 3;
                    if (padBytes > 0)
                    {
                        if (ctx->readerAvailable < padBytes)
                            InvokeEnsureReadable(ctx, padBytes);
                        ctx->readerPtr      += padBytes;
                        ctx->readerAvailable -= padBytes;
                    }
                }
            }

            // Field at (baseAddr + fieldOffset) holds a Dictionary<K,V> reference.
            // Default-allocate when the live reference is null so the deserialized
            // host has a usable (possibly empty) dictionary instead of a null field --
            // matches legacy DictionaryField's ctor at DictionaryField.cpp:100-102
            // ("Default-allocate the dictionary if the field is null, matching
            // List<T>/array behavior"). The Func<object> factory was interned
            // once at build time by DictionarySerialization.InternDictionaryDefaultAllocateFactory
            // and the integer index stamped into the dict header; here we pull it
            // back via SerializationCommandObjectTable and invoke directly -- no
            // execute-time reflection, no hash lookup, no per-call generic dispatch.
            // Index -1 means the helper was unavailable at build time; leave the
            // field null in that case.
            ref byte dictSlot = ref Unsafe.AddByteOffset(ref baseAddr, (nint)header->fieldOffset);
            object dictRef = Unsafe.As<byte, object>(ref dictSlot);
            if (dictRef == null && header->dictDefaultAllocateFactoryIndex >= 0)
            {
                var factory = (Func<object>)SerializationCommandObjectTable.Get(header->dictDefaultAllocateFactoryIndex);
                dictRef = factory();
                Unsafe.As<byte, object>(ref dictSlot) = dictRef;
            }
            if (dictRef != null)
            {
                // Resolve the dict's canonical identifier so SetEntriesFromSerializedData
                // can key the duplicate-row cache by it. Empty when no FUID context or
                // no template — duplicate-row tracking simply doesn't apply in those cases.
                // [FreeFunction] unavailable in UNITY_NATIVE_TEST_RESOURCES; the
                // diagnostic is non-essential, so it stays empty there.
                string dictionaryIdentifier = string.Empty;
                if (ctx->hostingEntityId != EntityId.None
                    && fuidTemplate != IntPtr.Zero)
                {
                    dictionaryIdentifier = FormatDictionaryFieldUniqueIdentifier(
                        fuidTemplate) ?? string.Empty;
                }

                // InvokeSetEntriesTyped uses the build-time interned closed
                // delegate via setEntriesTypedIndex to avoid the per-call
                // dict.GetType() + ConcurrentDictionary lookup; falls back to
                // the non-typed SetEntriesFromSerializedData entry point when
                // the index is -1.
                DictionarySerialization.InvokeSetEntriesTyped(
                    header->setEntriesTypedIndex,
                    ctx->hostingEntityId, dictRef, entries, dictionaryIdentifier, ctx->warnAboutIgnoredEntries);
            }
        }
        finally
        {
            if (pushed)
                PopDictionaryFUIDFrame();
        }

        pos = bodyEnd + fuidAdvance;
    }

}
