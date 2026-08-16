using BitMono.Shared.Models;

namespace BitMono.Shared.Tests;

public sealed class ProtectionPresetsTests
{
    [Theory]
    [InlineData(ObfuscationPreset.Minimal)]
    [InlineData(ObfuscationPreset.Balanced)]
    [InlineData(ObfuscationPreset.Maximum)]
    public void Expand_ExcludesOptInLocalVariableEncoding(ObfuscationPreset preset)
    {
        var protectionSettings = ProtectionPresets.Expand(preset);

        protectionSettings.ShouldNotBeNull();
        protectionSettings.Protections.ShouldNotContain(
            protection => protection.Name == "LocalVariableEncoding");
    }
}
