namespace BitMono.Obfuscation.Tests.Protections;

internal readonly record struct LocalVariableEncodingAffineEncoding(
    int BitWidth,
    ulong XorKey,
    ulong Multiplier,
    ulong Addend);
