using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using static BitMono.Obfuscation.Tests.Protections.LocalVariableEncodingCilInspection;

namespace BitMono.Obfuscation.Tests.Protections;

internal static class LocalVariableEncodingBooleanAssertions
{
    private const string BooleanMethodName = "BooleanLocal";
    private const string BooleanBackedEnumMethodName = "BooleanBackedEnumLocal";

    internal static void AssertBypass(
        string baselineAssemblyPath,
        string protectedAssemblyPath,
        ModuleDefinition baselineModule,
        ModuleDefinition protectedModule)
    {
        AssertBooleanSignature(baselineModule);
        AssertBooleanSignature(protectedModule);
        AssertBooleanBackedEnumSignature(baselineModule);
        AssertBooleanBackedEnumSignature(protectedModule);
        AssertSerializedBypass(
            baselineAssemblyPath,
            protectedAssemblyPath,
            baselineModule,
            protectedModule,
            BooleanMethodName);
        AssertSerializedBypass(
            baselineAssemblyPath,
            protectedAssemblyPath,
            baselineModule,
            protectedModule,
            BooleanBackedEnumMethodName);
    }

    private static void AssertBooleanSignature(ModuleDefinition module)
    {
        var body = FindMethod(module, BooleanMethodName).CilMethodBody.ShouldNotBeNull();
        body.LocalVariables.Count.ShouldBe(1);
        body.LocalVariables[0].VariableType.ElementType.ShouldBe(ElementType.Boolean);
    }

    private static void AssertBooleanBackedEnumSignature(ModuleDefinition module)
    {
        var body = FindMethod(module, BooleanBackedEnumMethodName).CilMethodBody.ShouldNotBeNull();
        body.LocalVariables.Count.ShouldBe(1);
        var typeSignature = body.LocalVariables[0].VariableType.ShouldBeOfType<TypeDefOrRefSignature>();
        typeSignature.ElementType.ShouldBe(ElementType.ValueType);
        module.RuntimeContext.ShouldNotBeNull();
        typeSignature.Type.TryResolve(module.RuntimeContext!, out var typeDefinition).ShouldBeTrue();
        typeDefinition.IsEnum.ShouldBeTrue();
        typeDefinition.GetEnumUnderlyingType().ShouldNotBeNull().ElementType.ShouldBe(ElementType.Boolean);
    }

    private static void AssertSerializedBypass(
        string baselineAssemblyPath,
        string protectedAssemblyPath,
        ModuleDefinition baselineModule,
        ModuleDefinition protectedModule,
        string methodName)
    {
        byte[] baselineInstructions = ReadSerializedInstructions(baselineAssemblyPath, methodName);
        byte[] protectedInstructions = ReadSerializedInstructions(protectedAssemblyPath, methodName);
        protectedInstructions.SequenceEqual(baselineInstructions).ShouldBeTrue(
            $"{methodName} serialized method instructions must remain byte-for-byte unchanged");

        AssertLocalAccessShape(baselineModule, methodName, "baseline");
        AssertLocalAccessShape(protectedModule, methodName, "protected");
    }

    private static void AssertLocalAccessShape(ModuleDefinition module, string methodName, string artifactName)
    {
        var body = FindMethod(module, methodName).CilMethodBody.ShouldNotBeNull();
        body.Instructions.ExpandMacros();
        body.Instructions.Count.ShouldBe(5);
        body.Instructions[0].OpCode.Code.ShouldBe(CilCode.Ldc_I4);
        body.Instructions[0].Operand.ShouldBe(0);
        body.Instructions[1].OpCode.Code.ShouldBe(CilCode.Stloc);
        body.Instructions[2].OpCode.Code.ShouldBe(CilCode.Ldloc);
        body.Instructions[3].OpCode.Code.ShouldBe(CilCode.Pop);
        body.Instructions[4].OpCode.Code.ShouldBe(CilCode.Ret);

        var variable = body.LocalVariables.Single();
        ReferenceEquals(body.Instructions[1].GetLocalVariable(body.LocalVariables), variable).ShouldBeTrue();
        ReferenceEquals(body.Instructions[2].GetLocalVariable(body.LocalVariables), variable).ShouldBeTrue();
        HasGenericEncodingShape(body.Instructions, 1).ShouldBeFalse(
            $"{artifactName} {methodName} must not contain an encoder");
        HasGenericDecodingShape(body.Instructions, 2).ShouldBeFalse(
            $"{artifactName} {methodName} must not contain a decoder");
    }
}
