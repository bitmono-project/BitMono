using System;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using static BitMono.Obfuscation.Tests.Protections.LocalVariableEncodingCilInspection;

namespace BitMono.Obfuscation.Tests.Protections;

internal static class LocalVariableEncodingCilAssertions
{
    internal static readonly string[] EligibleMethodNames =
    {
        "IntegralBoundaries", "EnumLocal", "RepeatedWrites", "TwoLiveLocals", "BranchMerge",
        "LoopAccumulation", "ExceptionFlow", "ManyLocalSlots", "AddressTaken"
    };

    internal static readonly string[] UnsupportedMethodNames =
    {
        "ReferenceLocal", "FloatingPointLocal", "StructureLocal"
    };

    private static readonly string[] InjectedUnsupportedMethodNames =
    {
        "UnsupportedIntPtrLocal", "UnsupportedUIntPtrLocal", "UnsupportedPointerLocal",
        "UnsupportedByReferenceLocal", "UnsupportedPinnedByReferenceLocal",
        "UnsupportedMethodGenericParameterLocal"
    };

    internal static void AssertDeterministicCodecs(ModuleDefinition module)
    {
        string[] requiredMethodNames = EligibleMethodNames
            .Concat(new[] { "DefaultInitializedLocal", "BranchTargetStore", "ExceptionBoundaryLocal" })
            .ToArray();
        foreach (string methodName in requiredMethodNames)
            HasCompleteAffineCodec(module, methodName).ShouldBeTrue(
                $"deterministic protected method {methodName} must contain the complete codec");

        int encodedVariableCount = 0;
        foreach (var method in module.GetAllTypes().SelectMany(type => type.Methods))
        {
            if (method.CilMethodBody is not { } body)
                continue;

            body.Instructions.ExpandMacros();
            foreach (var variable in body.LocalVariables)
            {
                var shape = GetLocalShape(module, variable);
                if (shape.BitWidth == 0)
                    continue;

                int[] storeIndexes = GetVariableAccessIndexes(body, variable, IsStoreVariable);
                LocalVariableEncodingAffineEncoding? expectedEncoding = null;
                foreach (int storeIndex in storeIndexes)
                {
                    if (!TryReadEncoding(body.Instructions, storeIndex, shape, out var encoding))
                        continue;

                    AssertDeterministicEncoding(encoding);
                    if (expectedEncoding == null)
                        expectedEncoding = encoding;
                    else
                        encoding.ShouldBe(expectedEncoding.Value);
                }

                if (expectedEncoding == null)
                    continue;

                encodedVariableCount++;
                foreach (int storeIndex in storeIndexes)
                {
                    TryReadEncoding(body.Instructions, storeIndex, shape, out var encoding).ShouldBeTrue(
                        $"deterministic codec store in {method.FullName}");
                    encoding.ShouldBe(expectedEncoding.Value);
                }

                foreach (int loadIndex in GetVariableAccessIndexes(body, variable, IsLoadVariable))
                {
                    TryReadDecoding(body.Instructions, loadIndex, shape, out ulong addend,
                        out ulong inverseMultiplier, out ulong xorKey).ShouldBeTrue(
                        $"deterministic codec load in {method.FullName}");
                    xorKey.ShouldBe(1UL);
                    addend.ShouldBe(GetValueMask(shape.BitWidth) - 1);
                    inverseMultiplier.ShouldBe(GetExpectedInverseMultiplier(shape.BitWidth));
                }
            }
        }

        encodedVariableCount.ShouldBeGreaterThan(0);
    }

    internal static void AssertDefaultInitializedLocalCodec(
        ModuleDefinition baselineModule,
        ModuleDefinition protectedModule)
    {
        var baselineMethod = FindMethod(baselineModule, "DefaultInitializedLocal");
        var baselineBody = baselineMethod.CilMethodBody.ShouldNotBeNull();
        baselineBody.InitializeLocals.ShouldBeTrue();
        baselineBody.Instructions.ExpandMacros();
        baselineBody.LocalVariables.Count.ShouldBe(1);
        baselineBody.Instructions.Count.ShouldBe(2);
        baselineBody.Instructions[0].OpCode.Code.ShouldBe(CilCode.Ldloc);
        baselineBody.Instructions[0].GetLocalVariable(baselineBody.LocalVariables)
            .ShouldBeSameAs(baselineBody.LocalVariables[0]);
        baselineBody.Instructions[1].OpCode.Code.ShouldBe(CilCode.Ret);

        var method = FindMethod(protectedModule, "DefaultInitializedLocal");
        var body = method.CilMethodBody.ShouldNotBeNull();
        body.InitializeLocals.ShouldBeTrue();
        body.Instructions.ExpandMacros();
        body.LocalVariables.Count.ShouldBe(1);

        var variable = body.LocalVariables[0];
        var shape = GetLocalShape(protectedModule, variable);
        int[] storeIndexes = GetVariableAccessIndexes(body, variable, IsStoreVariable);
        int[] loadIndexes = GetVariableAccessIndexes(body, variable, IsLoadVariable);
        storeIndexes.Length.ShouldBe(1, "entry initialization must add exactly one encoded store");
        loadIndexes.Length.ShouldBe(1, "the original default-value access must remain exactly once");
        storeIndexes[0].ShouldBeLessThan(loadIndexes[0],
            "encoded initialization store must precede the original local access");

        TryReadEncoding(body.Instructions, storeIndexes[0], shape, out var encoding).ShouldBeTrue(
            "entry initialization store must contain the complete affine encoder");
        IsValidEncoding(encoding).ShouldBeTrue();
        int plaintextZeroIndex = storeIndexes[0] - 7;
        plaintextZeroIndex.ShouldBeGreaterThanOrEqualTo(0);
        TryReadConstant(body.Instructions[plaintextZeroIndex], shape.BitWidth, out ulong plaintextValue)
            .ShouldBeTrue();
        plaintextValue.ShouldBe(0UL, "entry initialization must encode the CLR default value");

        TryReadDecoding(body.Instructions, loadIndexes[0], shape, out ulong addend,
            out ulong inverseMultiplier, out ulong xorKey).ShouldBeTrue(
            "the original default-value load must contain the complete affine decoder");
        addend.ShouldBe(encoding.Addend);
        xorKey.ShouldBe(encoding.XorKey);
        (unchecked(encoding.Multiplier * inverseMultiplier) & GetValueMask(shape.BitWidth)).ShouldBe(1UL);
        HasCompleteAffineCodec(protectedModule, "DefaultInitializedLocal").ShouldBeTrue();
    }

    internal static void AssertUninitializedLocalBypass(
        ModuleDefinition baselineModule,
        ModuleDefinition protectedModule)
    {
        var baselineMethod = FindMethod(baselineModule, "UninitializedLocal");
        var protectedMethod = FindMethod(protectedModule, "UninitializedLocal");
        var baselineBody = baselineMethod.CilMethodBody.ShouldNotBeNull();
        var protectedBody = protectedMethod.CilMethodBody.ShouldNotBeNull();
        baselineBody.InitializeLocals.ShouldBeFalse();
        protectedBody.InitializeLocals.ShouldBeFalse();
        baselineBody.Instructions.ExpandMacros();
        protectedBody.Instructions.ExpandMacros();

        baselineBody.LocalVariables.Count.ShouldBe(1);
        protectedBody.LocalVariables.Count.ShouldBe(1);
        protectedBody.Instructions.Count.ShouldBe(baselineBody.Instructions.Count,
            "InitializeLocals=false method instruction count must be unchanged");

        for (int instructionIndex = 0; instructionIndex < baselineBody.Instructions.Count; instructionIndex++)
        {
            var baselineInstruction = baselineBody.Instructions[instructionIndex];
            var protectedInstruction = protectedBody.Instructions[instructionIndex];
            protectedInstruction.OpCode.Code.ShouldBe(baselineInstruction.OpCode.Code,
                $"InitializeLocals=false opcode at index {instructionIndex}");
            AssertEquivalentOperand(
                baselineInstruction.Operand,
                protectedInstruction.Operand,
                baselineBody,
                protectedBody,
                instructionIndex);
        }

        var protectedVariable = protectedBody.LocalVariables[0];
        AssertVariableHasNoCodec(protectedBody, protectedVariable, "UninitializedLocal");
    }

    internal static void AssertBranchTargetStoreCodec(
        ModuleDefinition baselineModule,
        ModuleDefinition protectedModule)
    {
        var baselineMethod = FindMethod(baselineModule, "BranchTargetStore");
        var baselineBody = baselineMethod.CilMethodBody.ShouldNotBeNull();
        baselineBody.Instructions.ExpandMacros();
        baselineBody.LocalVariables.Count.ShouldBe(1);
        var baselineBranch = baselineBody.Instructions.Single(
            instruction => instruction.OpCode.Code == CilCode.Br);
        var baselineStore = baselineBody.Instructions.Single(IsStoreVariable);
        baselineBranch.Operand.ShouldBeOfType<CilInstructionLabel>();
        var baselineTarget = ((CilInstructionLabel)baselineBranch.Operand!).Instruction.ShouldNotBeNull();
        baselineTarget.ShouldBeSameAs(baselineStore,
            "baseline branch must directly target the original local store");
        HasCompleteAffineCodec(baselineModule, "BranchTargetStore").ShouldBeFalse();

        var protectedMethod = FindMethod(protectedModule, "BranchTargetStore");
        var protectedBody = protectedMethod.CilMethodBody.ShouldNotBeNull();
        protectedBody.Instructions.ExpandMacros();
        protectedBody.LocalVariables.Count.ShouldBe(1);
        var protectedVariable = protectedBody.LocalVariables[0];
        var protectedBranch = protectedBody.Instructions.Single(
            instruction => instruction.OpCode.Code == CilCode.Br);
        protectedBranch.Operand.ShouldBeOfType<CilInstructionLabel>();
        var protectedTarget = ((CilInstructionLabel)protectedBranch.Operand!).Instruction.ShouldNotBeNull();
        int protectedBranchIndex = protectedBody.Instructions.IndexOf(protectedBranch);
        int protectedTargetIndex = protectedBody.Instructions.IndexOf(protectedTarget);
        protectedTargetIndex.ShouldBeGreaterThan(protectedBranchIndex,
            "protected branch target must remain the forward store path");
        protectedTarget.OpCode.Code.ShouldBe(CilCode.Ldc_I4,
            "protected branch must target the first encoder instruction");

        int protectedStoreIndex = protectedTargetIndex + 6;
        protectedStoreIndex.ShouldBeLessThan(protectedBody.Instructions.Count);
        var protectedStore = protectedBody.Instructions[protectedStoreIndex];
        IsStoreVariable(protectedStore).ShouldBeTrue();
        protectedStore.GetLocalVariable(protectedBody.LocalVariables).ShouldBeSameAs(protectedVariable);
        var shape = GetLocalShape(protectedModule, protectedVariable);
        TryReadEncoding(protectedBody.Instructions, protectedStoreIndex, shape, out var encoding).ShouldBeTrue(
            "branch target must begin a complete affine encoder");
        IsValidEncoding(encoding).ShouldBeTrue();
        protectedBody.Instructions[protectedTargetIndex].ShouldBeSameAs(protectedTarget);
        HasCompleteAffineCodec(protectedModule, "BranchTargetStore").ShouldBeTrue();
    }

    internal static void AssertExceptionBoundaryLocalCodec(
        ModuleDefinition baselineModule,
        ModuleDefinition protectedModule)
    {
        var baselineMethod = FindMethod(baselineModule, "ExceptionBoundaryLocal");
        var baselineBody = baselineMethod.CilMethodBody.ShouldNotBeNull();
        baselineBody.Instructions.ExpandMacros();
        var baselineHandler = baselineBody.ExceptionHandlers.Single();
        AssertExceptionBoundaryShape(baselineBody, baselineHandler, "baseline");
        HasCompleteAffineCodec(baselineModule, "ExceptionBoundaryLocal").ShouldBeFalse();

        var protectedMethod = FindMethod(protectedModule, "ExceptionBoundaryLocal");
        var protectedBody = protectedMethod.CilMethodBody.ShouldNotBeNull();
        protectedBody.Instructions.ExpandMacros();
        protectedBody.LocalVariables.Count.ShouldBe(1);
        var protectedVariable = protectedBody.LocalVariables[0];
        var protectedHandler = protectedBody.ExceptionHandlers.Single();
        AssertExceptionBoundaryShape(protectedBody, protectedHandler, "protected");

        int tryStartIndex = protectedBody.Instructions.IndexOf(GetLabelInstruction(protectedHandler.TryStart!));
        int handlerStartIndex = protectedBody.Instructions.IndexOf(GetLabelInstruction(protectedHandler.HandlerStart!));
        int handlerEndIndex = protectedBody.Instructions.IndexOf(GetLabelInstruction(protectedHandler.HandlerEnd!));
        var shape = GetLocalShape(protectedModule, protectedVariable);
        int firstStoreIndex = GetVariableAccessIndexes(protectedBody, protectedVariable, IsStoreVariable)[0];
        TryReadEncoding(protectedBody.Instructions, firstStoreIndex, shape, out var encoding).ShouldBeTrue();
        IsValidEncoding(encoding).ShouldBeTrue();

        AssertMatchingDecoder(protectedBody, tryStartIndex, shape, encoding, "try start");
        AssertMatchingDecoder(protectedBody, handlerStartIndex, shape, encoding, "handler start");
        AssertMatchingDecoder(protectedBody, handlerEndIndex, shape, encoding, "handler end");
        (tryStartIndex + 6).ShouldBeLessThan(handlerStartIndex,
            "try-start decoder must remain inside the protected region");
        (handlerStartIndex + 6).ShouldBeLessThan(handlerEndIndex,
            "handler-start decoder must remain inside the finally region");
        (handlerEndIndex + 6).ShouldBeLessThan(protectedBody.Instructions.Count,
            "handler-end decoder must remain outside the exclusive finally boundary");

        int[] handlerStoreIndexes = GetVariableAccessIndexes(protectedBody, protectedVariable, IsStoreVariable)
            .Where(storeIndex => storeIndex > handlerStartIndex && storeIndex < handlerEndIndex)
            .ToArray();
        handlerStoreIndexes.Length.ShouldBe(1,
            "the finally region must retain exactly one encoded local store");
        TryReadEncoding(protectedBody.Instructions, handlerStoreIndexes[0], shape, out var handlerEncoding)
            .ShouldBeTrue();
        handlerEncoding.ShouldBe(encoding);
        HasCompleteAffineCodec(protectedModule, "ExceptionBoundaryLocal").ShouldBeTrue();
    }

    internal static void AssertInjectedUnsupportedLocalBypasses(
        ModuleDefinition baselineModule,
        ModuleDefinition protectedModule)
    {
        foreach (string methodName in InjectedUnsupportedMethodNames)
        {
            var baselineMethod = FindMethod(baselineModule, methodName);
            var protectedMethod = FindMethod(protectedModule, methodName);
            var baselineBody = baselineMethod.CilMethodBody.ShouldNotBeNull();
            var protectedBody = protectedMethod.CilMethodBody.ShouldNotBeNull();
            baselineBody.Instructions.ExpandMacros();
            protectedBody.Instructions.ExpandMacros();

            AssertExpectedUnsupportedLocalSignature(baselineMethod, methodName);
            AssertExpectedUnsupportedLocalAccess(baselineBody, methodName);
            AssertExpectedUnsupportedLocalSignature(protectedMethod, methodName);
            AssertEquivalentInstructionBodies(baselineBody, protectedBody, methodName);
            AssertVariableHasNoCodec(protectedBody, protectedBody.LocalVariables[0], methodName);
        }
    }

    internal static void AssertAddressedLocalHasNoCodec(ModuleDefinition module, string methodName)
    {
        var method = FindMethod(module, methodName);
        var body = method.CilMethodBody.ShouldNotBeNull();
        body.Instructions.ExpandMacros();

        var addressedVariables = GetAddressedVariables(body);
        addressedVariables.Count.ShouldBeGreaterThan(0, $"{methodName} must contain an address-taken local");
        foreach (var variable in addressedVariables)
            AssertVariableHasNoCodec(body, variable, methodName);
    }

    internal static void AssertUnsupportedLocalsHaveNoCodec(ModuleDefinition module, string methodName)
    {
        var method = FindMethod(module, methodName);
        var body = method.CilMethodBody.ShouldNotBeNull();
        body.Instructions.ExpandMacros();

        var unsupportedVariables = body.LocalVariables
            .Where(variable => GetLocalShape(module, variable).BitWidth == 0)
            .Where(variable => HasVariableAccess(body, variable))
            .ToArray();
        unsupportedVariables.Length.ShouldBeGreaterThan(0,
            $"{methodName} must contain at least one accessed unsupported local");
        foreach (var variable in unsupportedVariables)
            AssertVariableHasNoCodec(body, variable, methodName);
    }

    private static void AssertDeterministicEncoding(LocalVariableEncodingAffineEncoding encoding)
    {
        encoding.XorKey.ShouldBe(1UL);
        encoding.Multiplier.ShouldBe(3UL);
        encoding.Addend.ShouldBe(GetValueMask(encoding.BitWidth) - 1);
        IsValidEncoding(encoding).ShouldBeTrue();
    }

    private static ulong GetExpectedInverseMultiplier(int bitWidth)
    {
        return bitWidth switch
        {
            8 => 0xab,
            16 => 0xaaab,
            32 => 0xaaaaaaab,
            64 => 0xaaaaaaaaaaaaaaab,
            _ => throw new ArgumentOutOfRangeException(nameof(bitWidth), bitWidth, null)
        };
    }

    private static void AssertExceptionBoundaryShape(
        CilMethodBody body,
        CilExceptionHandler handler,
        string artifactName)
    {
        handler.HandlerType.ShouldBe(CilExceptionHandlerType.Finally);
        var tryStart = GetLabelInstruction(handler.TryStart.ShouldNotBeNull());
        var tryEnd = GetLabelInstruction(handler.TryEnd.ShouldNotBeNull());
        var handlerStart = GetLabelInstruction(handler.HandlerStart.ShouldNotBeNull());
        var handlerEnd = GetLabelInstruction(handler.HandlerEnd.ShouldNotBeNull());
        tryStart.OpCode.Code.ShouldBe(CilCode.Ldloc, $"{artifactName} try start boundary");
        handlerStart.OpCode.Code.ShouldBe(CilCode.Ldloc, $"{artifactName} handler start boundary");
        handlerEnd.OpCode.Code.ShouldBe(CilCode.Ldloc, $"{artifactName} handler end boundary");
        tryEnd.ShouldBeSameAs(handlerStart,
            $"{artifactName} try end and handler start must share the local access boundary");
        var leave = body.Instructions.Single(instruction => instruction.OpCode.Code == CilCode.Leave);
        leave.Operand.ShouldBeOfType<CilInstructionLabel>();
        GetLabelInstruction((CilInstructionLabel)leave.Operand!).ShouldBeSameAs(handlerEnd,
            $"{artifactName} leave target must remain the handler-end local access");
    }

    private static CilInstruction GetLabelInstruction(ICilLabel label)
    {
        label.ShouldBeOfType<CilInstructionLabel>();
        return ((CilInstructionLabel)label).Instruction.ShouldNotBeNull();
    }

    private static void AssertMatchingDecoder(
        CilMethodBody body,
        int loadIndex,
        (ElementType ElementType, int BitWidth) shape,
        LocalVariableEncodingAffineEncoding encoding,
        string boundaryName)
    {
        TryReadDecoding(body.Instructions, loadIndex, shape, out ulong addend,
            out ulong inverseMultiplier, out ulong xorKey).ShouldBeTrue(
            $"{boundaryName} local load must contain the complete affine decoder");
        addend.ShouldBe(encoding.Addend);
        xorKey.ShouldBe(encoding.XorKey);
        (unchecked(encoding.Multiplier * inverseMultiplier) & GetValueMask(shape.BitWidth)).ShouldBe(1UL);
    }

    private static void AssertExpectedUnsupportedLocalSignature(MethodDefinition method, string methodName)
    {
        var body = method.CilMethodBody.ShouldNotBeNull();
        body.LocalVariables.Count.ShouldBe(1);
        TypeSignature variableType = body.LocalVariables[0].VariableType;

        switch (methodName)
        {
            case "UnsupportedIntPtrLocal":
                variableType.ElementType.ShouldBe(ElementType.I);
                break;
            case "UnsupportedUIntPtrLocal":
                variableType.ElementType.ShouldBe(ElementType.U);
                break;
            case "UnsupportedPointerLocal":
                variableType.ShouldBeOfType<PointerTypeSignature>();
                ((PointerTypeSignature)variableType).BaseType.ElementType.ShouldBe(ElementType.I4);
                break;
            case "UnsupportedByReferenceLocal":
                variableType.ShouldBeOfType<ByReferenceTypeSignature>();
                ((ByReferenceTypeSignature)variableType).BaseType.ElementType.ShouldBe(ElementType.I4);
                break;
            case "UnsupportedPinnedByReferenceLocal":
                variableType.ShouldBeOfType<PinnedTypeSignature>();
                var pinnedType = (PinnedTypeSignature)variableType;
                pinnedType.BaseType.ShouldBeOfType<ByReferenceTypeSignature>();
                ((ByReferenceTypeSignature)pinnedType.BaseType).BaseType.ElementType.ShouldBe(ElementType.I4);
                break;
            case "UnsupportedMethodGenericParameterLocal":
                variableType.ShouldBeOfType<GenericParameterSignature>();
                var genericParameter = (GenericParameterSignature)variableType;
                genericParameter.ParameterType.ShouldBe(GenericParameterType.Method);
                genericParameter.Index.ShouldBe(0);
                method.Signature!.GenericParameterCount.ShouldBe(1);
                method.GenericParameters.Count.ShouldBe(1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null);
        }
    }

    private static void AssertExpectedUnsupportedLocalAccess(CilMethodBody body, string methodName)
    {
        body.InitializeLocals.ShouldBeTrue();
        body.Instructions.Count.ShouldBe(3, $"{methodName} baseline instruction count");
        body.Instructions[0].OpCode.Code.ShouldBe(CilCode.Ldloc);
        body.Instructions[0].GetLocalVariable(body.LocalVariables).ShouldBeSameAs(body.LocalVariables[0]);
        body.Instructions[1].OpCode.Code.ShouldBe(CilCode.Pop);
        body.Instructions[2].OpCode.Code.ShouldBe(CilCode.Ret);
    }

    private static void AssertEquivalentInstructionBodies(
        CilMethodBody baselineBody,
        CilMethodBody protectedBody,
        string methodName)
    {
        protectedBody.InitializeLocals.ShouldBe(baselineBody.InitializeLocals);
        protectedBody.LocalVariables.Count.ShouldBe(baselineBody.LocalVariables.Count);
        protectedBody.Instructions.Count.ShouldBe(baselineBody.Instructions.Count,
            $"{methodName} instruction count must be unchanged");

        for (int instructionIndex = 0; instructionIndex < baselineBody.Instructions.Count; instructionIndex++)
        {
            var baselineInstruction = baselineBody.Instructions[instructionIndex];
            var protectedInstruction = protectedBody.Instructions[instructionIndex];
            protectedInstruction.OpCode.Code.ShouldBe(baselineInstruction.OpCode.Code,
                $"{methodName} opcode at index {instructionIndex}");
            AssertEquivalentOperand(
                baselineInstruction.Operand,
                protectedInstruction.Operand,
                baselineBody,
                protectedBody,
                instructionIndex);
        }
    }

    private static void AssertEquivalentOperand(
        object? baselineOperand,
        object? protectedOperand,
        CilMethodBody baselineBody,
        CilMethodBody protectedBody,
        int instructionIndex)
    {
        if (baselineOperand is CilLocalVariable baselineVariable)
        {
            protectedOperand.ShouldBeOfType<CilLocalVariable>();
            int baselineVariableIndex = baselineBody.LocalVariables.IndexOf(baselineVariable);
            int protectedVariableIndex = protectedBody.LocalVariables.IndexOf((CilLocalVariable)protectedOperand!);
            protectedVariableIndex.ShouldBe(baselineVariableIndex,
                $"InitializeLocals=false local operand at index {instructionIndex}");
            return;
        }

        protectedOperand.ShouldBe(baselineOperand,
            $"InitializeLocals=false operand at index {instructionIndex}");
    }

    private static void AssertVariableHasNoCodec(
        CilMethodBody body,
        CilLocalVariable variable,
        string methodName)
    {
        foreach (int storeIndex in GetVariableAccessIndexes(body, variable, IsStoreVariable))
            HasGenericEncodingShape(body.Instructions, storeIndex).ShouldBeFalse(
                $"{methodName} contains an affine encoder around an excluded store");

        foreach (int loadIndex in GetVariableAccessIndexes(body, variable, IsLoadVariable))
            HasGenericDecodingShape(body.Instructions, loadIndex).ShouldBeFalse(
                $"{methodName} contains an affine decoder around an excluded load");
    }
}
