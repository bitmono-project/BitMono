using BitMono.API.Protections;
using BitMono.Shared.DependencyInjection;
using BitMono.Shared.Logging;
using BitMono.Shared.Models;
using BitMono.Shared.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BitMono.Host.Extensions;

/// <summary>
/// Extension methods for configuring the BitMono container.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMethodReturnValue.Global")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
public static class BitMonoContainerExtensions
{
    private const string ProtectionsFileName = "BitMono.Protections.dll";
    private const string ProtectionsAssemblyName = "BitMono.Protections";
    private const string UnityFileName = "BitMono.Unity.dll";
    private const string DefaultPluginsDirectoryName = "plugins";
    // The plugin SDK contract: any real protection implements IProtection, which lives here, so the
    // compiler always emits this reference into a plugin's IL - making it a reliable compatibility anchor.
    private const string ContractAssemblyName = "BitMono.API";

    /// <summary>
    /// Loads and registers protection assemblies, including any drop-in plugins (see #227).
    /// </summary>
    /// <param name="container">Container to register protections in</param>
    /// <param name="file">Optional path to protections DLL</param>
    /// <returns>Container for chaining</returns>
    public static Container AddProtections(this Container container, string? file = null)
    {
        var protectionsFilePath = file ?? Path.Combine(AppContext.BaseDirectory, ProtectionsFileName);
        if (File.Exists(protectionsFilePath))
        {
            Assembly.Load(File.ReadAllBytes(protectionsFilePath));
        }
        else
        {
            // Single-file (Costura) build, e.g. the net462 CLI bundled in the Unity package: BitMono.Protections
            // is embedded in the host exe, not a loose DLL on disk, so load it by name and let Costura's
            // AssemblyResolve hook supply the embedded copy (#302).
            Assembly.Load(new AssemblyName(ProtectionsAssemblyName));
        }

        var unityFilePath = Path.Combine(AppContext.BaseDirectory, UnityFileName);
        if (File.Exists(unityFilePath))
        {
            var unityRawData = File.ReadAllBytes(unityFilePath);
            Assembly.Load(unityRawData);
        }

        var logger = container.GetService(typeof(ILogger)) as ILogger;
        var scanLogger = logger?.ForContext(typeof(BitMonoContainerExtensions));

        // Load drop-in plugins before scanning so their protections are discovered alongside the built-ins.
        var pluginAssemblies = LoadPlugins(container, logger);
        var incompatiblePlugins = GetIncompatiblePlugins(pluginAssemblies, scanLogger);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // Collect all protection types (excluding IPhaseProtection)
        var protectionTypes = new List<Type>();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
                scanLogger?.Warning("Some types in '{0}' could not be loaded: {1}",
                    assembly.GetName().Name ?? assembly.FullName ?? "?",
                    string.Join("; ", ex.LoaderExceptions.Where(e => e != null).Select(e => e!.Message).Distinct()));
            }

            foreach (var type in types)
            {
                // Open generic definitions can't be instantiated by the container, so skip them.
                if (!type.IsClass || type.IsAbstract || !type.IsPublic || type.IsGenericTypeDefinition)
                    continue;

                // Skip protections from a plugin built against a newer BitMono.API (already warned, #227).
                if (incompatiblePlugins.Contains(type.Assembly))
                    continue;

                // IPhaseProtection extends IProtection, so it MUST be checked first - phase protections
                // are driven by their pipeline, not registered as standalone protections.
                if (typeof(IPhaseProtection).IsAssignableFrom(type))
                    continue;

                if (typeof(IProtection).IsAssignableFrom(type))
                {
                    protectionTypes.Add(type);
                    // Register the concrete type
                    container.Register(type, type).AsSingleton();
                }
                else if (type.GetInterfaces().Any(i => i.Name == nameof(IProtection)))
                {
                    // Implements an interface *named* IProtection but not BitMono's contract - almost always
                    // a plugin shipping its own copy of BitMono.API, which would be silently ignored (#227).
                    scanLogger?.Warning(
                        "Type '{0}' in '{1}' looks like a protection but references a different BitMono.API and " +
                        "will be ignored. Reference BitMono.API with Private=\"false\" and don't ship it beside your plugin.",
                        type.FullName ?? type.Name, type.Assembly.GetName().Name ?? "?");
                }
            }
        }

        // Register the collection factory
        container.Register<ICollection<IProtection>>(() =>
        {
            var list = new List<IProtection>();
            foreach (var protType in protectionTypes)
            {
                try
                {
                    var instance = container.GetService(protType);
                    if (instance is IProtection protection)
                    {
                        list.Add(protection);
                    }
                }
                catch (Exception ex)
                {
                    // A broken plugin protection must not abort the whole run - log and skip it (#227).
                    scanLogger?.Error(ex,
                        "Failed to instantiate protection '{0}'. Its constructor parameters must be resolvable by the container.",
                        protType.FullName ?? protType.Name);
                }
            }
            return list;
        }).AsSingleton();

        return container;
    }

    private static IReadOnlyList<Assembly> LoadPlugins(Container container, ILogger? logger)
    {
        // No logger means the host wasn't fully set up; plugin loading relies on logging for diagnostics.
        if (logger == null)
            return [];

        var obfuscationSettings = container.GetService(typeof(ObfuscationSettings)) as ObfuscationSettings;
        var directoryName = obfuscationSettings?.PluginsDirectoryName;
        if (string.IsNullOrWhiteSpace(directoryName))
            directoryName = DefaultPluginsDirectoryName;

        var pluginsDirectory = Path.IsPathRooted(directoryName)
            ? directoryName!
            : Path.Combine(AppContext.BaseDirectory, directoryName!);

        return new PluginLoader(pluginsDirectory, logger).LoadPlugins();
    }

    // Plugins built against a newer BitMono.API than this build provides are skipped with a clear warning,
    // instead of failing later with a cryptic MissingMethodException mid-obfuscation (#227).
    private static HashSet<Assembly> GetIncompatiblePlugins(IReadOnlyList<Assembly> plugins, ILogger? logger)
    {
        var incompatible = new HashSet<Assembly>();
        var hostVersion = typeof(IProtection).Assembly.GetName().Version;
        foreach (var plugin in plugins)
        {
            Version? contractVersion;
            try
            {
                contractVersion = plugin.GetReferencedAssemblies()
                    .FirstOrDefault(x => string.Equals(x.Name, ContractAssemblyName, StringComparison.OrdinalIgnoreCase))
                    ?.Version;
            }
            catch (Exception ex)
            {
                // Can't read the plugin's metadata - don't let it abort the run; treat as compatible.
                logger?.Warning("Could not read references of plugin '{0}': {1}",
                    plugin.GetName().Name ?? "?", ex.Message);
                continue;
            }
            if (!PluginCompatibility.IsBuiltAgainstNewerContract(hostVersion, contractVersion))
                continue;

            incompatible.Add(plugin);
            logger?.Warning(
                "Plugin '{0}' was built against {1} {2}, newer than this build's {3}; its protections are skipped. " +
                "Update BitMono or rebuild the plugin against {3}.",
                plugin.GetName().Name ?? "?", ContractAssemblyName,
                contractVersion!.ToString(2), hostVersion!.ToString(2));
        }
        return incompatible;
    }
}
