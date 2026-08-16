using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;
using static BitMono.Obfuscation.Tests.Protections.LocalVariableEncodingCilInspection;

namespace BitMono.Obfuscation.Tests.Protections;

internal static class LocalVariableEncodingAddressTakenAssertions
{
    internal static void AssertTransformation(ModuleDefinition baselineModule, ModuleDefinition protectedModule)
    {
        var baselineBody = FindMethod(baselineModule, LocalVariableEncodingAddressTakenFixture.MethodName)
            .CilMethodBody.ShouldNotBeNull();
        var protectedBody = FindMethod(protectedModule, LocalVariableEncodingAddressTakenFixture.MethodName)
            .CilMethodBody.ShouldNotBeNull();
        baselineBody.Instructions.ExpandMacros();
        protectedBody.Instructions.ExpandMacros();
        baselineBody.LocalVariables.Count.ShouldBe(2);
        protectedBody.LocalVariables.Count.ShouldBe(2);

        var baselineAddressedVariable = baselineBody.LocalVariables[0];
        var protectedAddressedVariable = protectedBody.LocalVariables[0];
        AssertAddressedAccesses(baselineBody, baselineAddressedVariable);
        AssertAddressedAccesses(protectedBody, protectedAddressedVariable);
        AssertAddressMutationSequence(baselineBody, baselineAddressedVariable);
        AssertAddressMutationSequence(protectedBody, protectedAddressedVariable);
        GetAddressedVariables(baselineBody).Single().ShouldBeSameAs(baselineAddressedVariable);
        GetAddressedVariables(protectedBody).Single().ShouldBeSameAs(protectedAddressedVariable);

        HasCompleteAffineCodec(baselineModule, LocalVariableEncodingAddressTakenFixture.MethodName).ShouldBeFalse();
        HasCompleteAffineCodec(protectedModule, LocalVariableEncodingAddressTakenFixture.MethodName).ShouldBeTrue();
        GetVariableAccessIndexes(baselineBody, baselineBody.LocalVariables[1], IsStoreVariable).Length.ShouldBe(2);
        GetVariableAccessIndexes(protectedBody, protectedBody.LocalVariables[1], IsStoreVariable).Length.ShouldBe(3);
    }

    private static void AssertAddressedAccesses(CilMethodBody body, CilLocalVariable variable)
    {
        var accessCodes = body.Instructions
            .Where(instruction => ReferenceEquals(instruction.Operand, variable))
            .Select(instruction => instruction.OpCode.Code)
            .ToArray();
        accessCodes.ShouldBe(new[] { CilCode.Stloc, CilCode.Ldloca, CilCode.Ldloc });

        int storeIndex = GetVariableAccessIndexes(body, variable, IsStoreVariable).Single();
        int loadIndex = GetVariableAccessIndexes(body, variable, IsLoadVariable).Single();
        HasGenericEncodingShape(body.Instructions, storeIndex).ShouldBeFalse();
        HasGenericDecodingShape(body.Instructions, loadIndex).ShouldBeFalse();
    }

    private static void AssertAddressMutationSequence(CilMethodBody body, CilLocalVariable variable)
    {
        int addressIndex = body.Instructions.IndexOf(body.Instructions.Single(instruction =>
            instruction.OpCode.Code == CilCode.Ldloca && ReferenceEquals(instruction.Operand, variable)));
        var mutationCodes = body.Instructions
            .Skip(addressIndex - 2)
            .Take(8)
            .Select(instruction => instruction.OpCode.Code)
            .ToArray();
        mutationCodes.ShouldBe(new[]
        {
            CilCode.Ldc_I4,
            CilCode.Stloc,
            CilCode.Ldloca,
            CilCode.Dup,
            CilCode.Ldind_I4,
            CilCode.Ldc_I4,
            CilCode.Add,
            CilCode.Stind_I4
        });
        body.Instructions[addressIndex - 2].Operand.ShouldBe(40);
        body.Instructions[addressIndex + 3].Operand.ShouldBe(2);
    }
}
