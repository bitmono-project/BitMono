Protection Presets
===================

A **preset** is a protection *level* that you choose explicitly. Instead of toggling every
protection in ``protections.json`` by hand, you pick one level and BitMono enables a curated
set of protections for you.

Presets are always **chosen by you, never auto-detected**. BitMono cannot reliably tell which
runtime your app targets — Mono runs ordinary .NET assemblies, so a Mono/Unity build looks
identical to a .NET Core/Framework one — so the choice of runtime-specific protections stays
in your hands. BitMono will still *warn* you (without blocking) when a preset enables a
protection intended for a different runtime than the one your module targets.

Levels
------

.. list-table::
   :header-rows: 1
   :widths: 20 80

   * - Preset
     - Protections enabled
   * - ``Custom`` (default)
     - Uses ``protections.json`` exactly as configured. Nothing changes for existing setups.
   * - ``Minimal``
     - ``FullRenamer``, ``NoNamespaces``, ``BitTimeDateStamp``
   * - ``Balanced``
     - Minimal + ``StringsEncryption``, ``ObjectReturnType``, ``AntiDe4dot``, ``AntiILdasm``,
       ``BillionNops``
   * - ``Maximum``
     - Balanced + ``AntiDebugBreakpoints``, ``AntiDecompiler``, ``UnmanagedString``, ``DotNetHook``,
       ``CallToCalli``, ``BitMethodDotnet``, ``BitDecompiler``, ``BitDotNet``, ``BitMono``

.. note::

   ``Maximum`` includes the Mono-only packers (``BitDotNet``, ``BitDecompiler``), the native
   ``UnmanagedString``, and the runtime hookers (``DotNetHook``, ``CallToCalli``). These are the
   most aggressive and the most runtime-specific — make sure they fit your target before shipping.
   See :doc:`../developers/protection-runtime-moniker` and :doc:`../developers/native-code`.

``LocalVariableEncoding`` is intentionally absent from every preset. Its interactions with complete
preset combinations and non-CoreCLR runtimes have not been validated. Enable it explicitly with a
``Custom`` configuration in ``protections.json`` or with ``-p LocalVariableEncoding``.

How to choose a preset
----------------------

**Command line** (wins over the config file):

.. code-block:: console

   BitMono.CLI --file MyApp.dll --preset Balanced

**Configuration file** (``obfuscation.json``):

.. code-block:: json

   {
     "Preset": "Balanced"
   }

Precedence
----------

When BitMono decides which protections to run, it uses the first of these that applies:

#. An explicit protections list on the command line (``-p`` / ``--protections``).
#. ``--preset`` on the command line.
#. ``"Preset"`` in ``obfuscation.json``.
#. ``protections.json`` (used when the preset is ``Custom`` or unset).

So a preset overrides ``protections.json``, an explicit ``-p`` list still wins over a preset,
and the default ``Custom`` keeps today's behaviour.
