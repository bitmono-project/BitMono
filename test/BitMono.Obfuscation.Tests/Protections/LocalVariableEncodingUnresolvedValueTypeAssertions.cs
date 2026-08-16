using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using static BitMono.Obfuscation.Tests.Protections.LocalVariableEncodingCilInspection;

namespace BitMono.Obfuscation.Tests.Protections;

internal static class LocalVariableEncodingUnresolvedValueTypeAssertions
{
    private const string MethodName = "UnresolvedValueTypeLocal";
    private const string MissingAssemblyName = "BitMono.LocalVariableEncoding.Missing";

    internal static void AssertBypass(
        string baselineAssemblyPath,
        string protectedAssemblyPath,
        AsmResolver.DotNet.ModuleDefinition baselineModule,
        AsmResolver.DotNet.ModuleDefinition protectedModule)
    {
        AssertUnresolvedValueTypeSignature(baselineModule);
        AssertUnresolvedValueTypeSignature(protectedModule);

        byte[] baselineInstructions = ReadSerializedInstructions(baselineAssemblyPath, MethodName);
        byte[] protectedInstructions = ReadSerializedInstructions(protectedAssemblyPath, MethodName);
        protectedInstructions.SequenceEqual(baselineInstructions).ShouldBeTrue(
            $"{MethodName} serialized method instructions must remain byte-for-byte unchanged");

        var protectedBody = FindMethod(protectedModule, MethodName).CilMethodBody.ShouldNotBeNull();
        protectedBody.Instructions.ExpandMacros();
        protectedBody.Instructions.Count.ShouldBe(3);
        protectedBody.Instructions[0].OpCode.Code.ShouldBe(CilCode.Ldloc);
        protectedBody.Instructions[1].OpCode.Code.ShouldBe(CilCode.Pop);
        protectedBody.Instructions[2].OpCode.Code.ShouldBe(CilCode.Ret);
        HasGenericDecodingShape(protectedBody.Instructions, 0).ShouldBeFalse(
            $"{MethodName} must not contain a decoder for an unresolved value type");
    }

    private static void AssertUnresolvedValueTypeSignature(AsmResolver.DotNet.ModuleDefinition module)
    {
        var method = FindMethod(module, MethodName);
        var body = method.CilMethodBody.ShouldNotBeNull();
        body.LocalVariables.Count.ShouldBe(1);
        var typeSignature = body.LocalVariables[0].VariableType.ShouldBeOfType<TypeDefOrRefSignature>();
        typeSignature.ElementType.ShouldBe(ElementType.ValueType);
        typeSignature.Type.ShouldBeAssignableTo<AsmResolver.DotNet.TypeReference>();
        var typeReference = (AsmResolver.DotNet.TypeReference)typeSignature.Type;
        typeReference.Namespace.ShouldBe(MissingAssemblyName);
        typeReference.Name.ShouldBe("MissingValueType");
        typeReference.Scope.ShouldBeAssignableTo<AsmResolver.DotNet.AssemblyReference>();
        var assemblyReference = (AsmResolver.DotNet.AssemblyReference)typeReference.Scope!;
        assemblyReference.Name.ShouldBe(MissingAssemblyName);
        module.RuntimeContext.ShouldNotBeNull();
        typeReference.TryResolve(module.RuntimeContext!, out _).ShouldBeFalse(
            $"{MethodName} fixture type must remain unresolved");
    }
}
