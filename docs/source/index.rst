BitMono
=======

This is the documentation of the BitMono project. BitMono is not only a tool that can be used for a two clicks to obfuscate your file, even for your own plugins and purposes - for example BitMono can be used as an Engine for your own obfuscation.

Most of the questions/problems in BitMono will be solved by just learning `AsmResolver docs <https://docs.washi.dev/asmresolver/index.html>`_

Join our `Discord Server <https://discord.gg/sFDHd47St4>`_ you will find there help to your question(s) or just a nice talk!

Table of Contents:
------------------

.. toctree::
   :maxdepth: 1
   :caption: Usage
   :name: sec-usage

   usage/how-to-use
   usage/msbuild-integration
   usage/github-actions
   usage/native-aot
   usage/assembly-signing
   usage/nuget-configuration
   usage/troubleshooting

.. toctree::
   :maxdepth: 1
   :caption: Protections
   :name: sec-protections

   protections/antiildasm
   protections/antide4dot
   protections/bitdotnet
   protections/bitdecompiler
   protections/bitmono
   protections/bittimedatestamp
   protections/bitmethoddotnet
   protections/localvariableencoding
   protections/antidecompiler
   protections/antidebugbreakpoints
   protections/calltocalli
   protections/dotnethook
   protections/fullrenamer
   protections/objectreturntype
   protections/stringsencryption
   protections/unmanagedstring
   protections/nonamespaces
   protections/billionnops

.. toctree::
   :maxdepth: 1
   :caption: Protection List
   :name: sec-protection-list

   protection-list/overview
   protection-list/unity


.. toctree::
   :maxdepth: 1
   :caption: Developers
   :name: sec-developers

   developers/first-protection
   developers/plugins
   developers/obfuscation-execution-order
   developers/which-base-protection-select
   developers/protection-runtime-moniker
   developers/il2cpp-compatibility
   developers/native-code
   developers/do-not-resolve-members
   developers/configuration
   developers/obfuscate-build
   developers/building
   developers/unity-integration-testing


.. toctree::
   :maxdepth: 1
   :caption: Obfuscation Issues
   :name: sec-obfuscationissues

   obfuscationissues/corlib-not-found
   obfuscationissues/intended-for
   obfuscationissues/compatibility
   obfuscationissues/ready-to-run


.. toctree::
   :maxdepth: 1
   :caption: Best Practices
   :name: sec-bestpractices

   bestpractices/bitmono-combo
   bestpractices/zero-risk-obfuscation


.. toctree::
   :maxdepth: 1
   :caption: Configuration
   :name: sec-configuration

   configuration/presets
   configuration/exclude-obfuscation
   configuration/third-party-issues
   configuration/protections


.. toctree::
   :maxdepth: 1
   :caption: Frequently Asked Questions
   :name: sec-faq

   faq/costura-support
   faq/disable-path-masking
   faq/unable-to-reference-after-protect
   faq/when-and-why-use-bitmono
