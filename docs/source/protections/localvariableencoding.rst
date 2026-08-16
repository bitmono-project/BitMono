LocalVariableEncoding
=====================

How it works?
-------------

LocalVariableEncoding encodes supported CIL integral and enum local slots while their values are at rest
between ``stloc`` and ``ldloc`` instructions. Each eligible store writes an affine encoding, and each
eligible load applies its inverse before the value is used. The arithmetic is reversible modulo the
local type's bit width, so the method observes the original value.

Eligibility
-----------

The protection leaves a method unchanged when ``InitLocals`` is false. Within eligible methods, it
supports character, signed and unsigned 8-, 16-, 32-, and 64-bit integer locals, including enums with
those underlying types. It skips Boolean locals, locals whose address is taken, and locals with
unsupported types. Reference, floating-point, native-sized integer, pointer, by-reference, generic,
and aggregate local types are not encoded.

Scope
-----

This is CIL obfuscation, not encryption of the native stack frame produced by the JIT. A JIT compiler
may remove the reversible arithmetic, keep a value only in a CPU register, or otherwise change its
physical representation. LocalVariableEncoding therefore does not guarantee that local values are
secret in process memory.

End-to-end behavior is verified on CoreCLR. Compatibility with Mono and Unity's Mono backend has not
been validated and is not claimed.

LocalVariableEncoding is skipped for IL2CPP builds because its rewritten local-slot IL has not been
validated through the IL2CPP conversion pipeline.

The protection is opt-in and is intentionally absent from every preset. Enable it explicitly in a
``Custom`` protection configuration.

Ordering
--------

Place LocalVariableEncoding after standalone protections that rewrite existing target method bodies.
In a custom protection list, keep it after protections such as AntiDebugBreakpoints, CallToCalli, and
BitMethodDotnet. The target-method list is captured before protections run, so methods introduced by
another protection are outside this pass. Pipeline protections always run after standalone
protections and may still introduce later IL changes.

Protection Type
---------------

The protection type is ``Protection``.
