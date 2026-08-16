using AsmResolver.PE.DotNet.Metadata.Tables;

namespace BitMono.Protections;

[DoNotResolve(MemberInclusionFlags.SpecialRuntime | MemberInclusionFlags.MethodBody)]
[RuntimeMonikerNETCore]
[IL2CPPIncompatible(
    "Rewritten local-slot IL has not been validated through the IL2CPP conversion pipeline")]
public sealed class LocalVariableEncoding : Protection
{
    private const int RandomWordLimit = 1 << 16;

    private readonly RandomNext _randomNext;

    public LocalVariableEncoding(RandomNext randomNext, IBitMonoServiceProvider serviceProvider) : base(serviceProvider)
    {
        _randomNext = randomNext;
    }

    public override Task ExecuteAsync()
    {
        foreach (var method in Context.Parameters.Members.OfType<MethodDefinition>())
        {
            Context.ThrowIfCancellationTokenRequested();

            if (method.CilMethodBody is not { InitializeLocals: true } body || body.Instructions.Count == 0)
                continue;

            Protect(body);
        }

        return Task.CompletedTask;
    }

    private void Protect(CilMethodBody body)
    {
        var originalInstructions = body.Instructions.ToArray();
        var addressedVariables = GetAddressedVariables(originalInstructions);
        var encodings = new Dictionary<CilLocalVariable, LocalEncoding>();

        foreach (var instruction in originalInstructions)
        {
            if (!IsLoadVariable(instruction) && !IsStoreVariable(instruction))
                continue;

            var variable = instruction.GetLocalVariable(body.LocalVariables);
            if (addressedVariables.Contains(variable) || encodings.ContainsKey(variable))
                continue;

            var encoding = CreateEncoding(variable);
            if (encoding != null)
                encodings.Add(variable, encoding);
        }

        if (encodings.Count == 0)
            return;

        for (int instructionIndex = originalInstructions.Length - 1; instructionIndex >= 0; instructionIndex--)
        {
            var instruction = originalInstructions[instructionIndex];
            if (!IsLoadVariable(instruction) && !IsStoreVariable(instruction))
                continue;

            var variable = instruction.GetLocalVariable(body.LocalVariables);
            if (!encodings.TryGetValue(variable, out var encoding))
                continue;

            int currentIndex = body.Instructions.IndexOf(instruction);
            if (IsLoadVariable(instruction))
                body.Instructions.InsertRange(currentIndex + 1, CreateDecodingInstructions(encoding));
            else
                ReplaceStore(body.Instructions, currentIndex, instruction, variable, encoding);
        }

        var initializationInstructions = new List<CilInstruction>();
        foreach (var pair in encodings)
        {
            initializationInstructions.Add(CreateConstantInstruction(0, pair.Value.BitWidth));
            initializationInstructions.AddRange(CreateEncodingInstructions(pair.Value));
            initializationInstructions.Add(new CilInstruction(CilOpCodes.Stloc, pair.Key));
        }

        body.Instructions.InsertRange(0, initializationInstructions);
        body.Instructions.CalculateOffsets();
        body.VerifyLabels();
        body.ComputeMaxStack();
    }

    private LocalEncoding? CreateEncoding(CilLocalVariable variable)
    {
        var runtimeContext = Context.Module.RuntimeContext;
        if (runtimeContext == null)
            return null;

        var elementType = GetElementType(variable.VariableType, runtimeContext);
        int bitWidth = elementType switch
        {
            ElementType.I1 or ElementType.U1 => 8,
            ElementType.Char or ElementType.I2 or ElementType.U2 => 16,
            ElementType.I4 or ElementType.U4 => 32,
            ElementType.I8 or ElementType.U8 => 64,
            _ => 0
        };
        if (bitWidth == 0)
            return null;

        ulong valueMask = bitWidth == 64
            ? ulong.MaxValue
            : (1UL << bitWidth) - 1;
        ulong xorKey = NextNonZero(valueMask);
        ulong multiplier = NextMultiplier(valueMask);
        ulong addend = NextNonZero(valueMask);
        if (unchecked((xorKey * multiplier + addend) & valueMask) == 0)
        {
            addend = (addend + 1) & valueMask;
            if (addend == 0)
                addend = 1;
        }

        ulong inverseMultiplier = GetMultiplicativeInverse(multiplier) & valueMask;
        return new LocalEncoding(elementType, bitWidth, xorKey, multiplier, addend, inverseMultiplier);
    }

    private static ElementType GetElementType(TypeSignature variableType, RuntimeContext runtimeContext)
    {
        if (variableType.ElementType != ElementType.ValueType)
            return variableType.ElementType;

        if (variableType is not TypeDefOrRefSignature typeSignature ||
            !typeSignature.Type.TryResolve(runtimeContext, out var typeDefinition) ||
            !typeDefinition.IsEnum)
            return ElementType.ValueType;

        return typeDefinition.GetEnumUnderlyingType()?.ElementType ?? ElementType.ValueType;
    }

    private HashSet<CilLocalVariable> GetAddressedVariables(IEnumerable<CilInstruction> instructions)
    {
        var variables = new HashSet<CilLocalVariable>();
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode.Code is not (CilCode.Ldloca or CilCode.Ldloca_S))
                continue;

            if (instruction.Operand is CilLocalVariable variable)
                variables.Add(variable);
        }

        return variables;
    }

    private IEnumerable<CilInstruction> CreateEncodingInstructions(LocalEncoding encoding)
    {
        var instructions = new List<CilInstruction>
        {
            CreateConstantInstruction(encoding.XorKey, encoding.BitWidth),
            new CilInstruction(CilOpCodes.Xor)
        };

        instructions.Add(CreateConstantInstruction(encoding.Multiplier, encoding.BitWidth));
        instructions.Add(new CilInstruction(CilOpCodes.Mul));
        instructions.Add(CreateConstantInstruction(encoding.Addend, encoding.BitWidth));
        instructions.Add(new CilInstruction(CilOpCodes.Add));

        var conversionInstruction = CreateStorageConversionInstruction(encoding.ElementType);
        if (conversionInstruction != null)
            instructions.Add(conversionInstruction);

        return instructions;
    }

    private IEnumerable<CilInstruction> CreateDecodingInstructions(LocalEncoding encoding)
    {
        var instructions = new List<CilInstruction>
        {
            CreateConstantInstruction(encoding.Addend, encoding.BitWidth),
            new CilInstruction(CilOpCodes.Sub)
        };

        instructions.Add(CreateConstantInstruction(encoding.InverseMultiplier, encoding.BitWidth));
        instructions.Add(new CilInstruction(CilOpCodes.Mul));
        instructions.Add(CreateConstantInstruction(encoding.XorKey, encoding.BitWidth));
        instructions.Add(new CilInstruction(CilOpCodes.Xor));

        var conversionInstruction = CreateStorageConversionInstruction(encoding.ElementType);
        if (conversionInstruction != null)
            instructions.Add(conversionInstruction);

        return instructions;
    }

    private static void ReplaceStore(CilInstructionCollection instructions, int instructionIndex,
        CilInstruction instruction, CilLocalVariable variable, LocalEncoding encoding)
    {
        var xorKeyInstruction = CreateConstantInstruction(encoding.XorKey, encoding.BitWidth);
        instruction.ReplaceWith(xorKeyInstruction.OpCode, xorKeyInstruction.Operand);

        var encodingInstructions = new List<CilInstruction>
        {
            new CilInstruction(CilOpCodes.Xor)
        };

        encodingInstructions.Add(CreateConstantInstruction(encoding.Multiplier, encoding.BitWidth));
        encodingInstructions.Add(new CilInstruction(CilOpCodes.Mul));
        encodingInstructions.Add(CreateConstantInstruction(encoding.Addend, encoding.BitWidth));
        encodingInstructions.Add(new CilInstruction(CilOpCodes.Add));

        var conversionInstruction = CreateStorageConversionInstruction(encoding.ElementType);
        if (conversionInstruction != null)
            encodingInstructions.Add(conversionInstruction);

        encodingInstructions.Add(new CilInstruction(CilOpCodes.Stloc, variable));
        instructions.InsertRange(instructionIndex + 1, encodingInstructions);
    }

    private static CilInstruction? CreateStorageConversionInstruction(ElementType elementType)
    {
        return elementType switch
        {
            ElementType.I1 => new CilInstruction(CilOpCodes.Conv_I1),
            ElementType.U1 => new CilInstruction(CilOpCodes.Conv_U1),
            ElementType.I2 => new CilInstruction(CilOpCodes.Conv_I2),
            ElementType.Char or ElementType.U2 => new CilInstruction(CilOpCodes.Conv_U2),
            _ => null
        };
    }

    private static CilInstruction CreateConstantInstruction(ulong value, int bitWidth)
    {
        return bitWidth == 64
            ? new CilInstruction(CilOpCodes.Ldc_I8, unchecked((long)value))
            : new CilInstruction(CilOpCodes.Ldc_I4, unchecked((int)value));
    }

    private ulong NextNonZero(ulong valueMask)
    {
        ulong value = NextValue() & valueMask;
        return value == 0 ? 1 : value;
    }

    private ulong NextMultiplier(ulong valueMask)
    {
        ulong value = (NextValue() & valueMask) | 1;
        return value == 1 ? 3 : value;
    }

    private ulong NextValue()
    {
        ulong value = (uint)_randomNext(0, RandomWordLimit);
        value |= (ulong)(uint)_randomNext(0, RandomWordLimit) << 16;
        value |= (ulong)(uint)_randomNext(0, RandomWordLimit) << 32;
        value |= (ulong)(uint)_randomNext(0, RandomWordLimit) << 48;
        return value;
    }

    private static ulong GetMultiplicativeInverse(ulong multiplier)
    {
        ulong inverse = multiplier;
        for (int iteration = 0; iteration < 6; iteration++)
            inverse = unchecked(inverse * (2 - multiplier * inverse));

        return inverse;
    }

    private static bool IsLoadVariable(CilInstruction instruction)
    {
        return instruction.OpCode.Code is CilCode.Ldloc or CilCode.Ldloc_S
            or CilCode.Ldloc_0 or CilCode.Ldloc_1 or CilCode.Ldloc_2 or CilCode.Ldloc_3;
    }

    private static bool IsStoreVariable(CilInstruction instruction)
    {
        return instruction.OpCode.Code is CilCode.Stloc or CilCode.Stloc_S
            or CilCode.Stloc_0 or CilCode.Stloc_1 or CilCode.Stloc_2 or CilCode.Stloc_3;
    }

    private sealed class LocalEncoding
    {
        public LocalEncoding(ElementType elementType, int bitWidth, ulong xorKey, ulong multiplier, ulong addend,
            ulong inverseMultiplier)
        {
            ElementType = elementType;
            BitWidth = bitWidth;
            XorKey = xorKey;
            Multiplier = multiplier;
            Addend = addend;
            InverseMultiplier = inverseMultiplier;
        }

        public ElementType ElementType { get; }
        public int BitWidth { get; }
        public ulong XorKey { get; }
        public ulong Multiplier { get; }
        public ulong Addend { get; }
        public ulong InverseMultiplier { get; }
    }
}
