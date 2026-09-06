namespace BitMono.Obfuscation.Starter;

public class BitMonoStarter
{
    private readonly IBitMonoServiceProvider _serviceProvider;
    private readonly ObfuscationSettings _obfuscationSettings;
    private readonly IEngineContextAccessor _engineContextAccessor;
    private readonly ILogger _logger;

    public BitMonoStarter(IBitMonoServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _obfuscationSettings = serviceProvider.GetRequiredService<ObfuscationSettings>();
        _engineContextAccessor = serviceProvider.GetRequiredService<IEngineContextAccessor>();
        _logger = serviceProvider
            .GetRequiredService<ILogger>()
            .ForContext<BitMonoStarter>();
    }

    private async Task<bool> StartAsync(StarterContext context, IDataWriter dataWriter)
    {
        context.ThrowIfCancellationRequested();

        var obfuscator = new BitMonoObfuscator(_serviceProvider, context, dataWriter, _obfuscationSettings, _logger);
        await obfuscator.ProtectAsync();
        return true;
    }
    public Task<bool> StartAsync(FinalFileInfo info, IModuleFactory moduleFactory, IDataWriter dataWriter,
        IReferencesDataResolver referencesDataResolver,
        CancellationToken cancellationToken)
    {
        var runtimeModule = LoadRuntimeModule();
        var moduleFactoryResult = moduleFactory.Create();
        var bitMonoContextFactory = new BitMonoContextFactory(moduleFactoryResult.Module, referencesDataResolver, _obfuscationSettings);
        var bitMonoContext = bitMonoContextFactory.Create(info.FilePath, info.OutputDirectoryPath, cancellationToken);
        var engineContextFactory = new StarterContextFactory(moduleFactoryResult, runtimeModule, bitMonoContext, cancellationToken);
        var engineContext = engineContextFactory.Create();
        _engineContextAccessor.Instance = engineContext;
        bitMonoContext.OutputFile = OutputFilePathFactory.Create(bitMonoContext);
        return StartAsync(engineContext, dataWriter);
    }
    public Task<bool> StartAsync(CompleteFileInfo info, CancellationToken cancellationToken)
    {
        return StartAsync(new FinalFileInfo(info.FileName, info.OutputDirectoryPath),
            new ModuleFactory(info.FileData, _obfuscationSettings, new LogErrorListener(_logger, _obfuscationSettings), _logger),
            new FileDataWriter(), new AutomaticReferencesDataResolver(info.FileReferences), cancellationToken);
    }
    public Task<bool> StartAsync(IncompleteFileInfo info, CancellationToken cancellationToken)
    {
        return StartAsync(new FinalFileInfo(info.FilePath, info.OutputDirectoryPath),
            new ModuleFactory(File.ReadAllBytes(info.FilePath), _obfuscationSettings, new LogErrorListener(_logger, _obfuscationSettings), _logger),
            new FileDataWriter(), new AutomaticPathReferencesDataResolver(info.ReferencesDirectoryPaths), cancellationToken);
    }

    // Protections clone their helper types (the string decryptor, the hook stub, ...) out of the Runtime
    // assembly, so we read it as a data module. Normally it's a DLL on disk; in the single-file net462 CLI
    // (Costura, e.g. the one bundled in the Unity package) it's embedded and has no file path, so read the
    // image straight from the copy already mapped in memory instead (#302).
    private static ModuleDefinition LoadRuntimeModule()
    {
        var runtimeAssembly = typeof(Runtime.Data).Assembly;
        var location = runtimeAssembly.Location;
        if (!string.IsNullOrEmpty(location) && File.Exists(location))
        {
            return ModuleDefinition.FromFile(location);
        }
        return ModuleDefinition.FromModule(runtimeAssembly.ManifestModule);
    }
}
