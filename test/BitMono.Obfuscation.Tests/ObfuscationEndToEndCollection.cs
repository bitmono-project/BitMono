using Xunit;

namespace BitMono.Obfuscation.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ObfuscationEndToEndCollection
{
    public const string Name = "ObfuscationEndToEnd";
}
