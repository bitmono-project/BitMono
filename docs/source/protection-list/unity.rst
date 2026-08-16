Unity Protections
=================

This section lists various protections compatible with the `Unity Engine <https://unity.com/>`_. 

Unity Runtime is typically based on the .NET Framework 4.*, depending on the Unity version in use. However, it is primarily executed on `Mono <https://github.com/mono/mono>`_, with Unity using its `own fork of Mono <https://github.com/Unity-Technologies/mono>`_ specifically for Unity Runtime. 

You can also refer to this list for Mono runtime protections, but be aware that while Unity's Mono fork is similar to the standard Mono runtime, there may be differences that could impact compatibility.

Protection List
---------------

- **StringsEncryption**  
  Can significantly slow down your application.

- **BitDotNet**  
  Do not use with Unity versions higher than 2020.*. and instead use BitDecompiler

- **BitMethodDotnet**

- **DotNetHook**

- **CallToCalli**

- **ObjectReturnType**  
  Unstable.

- **NoNamespaces**  
  May cause issues if you rely heavily on Reflection.

- **FullRenamer**  
  May cause issues if you rely heavily on Reflection.

- **AntiDebugBreakpoints**  
  Unstable.

- **AntiDecompiler**

- **BitDecompiler**

- **BitTimeDateStamp**

- **BitMono**

- **BillionNops**

- **AntiDe4dot**

- **AntiILdasm**

The list above targets the **Mono** scripting backend, where the protected ``Assembly-CSharp.dll`` ships
as-is.

IL2CPP builds
-------------

With the **IL2CPP** scripting backend the managed assembly is not shipped: ``il2cpp.exe`` consumes it and
converts it to C++ (``GameAssembly.dll``), keeping a copy of every class/method/field name in
``global-metadata.dat``. That metadata is what tools like `Il2CppDumper <https://github.com/Perfare/Il2CppDumper>`_
read to reconstruct your code, so the useful obfuscation is whatever **survives into the metadata**.

BitMono obfuscates the managed assembly *before* ``il2cpp.exe`` runs, so name and string obfuscation carry
through into ``global-metadata.dat``. The Unity integration detects the IL2CPP backend automatically (or set
``"IL2CPP": true`` in ``obfuscation.json`` / pass ``--il2cpp`` to the CLI) and runs **only the
IL2CPP-compatible protections**, skipping the rest with a clear log line for each.

IL2CPP-compatible (kept):

- **FullRenamer** - renamed names are written cloaked into ``global-metadata.dat``.
- **NoNamespaces** - clears namespaces in the metadata.
- **StringsEncryption** - removes plaintext strings from the metadata; the decryptor is AOT-compiled to C++.
- **AntiDebugBreakpoints** - pure managed timing checks that AOT-compile and still run at runtime.

Skipped on IL2CPP (would break the ``il2cpp.exe`` build, or only affect the discarded managed PE):
**UnmanagedString**, **CallToCalli**, **DotNetHook**, **BitMethodDotnet**, **ObjectReturnType**,
**AntiDe4dot**, **BillionNops**, **AntiILdasm**, **BitTimeDateStamp**, **AntiDecompiler**, **BitMono**,
**BitDotNet**, **BitDecompiler**, **LocalVariableEncoding**.

Inspecting the metadata
~~~~~~~~~~~~~~~~~~~~~~~~~

To see what actually ended up in a build's ``global-metadata.dat`` (its il2cpp version, and how many
identifier names and string literals it carries), point the CLI at the file:

.. code-block:: text

   BitMono.CLI --inspect-metadata "path/to/global-metadata.dat"

It just reads and prints - it doesn't change the file. It also splits the names into *reserved* ones that
must never be renamed (``Awake``, ``Start``, constructors, anything the engine looks up by name) and
*rename candidates*. If the candidates still read like plain English, your renaming didn't reach the
metadata. Handy for confirming your names came through obfuscated, or before reporting a metadata issue.

.. note::

   The full job of protecting the IL2CPP *output* itself - encrypting ``global-metadata.dat`` and
   injecting a native decryptor into ``GameAssembly.dll`` - is tracked in
   `#276 <https://github.com/sunnamed434/BitMono/issues/276>`_. The ``--inspect-metadata`` reader above is
   the first piece of it; the native decryptor half is still to come.

Additional Considerations
-------------------------

- Some of these protections may not be suitable for all Unity versions or Mono runtimes.
- When using optional protections like `NoNamespaces` or `FullRenamer`, ensure to test thoroughly if your application uses extensive Reflection, as they might break functionality.
