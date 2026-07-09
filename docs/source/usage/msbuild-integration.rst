MSBuild Integration (NuGet)
===========================

Obfuscate your project automatically on every build by adding one NuGet ``<PackageReference>``, no
separate tool run, no extra build step. This is the easiest way to protect a .NET project: install the
package, build in ``Release``, and the output assembly comes out obfuscated.

The package is ``BitMono.Integration``. It bundles the BitMono CLI and hooks into your build, so it
works the same from Visual Studio, ``dotnet build`` / ``dotnet publish``, GitHub Actions, Azure DevOps,
or any other CI.

.. note::

   Install ``BitMono.Integration`` only in the projects you actually want to obfuscate, usually the
   final app or the assembly you ship. Adding it to every project in a solution is pointless.

Requirements
------------

The .NET 8 (or newer) runtime has to be on the build machine. The obfuscator runs out-of-process via
``dotnet``, and the SDK that builds your project already covers that.

Installation
------------

Add it as a development-only dependency so it never leaks into your own package or to projects that
reference yours:

.. code-block:: xml

   <ItemGroup>
     <PackageReference Include="BitMono.Integration" Version="0.41.1">
       <PrivateAssets>all</PrivateAssets>
       <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
     </PackageReference>
   </ItemGroup>

.. note::

   Use the latest version from `nuget.org <https://www.nuget.org/packages/BitMono.Integration/>`_.

Rebuild, and in ``Release`` you'll see ``[BitMono] Obfuscated <YourAssembly>.dll successfully.`` in the
build output.

When it runs
------------

By default obfuscation runs only in ``Release``, so your ``Debug`` inner loop stays fast and
debuggable. It happens after compilation, on the intermediate assembly, so the protected assembly flows
into both ``bin`` (``dotnet build``) and your ``dotnet publish`` output automatically. Works for
executables and libraries alike.

Configuration
-------------

``BitMono.Integration`` uses the same JSON config files as the CLI. Drop any of these next to your
``.csproj`` and they're picked up automatically:

- ``protections.json`` — which protections to enable
- ``criticals.json`` — what to exclude from obfuscation
- ``obfuscation.json`` — general settings (watermark, output name, ...)

See :doc:`how-to-use` for the full schema of each. A minimal ``protections.json``:

.. code-block:: json

   {
     "Protections": [
       { "Name": "FullRenamer", "Enabled": true },
       { "Name": "StringsEncryption", "Enabled": true }
     ]
   }

If you don't ship any of these files, BitMono falls back to the defaults bundled in the package.

Don't want config files in your repo? Pick the protections (or a preset) right in the ``.csproj``:

.. code-block:: xml

   <PropertyGroup>
     <!-- specific protections... -->
     <BitMonoProtections>FullRenamer;StringsEncryption</BitMonoProtections>
     <!-- ...or a preset instead -->
     <!-- <BitMonoPreset>Balanced</BitMonoPreset> -->
   </PropertyGroup>

.. important::

   Keep your config files out of the build output so they're not shipped with your app:

   .. code-block:: xml

      <ItemGroup>
        <None Update="obfuscation.json;protections.json;criticals.json"
              CopyToOutputDirectory="Never" />
        <Content Remove="obfuscation.json;protections.json;criticals.json" />
      </ItemGroup>

MSBuild properties
------------------

Everything is overridable from your ``.csproj`` (or a ``Directory.Build.props``):

.. list-table::
   :header-rows: 1
   :widths: 30 20 50

   * - Property
     - Default
     - Description
   * - ``BitMonoEnabled``
     - ``true``
     - Master on/off switch. Set ``false`` to skip obfuscation entirely.
   * - ``BitMonoConfigurations``
     - ``Release``
     - Semicolon-separated configurations to obfuscate in. Set empty to run in every configuration.
   * - ``BitMonoFailOnError``
     - ``true``
     - Fail the build if obfuscation fails. Set ``false`` to warn and continue (output is not obfuscated).
   * - ``BitMonoNoWatermark``
     - ``false``
     - Disable the BitMono watermark.
   * - ``BitMonoStrongNameKey``
     - *(empty)*
     - Path to a ``.snk`` to re-sign the obfuscated assembly (see Signing below).
   * - ``BitMonoProtections``
     - *(empty)*
     - Semicolon-separated protection list (e.g. ``FullRenamer;StringsEncryption``) set right in the
       ``.csproj``, no ``protections.json`` file needed. Wins over ``protections.json`` and ``BitMonoPreset``.
   * - ``BitMonoPreset``
     - *(empty)*
     - Protection preset: ``Minimal``, ``Balanced`` or ``Maximum``. Used when ``BitMonoProtections`` is not set.
   * - ``BitMonoObfuscationFile`` / ``BitMonoProtectionsFile`` / ``BitMonoCriticalsFile`` / ``BitMonoLoggingFile``
     - project-root JSON
     - Override the path to a specific config file.
   * - ``BitMonoDotnetHost``
     - ``dotnet``
     - The host used to run the bundled CLI; set to an absolute ``dotnet`` path for unusual setups.

Example, obfuscate in both Debug and Release and turn off the watermark:

.. code-block:: xml

   <PropertyGroup>
     <BitMonoConfigurations></BitMonoConfigurations>
     <BitMonoNoWatermark>true</BitMonoNoWatermark>
   </PropertyGroup>

Multi-targeted projects
-----------------------

Projects with multiple ``<TargetFrameworks>`` are fully supported, each target framework's output is
obfuscated independently during its inner build.

VB.NET and F#
-------------

BitMono works on the compiled IL and the integration hooks MSBuild (not the C# compiler), so it behaves
the same for VB.NET and F#. Add the exact same ``<PackageReference>`` to your ``.vbproj`` / ``.fsproj``
and build in ``Release``:

.. code-block:: xml

   <!-- MyVbApp.vbproj -->
   <ItemGroup>
     <PackageReference Include="BitMono.Integration" Version="0.41.1">
       <PrivateAssets>all</PrivateAssets>
       <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
     </PackageReference>
   </ItemGroup>

The same ``protections.json`` / ``criticals.json`` / ``obfuscation.json`` apply, and running the CLI
directly (``BitMono.CLI -f MyVbApp.dll``) works too.

Signing
-------

If your project is strong-name signed (``<SignAssembly>true</SignAssembly>`` with
``<AssemblyOriginatorKeyFile>``), point ``BitMonoStrongNameKey`` at the same ``.snk`` so the obfuscated
assembly gets re-signed. See :doc:`assembly-signing`.

Troubleshooting
---------------

- Build fails with a non-zero exit code from BitMono? Read the ``[BitMono]`` messages above the error,
  they include the full CLI command and the obfuscator's log. See :doc:`troubleshooting`.
- Nothing happens in Debug? That's expected, obfuscation runs only in ``Release`` by default. Set
  ``<BitMonoConfigurations></BitMonoConfigurations>`` to run in every configuration.
