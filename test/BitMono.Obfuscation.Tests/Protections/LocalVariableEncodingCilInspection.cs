using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace BitMono.Obfuscation.Tests.Protections;

internal static class LocalVariableEncodingCilInspection
{
    internal static bool HasCompleteAffineCodec(ModuleDefinition module, string methodName)
    {
        var method = FindMethod(module, methodName);
        if (method.CilMethodBody is not { } body || module.RuntimeContext == null)
            return false;

        body.Instructions.ExpandMacros();
        var addressedVariables = GetAddressedVariables(body);
        var eligibleVariables = body.LocalVariables
            .Where(variable => !addressedVariables.Contains(variable))
            .Where(variable => GetLocalShape(module, variable).BitWidth != 0)
            .Where(variable => HasVariableAccess(body, variable))
            .ToArray();
        if (eligibleVariables.Length == 0)
            return false;

        foreach (var variable in eligibleVariables)
        {
            var shape = GetLocalShape(module, variable);
            int[] storeIndexes = GetVariableAccessIndexes(body, variable, IsStoreVariable);
            if (storeIndexes.Length == 0 ||
                !TryReadEncoding(body.Instructions, storeIndexes[0], shape, out var encoding) ||
                !IsValidEncoding(encoding))
                return false;

            foreach (int storeIndex in storeIndexes)
            {
                if (!TryReadEncoding(body.Instructions, storeIndex, shape, out var candidate) ||
                    candidate != encoding)
                    return false;
            }

            foreach (int loadIndex in GetVariableAccessIndexes(body, variable, IsLoadVariable))
            {
                if (!TryReadDecoding(body.Instructions, loadIndex, shape, out ulong addend,
                        out ulong inverseMultiplier, out ulong xorKey) ||
                    addend != encoding.Addend ||
                    xorKey != encoding.XorKey ||
                    unchecked(encoding.Multiplier * inverseMultiplier) is var product &&
                    (product & GetValueMask(shape.BitWidth)) != 1)
                    return false;
            }
        }

        return true;
    }

    internal static bool TryReadEncoding(
        CilInstructionCollection instructions,
        int storeIndex,
        (ElementType ElementType, int BitWidth) shape,
        out LocalVariableEncodingAffineEncoding encoding)
    {
        encoding = default;
        CilCode? conversionCode = GetStorageConversionCode(shape.ElementType);
        int firstIndex = storeIndex - 6 - (conversionCode == null ? 0 : 1);
        if (firstIndex < 0 ||
            !TryReadConstant(instructions[firstIndex], shape.BitWidth, out ulong xorKey) ||
            instructions[firstIndex + 1].OpCode.Code != CilCode.Xor ||
            !TryReadConstant(instructions[firstIndex + 2], shape.BitWidth, out ulong multiplier) ||
            instructions[firstIndex + 3].OpCode.Code != CilCode.Mul ||
            !TryReadConstant(instructions[firstIndex + 4], shape.BitWidth, out ulong addend) ||
            instructions[firstIndex + 5].OpCode.Code != CilCode.Add ||
            !HasExpectedConversion(instructions, firstIndex + 6, conversionCode))
            return false;

        encoding = new LocalVariableEncodingAffineEncoding(shape.BitWidth, xorKey, multiplier, addend);
        return true;
    }

    internal static bool TryReadDecoding(
        CilInstructionCollection instructions,
        int loadIndex,
        (ElementType ElementType, int BitWidth) shape,
        out ulong addend,
        out ulong inverseMultiplier,
        out ulong xorKey)
    {
        addend = 0;
        inverseMultiplier = 0;
        xorKey = 0;
        CilCode? conversionCode = GetStorageConversionCode(shape.ElementType);
        int firstIndex = loadIndex + 1;
        int lastIndex = firstIndex + 5 + (conversionCode == null ? 0 : 1);
        return lastIndex < instructions.Count &&
               TryReadConstant(instructions[firstIndex], shape.BitWidth, out addend) &&
               instructions[firstIndex + 1].OpCode.Code == CilCode.Sub &&
               TryReadConstant(instructions[firstIndex + 2], shape.BitWidth, out inverseMultiplier) &&
               instructions[firstIndex + 3].OpCode.Code == CilCode.Mul &&
               TryReadConstant(instructions[firstIndex + 4], shape.BitWidth, out xorKey) &&
               instructions[firstIndex + 5].OpCode.Code == CilCode.Xor &&
               HasExpectedConversion(instructions, firstIndex + 6, conversionCode);
    }

    internal static bool IsValidEncoding(LocalVariableEncodingAffineEncoding encoding)
    {
        ulong valueMask = GetValueMask(encoding.BitWidth);
        return encoding.XorKey != 0 &&
               (encoding.Multiplier & 1) == 1 &&
               (unchecked(encoding.XorKey * encoding.Multiplier + encoding.Addend) & valueMask) != 0;
    }

    internal static bool HasGenericEncodingShape(CilInstructionCollection instructions, int storeIndex)
    {
        int addIndex = storeIndex - 1;
        if (addIndex >= 0 && IsStorageConversion(instructions[addIndex].OpCode.Code))
            addIndex--;

        int firstIndex = addIndex - 5;
        return firstIndex >= 0 &&
               IsConstant(instructions[firstIndex]) &&
               instructions[firstIndex + 1].OpCode.Code == CilCode.Xor &&
               IsConstant(instructions[firstIndex + 2]) &&
               instructions[firstIndex + 3].OpCode.Code == CilCode.Mul &&
               IsConstant(instructions[firstIndex + 4]) &&
               instructions[firstIndex + 5].OpCode.Code == CilCode.Add;
    }

    internal static bool HasGenericDecodingShape(CilInstructionCollection instructions, int loadIndex)
    {
        int firstIndex = loadIndex + 1;
        return firstIndex + 5 < instructions.Count &&
               IsConstant(instructions[firstIndex]) &&
               instructions[firstIndex + 1].OpCode.Code == CilCode.Sub &&
               IsConstant(instructions[firstIndex + 2]) &&
               instructions[firstIndex + 3].OpCode.Code == CilCode.Mul &&
               IsConstant(instructions[firstIndex + 4]) &&
               instructions[firstIndex + 5].OpCode.Code == CilCode.Xor;
    }

    internal static bool TryReadConstant(CilInstruction instruction, int bitWidth, out ulong value)
    {
        value = 0;
        if (bitWidth == 64 && instruction.OpCode.Code == CilCode.Ldc_I8 && instruction.Operand is long longValue)
        {
            value = unchecked((ulong)longValue);
            return true;
        }

        if (bitWidth != 64 && instruction.OpCode.Code == CilCode.Ldc_I4 && instruction.Operand is int integerValue)
        {
            value = unchecked((uint)integerValue) & GetValueMask(bitWidth);
            return true;
        }

        return false;
    }

    internal static bool HasVariableAccess(CilMethodBody body, CilLocalVariable variable)
    {
        return body.Instructions.Any(instruction =>
            (IsLoadVariable(instruction) || IsStoreVariable(instruction)) &&
            ReferenceEquals(instruction.GetLocalVariable(body.LocalVariables), variable));
    }

    internal static int[] GetVariableAccessIndexes(
        CilMethodBody body,
        CilLocalVariable variable,
        Func<CilInstruction, bool> accessPredicate)
    {
        return Enumerable.Range(0, body.Instructions.Count)
            .Where(instructionIndex => accessPredicate(body.Instructions[instructionIndex]))
            .Where(instructionIndex => ReferenceEquals(
                body.Instructions[instructionIndex].GetLocalVariable(body.LocalVariables), variable))
            .ToArray();
    }

    internal static HashSet<CilLocalVariable> GetAddressedVariables(CilMethodBody body)
    {
        return body.Instructions
            .Where(instruction => instruction.OpCode.Code is CilCode.Ldloca or CilCode.Ldloca_S)
            .Select(instruction => instruction.Operand)
            .OfType<CilLocalVariable>()
            .ToHashSet();
    }

    internal static (ElementType ElementType, int BitWidth) GetLocalShape(
        ModuleDefinition module,
        CilLocalVariable variable)
    {
        var underlyingType = variable.VariableType.GetUnderlyingType(module.RuntimeContext!);
        int bitWidth = underlyingType.ElementType switch
        {
            ElementType.I1 or ElementType.U1 => 8,
            ElementType.Char or ElementType.I2 or ElementType.U2 => 16,
            ElementType.I4 or ElementType.U4 => 32,
            ElementType.I8 or ElementType.U8 => 64,
            _ => 0
        };
        return (underlyingType.ElementType, bitWidth);
    }

    internal static bool IsLoadVariable(CilInstruction instruction)
    {
        return instruction.OpCode.Code is CilCode.Ldloc or CilCode.Ldloc_S
            or CilCode.Ldloc_0 or CilCode.Ldloc_1 or CilCode.Ldloc_2 or CilCode.Ldloc_3;
    }

    internal static bool IsStoreVariable(CilInstruction instruction)
    {
        return instruction.OpCode.Code is CilCode.Stloc or CilCode.Stloc_S
            or CilCode.Stloc_0 or CilCode.Stloc_1 or CilCode.Stloc_2 or CilCode.Stloc_3;
    }

    internal static ulong GetValueMask(int bitWidth)
    {
        return bitWidth == 64 ? ulong.MaxValue : (1UL << bitWidth) - 1;
    }

    internal static MethodDefinition FindMethod(ModuleDefinition module, string methodName)
    {
        return module.GetAllTypes().SelectMany(type => type.Methods).Single(method => method.Name == methodName);
    }

    internal static byte[] ReadSerializedInstructions(string assemblyPath, string methodName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portableExecutableReader = new PEReader(stream);
        var metadataReader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(portableExecutableReader);
        var methodHandle = metadataReader.MethodDefinitions.Single(candidate =>
            metadataReader.GetString(metadataReader.GetMethodDefinition(candidate).Name) == methodName);
        int relativeVirtualAddress = metadataReader.GetMethodDefinition(methodHandle).RelativeVirtualAddress;
        relativeVirtualAddress.ShouldBeGreaterThan(0);
        return System.Reflection.Metadata.PEReaderExtensions
            .GetMethodBody(portableExecutableReader, relativeVirtualAddress)
            .GetILBytes()
            .ShouldNotBeNull();
    }

    private static bool HasExpectedConversion(
        CilInstructionCollection instructions,
        int instructionIndex,
        CilCode? conversionCode)
    {
        return conversionCode == null ||
               instructionIndex < instructions.Count &&
               instructions[instructionIndex].OpCode.Code == conversionCode.Value;
    }

    private static CilCode? GetStorageConversionCode(ElementType elementType)
    {
        return elementType switch
        {
            ElementType.I1 => CilCode.Conv_I1,
            ElementType.U1 => CilCode.Conv_U1,
            ElementType.I2 => CilCode.Conv_I2,
            ElementType.Char or ElementType.U2 => CilCode.Conv_U2,
            _ => null
        };
    }

    private static bool IsStorageConversion(CilCode code)
    {
        return code is CilCode.Conv_I1 or CilCode.Conv_U1 or CilCode.Conv_I2 or CilCode.Conv_U2;
    }

    private static bool IsConstant(CilInstruction instruction)
    {
        return instruction.OpCode.Code is CilCode.Ldc_I4 or CilCode.Ldc_I8;
    }
}
