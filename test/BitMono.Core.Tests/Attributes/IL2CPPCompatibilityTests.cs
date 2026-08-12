using System.Threading.Tasks;
using BitMono.API.Protections;
using BitMono.Core.Attributes;
using BitMono.Core.Extensions;

namespace BitMono.Core.Tests.Attributes;

public class IL2CPPCompatibilityTests
{
    // Pins the per-protection classification. Each of these emits native code, calli, packs the PE, or
    // produces IL/metadata il2cpp.exe can't handle - verified from each protection's source. See #250.
    public static IEnumerable<object[]> IncompatibleProtections() => new[]
    {
        new object[] { typeof(CallToCalli) },
        new object[] { typeof(DotNetHook) },
        new object[] { typeof(BitMethodDotnet) },
        new object[] { typeof(ObjectReturnType) },
        new object[] { typeof(AntiDe4dot) },
        new object[] { typeof(BillionNops) },
        new object[] { typeof(AntiILdasm) },
        new object[] { typeof(BitTimeDateStamp) },
        new object[] { typeof(UnmanagedString) },
        new object[] { typeof(global::BitMono.Protections.BitMono) },
        new object[] { typeof(BitDotNet) },
        new object[] { typeof(BitDecompiler) },
        new object[] { typeof(AntiDecompiler) },
        new object[] { typeof(LocalVariableEncoding) },
    };

    // These do only pure managed metadata/IL edits that survive into global-metadata.dat or run AOT.
    public static IEnumerable<object[]> CompatibleProtections() => new[]
    {
        new object[] { typeof(FullRenamer) },
        new object[] { typeof(NoNamespaces) },
        new object[] { typeof(StringsEncryption) },
        new object[] { typeof(AntiDebugBreakpoints) },
    };

    [Theory]
    [MemberData(nameof(IncompatibleProtections))]
    public void IncompatibleProtection_IsDetected(Type protectionType)
    {
        protectionType
            .IsIL2CPPIncompatible()
            .ShouldBeTrue($"{protectionType.Name} cannot run on IL2CPP builds");
    }

    [Theory]
    [MemberData(nameof(CompatibleProtections))]
    public void CompatibleProtection_IsNotExcluded(Type protectionType)
    {
        protectionType
            .IsIL2CPPIncompatible()
            .ShouldBeFalse($"{protectionType.Name} should still run on IL2CPP builds");
    }

    [Fact]
    public void PlainProtection_IsCompatible()
    {
        typeof(PlainDouble).IsIL2CPPIncompatible().ShouldBeFalse();
    }

    [Fact]
    public void ExplicitlyMarkedProtection_IsIncompatible_WithItsReason()
    {
        typeof(MarkedDouble).IsIL2CPPIncompatible().ShouldBeTrue();
        new MarkedDouble().GetIL2CPPIncompatibleReason().ShouldBe("custom reason");
    }

    [Fact]
    public void NativeCodeProtection_IsIncompatible_ViaConfigureForNativeCode()
    {
        // A protection that emits native code must be excluded even without an explicit attribute.
        typeof(NativeDouble).IsIL2CPPIncompatible().ShouldBeTrue();
        new NativeDouble().GetIL2CPPIncompatibleReason().ShouldNotBeNullOrWhiteSpace();
    }

    private class PlainDouble : IProtection
    {
        public Task ExecuteAsync() => Task.CompletedTask;
    }

    [IL2CPPIncompatible("custom reason")]
    private class MarkedDouble : IProtection
    {
        public Task ExecuteAsync() => Task.CompletedTask;
    }

    [ConfigureForNativeCode]
    private class NativeDouble : IProtection
    {
        public Task ExecuteAsync() => Task.CompletedTask;
    }
}
