How To Use
==========

Choose your integration:

CLI Tool
--------

Download and run BitMono from the command line.

Download
~~~~~~~~

Get BitMono from `GitHub releases <https://github.com/sunnamed434/BitMono/releases/latest>`_:

- .NET 8 apps: ``BitMono-v<version>-CLI-net8.0-win-x64.zip``
- .NET Framework apps: ``BitMono-v<version>-CLI-net462-win-x64.zip``
- Unity/Mono: use the .NET Framework version

Usage
~~~~~

.. code-block:: console

   BitMono.CLI -f MyApp.exe

More options:

.. code-block:: console

   BitMono.CLI -f MyApp.exe -l Dependencies -o MyObfuscatedApp -p FullRenamer,StringsEncryption

Available options:

- ``-f, --file``: File to obfuscate (required)
- ``-l, --libraries``: Dependencies folder (default: ``libs``). Accepts multiple space-separated folders, e.g. ``-l Dependencies MoreLibs``
- ``-o, --output``: Output folder (default: ``output``)
- ``-p, --protections``: Which protections to use
- ``--protections-file``: Custom protections config file
- ``--criticals-file``: Custom criticals config file
- ``--obfuscation-file``: Custom obfuscation config file
- ``-n, --output-name``: Custom output file name (default: same as input)
- ``--no-watermark``: Turn off watermarking
- ``--nologo``: Don't show the BitMono logo banner on startup (same idea as ``dotnet --nologo``)
- ``--strong-name-key``: Path to strong name key (.snk) file for assembly signing

Setup
~~~~~

Put your files like this:

.. code-block:: text

   my_project/
   ├── MyApp.exe
   └── libs/
       ├── SomeLibrary.dll
       └── AnotherLibrary.dll

The ``libs`` folder has your app's dependencies. BitMono needs these to understand your code.

.NET Global Tool
----------------

Install and use BitMono as a global .NET tool.

Installation
~~~~~~~~~~~~

.. code-block:: console

   dotnet tool install --global BitMono.GlobalTool

Usage
~~~~~

Same as CLI tool, just run ``BitMono`` instead of ``BitMono.CLI``.

Configuration
-------------

BitMono uses these config files:

``protections.json`` - Which protections to use:

.. code-block:: text

   {
     "Protections": [
       {
         "Name": "AntiILdasm",
         "Enabled": false
       },
       {
         "Name": "AntiDe4dot",
         "Enabled": false
       },
       {
         "Name": "FullRenamer",
         "Enabled": true
       },
       {
         "Name": "StringsEncryption",
         "Enabled": false
       },
       {
         "Name": "BitDotNet",
         "Enabled": true
       },
       {
         "Name": "BitMono",
         "Enabled": true
       }
     ]
   }

.. note::

   The order of protections in the configuration determines their execution order. 
   Packers (like BitDotNet and BitMono) always run last, regardless of their position in the configuration.

``criticals.json`` - What NOT to obfuscate:

.. code-block:: text

   {
     "UseCriticalAttributes": true,
     "CriticalAttributes": [
       {
         "Namespace": "UnityEngine",
         "Name": "SerializeField"
       }
     ],
     "UseCriticalModelAttributes": true,
     "CriticalModelAttributes": [
       {
         "Namespace": "System",
         "Name": "SerializableAttribute"
       }
     ],
     "UseCriticalInterfaces": true,
     "CriticalInterfaces": [
       "IRocketPlugin",
       "IOpenModPlugin"
     ],
     "UseCriticalBaseTypes": true,
     "CriticalBaseTypes": [
       "RocketPlugin*",
       "OpenModUnturnedPlugin*"
     ],
     "UseCriticalMethods": true,
     "CriticalMethods": [
       "Awake",
       "Start",
       "Update",
       "OnDestroy"
     ],
     "UseCriticalMethodsStartsWith": true,
     "CriticalMethodsStartsWith": [
       "OV_"
     ]
   }

This file controls what gets excluded from obfuscation:

- **CriticalAttributes** - Exclude members with specific attributes
- **CriticalModelAttributes** - Exclude types with serialization attributes  
- **CriticalInterfaces** - Exclude types that inherit specific interfaces
- **CriticalBaseTypes** - Exclude types that inherit specific base types (supports glob patterns)
- **CriticalMethods** - Exclude methods by exact name
- **CriticalMethodsStartsWith** - Exclude methods that start with specific strings

You can use glob patterns (``*``) in base types and method patterns.

``obfuscation.json`` - General settings:

.. code-block:: text

   {
     "Watermark": true,
     "OutputDirectoryName": "output",
     "OutputFileName": null,
     "Tips": true
   }

- ``OutputFileName``: Custom output file name (e.g., ``"Protected.dll"``). When set, uses the exact name ignoring watermark suffix.
- ``Tips``: Print short, helpful tips after obfuscation (how to exclude members, disable the watermark, use the ``libs`` directory, pick a preset). Set to ``false`` to silence them.

Most settings have sensible defaults. You only need to change them if you want something different.

Unity Integration
-----------------

BitMono includes Unity integration that automatically obfuscates your assemblies during the Unity build process.
The integration hooks into Unity's build pipeline and runs BitMono CLI to protect your game code.

.. note::

   **IL2CPP is supported.** When the scripting backend is IL2CPP, BitMono obfuscates the managed
   ``Assembly-CSharp.dll`` *before* ``il2cpp.exe`` converts it to C++, so renamed names land cloaked in
   ``global-metadata.dat`` (which is what tools like Il2CppDumper read). The Unity integration detects the
   IL2CPP backend automatically and runs only IL2CPP-compatible protections - protections that emit native
   code, ``calli``, pack the PE, etc. are skipped because they would break the ``il2cpp.exe`` build or only
   affect the managed PE that IL2CPP discards. See :doc:`../protection-list/unity` for the per-protection
   list and :doc:`../developers/il2cpp-compatibility` for how this is decided.

Download
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

1. Go to the latest BitMono release on `GitHub <https://github.com/sunnamed434/BitMono/releases/latest>`_
2. For your Unity version, download the archive with both package formats:

   - File name pattern: ``BitMono-Unity-v<version>-Unity<unityVersion>.zip``
   - Example: ``BitMono-Unity-v1.2.3-Unity2021.3.45f1.zip``

   The archive contains:

   - ``BitMono-Unity-v<version>-Unity<unityVersion>.unitypackage`` (classic package)
   - ``BitMono-Unity-UPM-v<version>-Unity<unityVersion>.tgz`` (Unity Package Manager tarball)

Install (choose one)
~~~~~~~~~~~~~~~~~~~~

.. rubric:: Option A: Import the .unitypackage (recommended for most users)

1. Extract the downloaded ``.zip``
2. In Unity: **Assets → Import Package → Custom Package**
3. Select ``BitMono-Unity-v<version>-Unity<unityVersion>.unitypackage``
4. Click **Import**

.. rubric:: Option B: Install via Unity Package Manager (UPM)

1. Extract the downloaded ``.zip``
2. In Unity: **Window → Package Manager**
3. Click the ``+`` dropdown → **Add package from tarball...**
4. Select ``BitMono-Unity-UPM-v<version>-Unity<unityVersion>.tgz``
5. Confirm installation

Project Structure
~~~~~~~~~~~~~~~~~

After importing, your project will contain:

.. code-block:: text

   Assets/
   ├── BitMono.Unity/
   │   ├── Editor/
   │   │   ├── BitMonoBuildProcessor.cs    # Build hook implementation
   │   │   ├── BitMonoConfig.cs            # Configuration ScriptableObject
   │   │   ├── BitMonoConfigInspector.cs   # Unity Inspector UI
   │   │   └── BitMono.Unity.Editor.asmdef # Assembly definition
   │   ├── BitMonoConfig.asset             # Your configuration file
   │   └── package.json                    # Unity Package Manager metadata
   └── BitMono.CLI/
       ├── BitMono.CLI.exe                 # The actual obfuscation tool
       ├── protections.json                # Protection settings
       ├── obfuscation.json                # Obfuscation settings
       └── criticals.json                  # What not to obfuscate

Configuration
~~~~~~~~~~~~~

1. In Unity, go to **Window → BitMono → Configuration**
2. Check **Enable Obfuscation** to turn on BitMono
3. That's it! BitMono will automatically protect your code during builds

The integration comes with sensible defaults. You only need to change settings if you want something different.

Usage
~~~~~

Just build your project normally:

1. Go to **File → Build Settings → Build**
2. BitMono automatically obfuscates your code during the build
3. Your final build contains protected code

That's it! No extra steps needed.

MSBuild Integration (NuGet)
---------------------------

Obfuscate your project automatically on every ``Release`` build by adding a single NuGet package
reference — no separate tool run. Install ``BitMono.Integration`` as a development dependency:

.. code-block:: xml

   <ItemGroup>
     <PackageReference Include="BitMono.Integration" Version="0.41.1">
       <PrivateAssets>all</PrivateAssets>
       <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
     </PackageReference>
   </ItemGroup>

Configure it with the same ``protections.json`` / ``criticals.json`` / ``obfuscation.json`` files placed
next to your ``.csproj``. See the dedicated :doc:`msbuild-integration` guide for full details.

Troubleshooting
---------------

For detailed troubleshooting information, see the `troubleshooting guide <troubleshooting.html>`_.

NuGet Package Integration (For Developers)
------------------------------------------

.. note::

   This section is for developers who want to integrate BitMono into their own obfuscation tools or build custom solutions. For regular users, the CLI tool or Unity integration are recommended.

BitMono is also available as NuGet packages, allowing you to integrate obfuscation capabilities directly into your own applications or build custom obfuscation tools.

Available Packages
~~~~~~~~~~~~~~~~~~

**Core Components:**

- `BitMono.API <https://www.nuget.org/packages/BitMono.API/>`_ - Core interfaces and abstractions
- `BitMono.Core <https://www.nuget.org/packages/BitMono.Core/>`_ - Main obfuscation engine
- `BitMono.Protections <https://www.nuget.org/packages/BitMono.Protections/>`_ - Collection of protection implementations
- `BitMono.Shared <https://www.nuget.org/packages/BitMono.Shared/>`_ - Shared utilities and models

**Host & Utilities:**

- `BitMono.Host <https://www.nuget.org/packages/BitMono.Host/>`_ - Application host framework
- `BitMono.Utilities <https://www.nuget.org/packages/BitMono.Utilities/>`_ - Helper functions and utilities
- `BitMono.Obfuscation <https://www.nuget.org/packages/BitMono.Obfuscation/>`_ - High-level obfuscation orchestrator
- `BitMono.Runtime <https://www.nuget.org/packages/BitMono.Runtime/>`_ - Runtime components for obfuscated assemblies

Configuration
~~~~~~~~~~~~~

When using NuGet packages, you'll need to configure BitMono programmatically or through configuration files. See the `developer documentation <../developers/configuration.html>`_ for detailed configuration options.

Dependencies
~~~~~~~~~~~~

BitMono NuGet packages may use nightly versions of AsmResolver. If you encounter dependency resolution issues, see the `NuGet configuration guide <nuget-configuration.html>`_ for setup instructions.

Next Steps
----------

- Read about `available protections <../protection-list/overview.html>`_
- Learn about `configuration options <../configuration/protections.html>`_
- Check `best practices <../bestpractices/zero-risk-obfuscation.html>`_
- Explore `developer documentation <../developers/first-protection.html>`_ for custom protections
