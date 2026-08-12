using BitMono.Core.Attributes;
using BitMono.Core.Extensions;

namespace BitMono.Core.Tests.Attributes;

public sealed class RuntimeMonikerTests
{
    [Fact]
    public void LocalVariableEncodingSupportsOnlyNetCore()
    {
        var monikers = typeof(LocalVariableEncoding).GetRuntimeMonikerAttributes();
        monikers.Length.ShouldBe(1);
        monikers[0].ShouldBeOfType<RuntimeMonikerNETCore>();
        monikers[0].Name.ShouldBe(KnownRuntimeMonikers.NETCore);
    }
}
