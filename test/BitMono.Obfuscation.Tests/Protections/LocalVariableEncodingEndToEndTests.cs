using System.Threading.Tasks;
using System.IO;
using AsmResolver.DotNet;
using BitMono.Core.Services;
using Xunit;
using static BitMono.Obfuscation.Tests.Protections.LocalVariableEncodingCilAssertions;
using static BitMono.Obfuscation.Tests.Protections.LocalVariableEncodingCilInspection;

namespace BitMono.Obfuscation.Tests.Protections;

[Collection(global::BitMono.Obfuscation.Tests.ObfuscationEndToEndCollection.Name)]
public sealed class LocalVariableEncodingEndToEndTests
{
    [Fact]
    public async Task SerializedAffineCodecPreservesExecutionAndExcludedLocals()
    {
        using var fixture = new LocalVariableEncodingEndToEndFixture("bitmono-local-variable-encoding-e2e");
        await fixture.AssertExecutionAsync(fixture.WorkingAssemblyPath, "baseline");

        bool succeeded = await fixture.ObfuscateAsync();
        succeeded.ShouldBeTrue();

        string protectedAssemblyPath = fixture.PrepareProtectedAssembly();
        await fixture.AssertExecutionAsync(protectedAssemblyPath, "protected");

        var baselineModule = ModuleDefinition.FromBytes(File.ReadAllBytes(fixture.WorkingAssemblyPath));
        var protectedModule = ModuleDefinition.FromBytes(File.ReadAllBytes(protectedAssemblyPath));

        foreach (string methodName in EligibleMethodNames)
        {
            HasCompleteAffineCodec(baselineModule, methodName).ShouldBeFalse(
                $"baseline method {methodName} must fail the serialized codec predicate");
            HasCompleteAffineCodec(protectedModule, methodName).ShouldBeTrue(
                $"protected method {methodName} must encode and decode every eligible local access");
        }

        AssertAddressedLocalHasNoCodec(protectedModule, "AddressTaken");
        foreach (string methodName in UnsupportedMethodNames)
            AssertUnsupportedLocalsHaveNoCodec(protectedModule, methodName);

        HasCompleteAffineCodec(baselineModule, "DefaultInitializedLocal").ShouldBeFalse(
            "baseline default-initialized local must fail the serialized codec predicate");
        AssertDefaultInitializedLocalCodec(baselineModule, protectedModule);
        AssertUninitializedLocalBypass(baselineModule, protectedModule);
        AssertBranchTargetStoreCodec(baselineModule, protectedModule);
        AssertExceptionBoundaryLocalCodec(baselineModule, protectedModule);
        AssertInjectedUnsupportedLocalBypasses(baselineModule, protectedModule);
        LocalVariableEncodingAddressTakenAssertions.AssertTransformation(baselineModule, protectedModule);
        LocalVariableEncodingBooleanAssertions.AssertBypass(
            fixture.WorkingAssemblyPath,
            protectedAssemblyPath,
            baselineModule,
            protectedModule);
        LocalVariableEncodingUnresolvedValueTypeAssertions.AssertBypass(
            fixture.WorkingAssemblyPath,
            protectedAssemblyPath,
            baselineModule,
            protectedModule);
    }

    [Fact]
    public async Task DeterministicRandomScriptExercisesKeyFallbacksAndCollisionRepair()
    {
        using var fixture = new LocalVariableEncodingEndToEndFixture(
            "bitmono-local-variable-encoding-deterministic-e2e");
        int[] scriptedWords =
        {
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0xfffd,
            0xffff,
            0xffff,
            0xffff
        };
        int randomCallCount = 0;
        RandomNext scriptedRandomNext = (minimumValue, maximumValue) =>
        {
            minimumValue.ShouldBe(0);
            maximumValue.ShouldBe(1 << 16);
            int scriptedWord = scriptedWords[randomCallCount % scriptedWords.Length];
            randomCallCount++;
            return scriptedWord;
        };

        await fixture.AssertExecutionAsync(fixture.WorkingAssemblyPath, "deterministic baseline");

        bool succeeded = await fixture.ObfuscateAsync(scriptedRandomNext);
        succeeded.ShouldBeTrue();
        randomCallCount.ShouldBeGreaterThan(0);
        (randomCallCount % scriptedWords.Length).ShouldBe(0,
            "each local encoding must consume the complete 12-word script");

        string protectedAssemblyPath = fixture.PrepareProtectedAssembly();
        await fixture.AssertExecutionAsync(protectedAssemblyPath, "deterministic protected");

        var protectedModule = ModuleDefinition.FromBytes(File.ReadAllBytes(protectedAssemblyPath));
        AssertDeterministicCodecs(protectedModule);
    }
}
