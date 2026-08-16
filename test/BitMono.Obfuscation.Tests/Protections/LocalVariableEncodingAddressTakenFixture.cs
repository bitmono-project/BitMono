using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace BitMono.Obfuscation.Tests.Protections;

internal static class LocalVariableEncodingAddressTakenFixture
{
    internal const string MethodName = "AddressTakenAndEligibleLocal";

    internal static void Inject(ModuleDefinition module, TypeDefinition programType)
    {
        var method = new MethodDefinition(
            MethodName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodSignature.CreateStatic(module.CorLibTypeFactory.Int32));
        var body = method.CilMethodBody = new CilMethodBody
        {
            InitializeLocals = true
        };
        var addressedVariable = new CilLocalVariable(module.CorLibTypeFactory.Int32);
        var eligibleVariable = new CilLocalVariable(module.CorLibTypeFactory.Int32);
        body.LocalVariables.Add(addressedVariable);
        body.LocalVariables.Add(eligibleVariable);

        body.Instructions.Add(CilInstruction.CreateLdcI4(40));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Stloc, addressedVariable));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ldloca, addressedVariable));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Dup));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ldind_I4));
        body.Instructions.Add(CilInstruction.CreateLdcI4(2));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Add));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Stind_I4));
        body.Instructions.Add(CilInstruction.CreateLdcI4(5));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Stloc, eligibleVariable));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ldloc, eligibleVariable));
        body.Instructions.Add(CilInstruction.CreateLdcI4(3));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Mul));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Stloc, eligibleVariable));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ldloc, addressedVariable));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ldloc, eligibleVariable));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Add));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
        body.ComputeMaxStack();
        programType.Methods.Add(method);
    }
}
