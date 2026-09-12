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
    // Wrapper construction path for SimpleNativeType reads in ExecuteReadCommands.
    // For SimpleNativeType wrappers the parameterless ctor is what allocates the
    // native peer, so running it is required to get a usable m_Ptr afterwards.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe object CreateWrapperInstance(IntPtr runtimeTypeHandle, IntPtr ctorFunctionPtr)
    {
        // ctorFunctionPtr is baked at build time on every backend now that the
        // native ResolveParameterlessCtorFunctionPointer passes kConstructor to
        // the method lookup (CoreCLR included), so no per-backend fallback is
        // needed here. Zero still means the type has no parameterless ctor.
        Type type = UnmarshalSystemType(runtimeTypeHandle);
        object obj = RuntimeHelpers.GetUninitializedObject(type);
        if (ctorFunctionPtr != IntPtr.Zero)
        {
            try
            {
                ((delegate*<object, void>)ctorFunctionPtr)(obj);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        return obj;
    }

    // Takes the individual fields rather than a ValueReferenceHeader* so the
    // gather pass (ProcessGatherRecurseClass) can reuse the same materialization
    // logic with its own GatherRecurseClassEntry layout — the field set
    // (fieldOffset / runtimeTypeHandle / ctorFunctionPtr) is identical between
    // the two opcode spaces, only the surrounding struct layout differs.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe object GetOrCreateVrtInstance(
        ref byte baseAddr, uint fieldOffset, IntPtr runtimeTypeHandle, IntPtr ctorFunctionPtr)
    {
        ref object slot = ref Unsafe.As<byte, object>(
            ref Unsafe.AddByteOffset(ref baseAddr, (nint)fieldOffset));
        object obj = slot;
        if (obj != null)
            return obj;

        Type type = UnmarshalSystemType(runtimeTypeHandle);
        if (type == null)
            return null;
        // ctorFunctionPtr is baked at build time on every backend (see
        // CreateWrapperInstance); zero means the type has no parameterless ctor.
        obj = RuntimeHelpers.GetUninitializedObject(type);
        if (ctorFunctionPtr != IntPtr.Zero)
        {
            try
            {
                ((delegate*<object, void>)ctorFunctionPtr)(obj);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        slot = obj;
        return obj;
    }

    // Consumes a ValueReferenceType entry and recurses into ExecuteWriteCommands
    // with the inner instance pinned as the source. The body's own leading FBP(N)
    // segment markers drive buffer claims and flushes.
    //
    // runtimeTypeHandle discriminates the encoding:
    //   - Non-zero (class field): resolve via GetOrCreateVrtInstance and pin
    //     ObjectWrapper.Data as the offset-zero source.
    //   - Zero (struct field): struct lives inline at baseAddr + fieldOffset;
    //     the outer caller's pin on the containing GC object covers this
    //     recursion, so just shift the base and recurse.
    //
    // ctorFunctionPtr is not the sentinel: a class whose parameterless ctor
    // lookup fails also stamps a zero ctorFunctionPtr and would alias as a
    // struct. runtimeTypeHandle (raw MonoType* / MethodTable*) is non-zero
    // for every real class.
    private static unsafe void ConsumeValueReference(
        NativeBufferContext* ctx, ref byte baseAddr, IntPtr transfer,
        ref byte* output, ref BufferDataStager bufferDataStager, ref byte* pos)
    {
        var header = (ValueReferenceHeader*)pos;
        pos += sizeof(ValueReferenceHeader);
        byte* nestedStart = pos;
        int nestedBytes = (int)header->nestedByteCount;

        if (nestedBytes == 0)
        {
            pos = nestedStart;
            // A class with no serialized leaves must still be materialized (null ->
            // default-constructed) to match native null-slot population; structs need nothing.
            if (header->runtimeTypeHandle != IntPtr.Zero)
                GetOrCreateVrtInstance(ref baseAddr, header->fieldOffset, header->runtimeTypeHandle, header->ctorFunctionPtr);
            return;
        }

        if (header->runtimeTypeHandle == IntPtr.Zero)
        {
            // Struct: inline at baseAddr + fieldOffset. Re-pinning via
            // `fixed (byte* p = &baseAddr)` would create an IL slot the GC
            // root-scan walks as an interior pointer — crashes when baseAddr
            // was reconstructed from a raw IntPtr.
            ref byte nestedBase = ref Unsafe.AddByteOffset(ref baseAddr, header->fieldOffset);
            ExecuteWriteCommands(ctx,
                (IntPtr)Unsafe.AsPointer(ref nestedBase),
                (IntPtr)nestedStart, nestedBytes, transfer,
                ref output, ref bufferDataStager, repeatCount: 1, repeatStride: 0);
        }
        else
        {
            // Class: ObjectWrapper.Data is the offset-zero reference for nested fieldOffsets.
            object obj = GetOrCreateVrtInstance(ref baseAddr, header->fieldOffset, header->runtimeTypeHandle, header->ctorFunctionPtr);
            fixed (byte* nestedBase = &Unsafe.As<ObjectWrapper>(obj).Data)
            {
                ExecuteWriteCommands(ctx, (IntPtr)nestedBase,
                    (IntPtr)nestedStart, nestedBytes, transfer, ref output, ref bufferDataStager, repeatCount: 1, repeatStride: 0);
            }
        }

        pos = nestedStart + nestedBytes;
    }

    // -----------------------------------------------------------------------
    // Gather pass — pre-write walker
    //
    // Walks the parallel gather byte stream emitted by the native build side
    // (see RttiGatherOp in SerializationCommands.h). For each entry:
    //
    //   - Register{Ref,RefArray,RefList}: read the [SerializeReference]
    //     object reference(s) at base+fieldOffset and hand each non-null
    //     reference to the native registry through registerRefFnPtr.
    //   - Recurse{Class,Struct}{,Array,List}: descend into a non-ref class
    //     or struct field; class variants null-materialize the field via
    //     runtimeTypeHandle + ctorFunctionPtr (mirroring GetOrCreateVrtInstance)
    //     so that constructor-initialized [SerializeReference] fields aren't
    //     missed when the parent ctor populates them.
    //   - InvokeOnBeforeSerialize{Class,Struct}: fire the user's
    //     ISerializationCallbackReceiver.OnBeforeSerialize callback so any
    //     [SerializeReference] fields set up in the callback are visible to
    //     the subsequent Register entries in this subtree. The write pass
    //     skips its own OnBeforeSerialize invocations whenever a gather pass
    //     ran for the root (the native side flips IsGatherCompleted on the
    //     ManagedReferencesTransferState).
    //
    // All recurse entries store a uint childCount = number of nested gather
    // entries that follow them; the walker advances by entry size as it
    // dispatches and uses childCount to bound the nested recursion. For
    // arrays / lists the nested block is walked once per element with a
    // per-element base; the byte cursor is rewound to nestedStart before each
    // iteration and ends at the end of the nested block after the last
    // iteration, so the outer loop in GatherWalkN can continue from there.
    // SkipGatherEntries handles the "field is null / collection is empty"
    // edge case by walking the same byte structure without executing.


    // Read-path mirror of ConsumeValueReference. The inner body is its own
    // self-contained FBP(N) segment chain, so ExecuteReadCommands gets a fresh
    // innerSegmentSize=0; its end-of-stream commit advances readerPtr past the
    // body's final segment before we return. Same class / struct split — see
    // ConsumeValueReference.
    private static unsafe void ConsumeValueReferenceRead(
        NativeReadBufferContext* ctx, ref byte baseAddr, IntPtr transfer, ref byte* pos)
    {
        var header = (ValueReferenceHeader*)pos;
        pos += sizeof(ValueReferenceHeader);
        byte* nestedStart = pos;
        int nestedBytes = (int)header->nestedByteCount;

        if (nestedBytes == 0)
        {
            pos = nestedStart;
            // A class with no serialized leaves must still be materialized (null ->
            // default-constructed) to match native null-slot population; structs need nothing.
            if (header->runtimeTypeHandle != IntPtr.Zero)
                GetOrCreateVrtInstance(ref baseAddr, header->fieldOffset, header->runtimeTypeHandle, header->ctorFunctionPtr);
            return;
        }

        if (header->runtimeTypeHandle == IntPtr.Zero)
        {
            // Struct: inline; outer pin covers this recursion (see ConsumeValueReference).
            ref byte nestedBase = ref Unsafe.AddByteOffset(ref baseAddr, header->fieldOffset);
            int innerSegmentSize = 0;
            ExecuteReadCommands(
                ctx,
                ref nestedBase,
                nestedStart, nestedBytes,
                transfer,
                ref innerSegmentSize,
                repeatCount: 1, repeatStride: 0);
        }
        else
        {
            // Class: `fixed` pins the inner instance across native P/Invokes in the recursion.
            object obj = GetOrCreateVrtInstance(ref baseAddr, header->fieldOffset, header->runtimeTypeHandle, header->ctorFunctionPtr);
            fixed (byte* nestedBase = &Unsafe.As<ObjectWrapper>(obj).Data)
            {
                int innerSegmentSize = 0;
                ExecuteReadCommands(
                    ctx,
                    ref Unsafe.AsRef<byte>(nestedBase),
                    nestedStart, nestedBytes,
                    transfer,
                    ref innerSegmentSize,
                    repeatCount: 1, repeatStride: 0);
            }
        }

        pos = nestedStart + nestedBytes;
    }

}
