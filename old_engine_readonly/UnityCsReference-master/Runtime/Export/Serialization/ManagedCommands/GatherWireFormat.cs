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

// NOTE: This enum must be kept in sync with RttiGatherOp in
// Runtime/Mono/SerializationBackend_DirectMemoryAccess/SerializationCommands.h.
// See the native header for the per-opcode wire / executor contract.
internal enum RttiGatherOp : byte
{
    RegisterRef                      = 0,
    RegisterRefArray                 = 1,
    RegisterRefList                  = 2,
    RecurseClass                     = 3,
    RecurseStruct                    = 4,
    RecurseClassArray                = 5,
    RecurseClassList                 = 6,
    RecurseStructArray               = 7,
    RecurseStructList                = 8,
    InvokeOnBeforeSerializeClass     = 9,
    InvokeOnBeforeSerializeStruct    = 10,
    RecurseDictionary                = 11,

    Unknown                          = 0xFF,
}

// Mirrors of native gather entry structs in SerializationCommands.h. Natural
// sequential layout matches the native side exactly. Reserved bytes pad each
// opcode to its declared size so the gather byte stream stays 4-byte aligned;
// entries with IntPtr fields also pad to 8-byte alignment for the pointer.

internal struct GatherRegisterRefEntry  // 8 + sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public IntPtr       propertyPathTemplate;  // baked template, resolved by MoveToBuffer's gather fixup pass
}

internal struct GatherRegisterRefArrayEntry  // 8 + sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public IntPtr       propertyPathTemplate;
}

internal struct GatherRegisterRefListEntry  // 8 + sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public IntPtr       propertyPathTemplate;
}

internal struct GatherRecurseClassEntry  // 16 + 2*sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public uint         nestedByteCount;
    public uint         reserved3;
    public IntPtr       runtimeTypeHandle;
    public IntPtr       ctorFunctionPtr;
}

internal struct GatherRecurseStructEntry  // 12 bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public uint         nestedByteCount;
}

internal struct GatherRecurseClassArrayEntry  // 16 + 2*sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public uint         nestedByteCount;
    public uint         reserved3;
    public IntPtr       runtimeTypeHandle;
    public IntPtr       ctorFunctionPtr;
}

internal struct GatherRecurseStructArrayEntry  // 16 bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public uint         nestedByteCount;
    public uint         elementSize;
}

internal struct GatherRecurseClassListEntry  // 16 + 2*sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public uint         nestedByteCount;
    public uint         reserved3;  // explicit pad — see native GatherRecurseClassListEntry::_pad2
    public IntPtr       runtimeTypeHandle;
    public IntPtr       ctorFunctionPtr;
}

internal struct GatherRecurseStructListEntry  // 16 bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public uint         nestedByteCount;
    public uint         elementSize;
}

internal struct GatherInvokeOnBeforeSerializeClassEntry  // 8 + sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         reserved3;
    public IntPtr       methodFnPtr;
}

internal struct GatherInvokeOnBeforeSerializeStructEntry  // 8 + sizeof(IntPtr) bytes
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         reserved3;
    public IntPtr       methodFnPtr;
}

internal struct GatherRecurseDictionaryEntry  // 16 + sizeof(IntPtr) bytes (24 on 64-bit)
{
    public RttiGatherOp opCode;
    public byte         reserved0;
    public byte         reserved1;
    public byte         reserved2;
    public uint         fieldOffset;
    public uint         nestedByteCount;
    public uint         elementSize;
    public IntPtr       propertyPathTemplate;  // baked dict-path FUID template; resolved by MoveToBuffer's gather fixup pass
}
