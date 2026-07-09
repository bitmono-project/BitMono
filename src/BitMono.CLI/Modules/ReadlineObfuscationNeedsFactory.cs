namespace BitMono.CLI.Modules;

internal class ReadlineObfuscationNeedsFactory
{
    private readonly string[] _args;

    public ReadlineObfuscationNeedsFactory(string[] args)
    {
        _args = args;
    }

    public ObfuscationNeeds Create(CancellationToken cancellationToken)
    {
        ObfuscationSettings? obfuscationSettings = null;
        List<ProtectionSetting>? protectionSettings = null;
        string? criticalsFile = null;
        string? obfuscationFile = null;

        if (!File.Exists(KnownConfigNames.Criticals))
        {
            criticalsFile = AskForConfigFile("criticals", KnownConfigNames.Criticals, cancellationToken);
        }

        // logging is configured in code now (BitMonoModule), no logging.json is read anymore (#295)

        if (!File.Exists(KnownConfigNames.Obfuscation))
        {
            obfuscationFile = AskForConfigFile("obfuscation", KnownConfigNames.Obfuscation, cancellationToken);
        }

        try
        {
            if (File.Exists(obfuscationFile ?? KnownConfigNames.Obfuscation))
            {
                obfuscationSettings = Json.LoadFile<ObfuscationSettings>(obfuscationFile ?? KnownConfigNames.Obfuscation);
            }

            if (File.Exists(KnownConfigNames.Protections))
            {
                var loadedSettings = Json.LoadFile<ProtectionSettings>(KnownConfigNames.Protections);
                protectionSettings = loadedSettings?.Protections;
            }

            if (ProtectionPresets.Expand(obfuscationSettings?.Preset) is { } presetProtectionSettings)
            {
                protectionSettings = presetProtectionSettings.Protections;
                Console.WriteLine($"Using protection preset: {ProtectionPresets.Parse(obfuscationSettings?.Preset)} ({presetProtectionSettings.Protections.Count} protection(s)).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load configuration files: {ex}");
        }

        var fileName = GetFileName(_args);
        while (true)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine("Please, specify file or drag-and-drop in BitMono CLI");

                fileName = PathFormatterUtility.Format(Console.ReadLine());
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    if (File.Exists(fileName))
                    {
                        Console.WriteLine($"File successfully specified: {fileName}");
                        break;
                    }

                    Console.WriteLine("File cannot be found, please, try again!");
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Console.WriteLine("Unable to specify empty null or whitespace file, please, try again!");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Something went wrong while specifying the file: {ex}");
            }
        }

        List<string> protections = [];

        bool hasEnabledProtections = protectionSettings != null && protectionSettings.Any(x => x.Enabled);
        bool hasAnyProtectionSettings = protectionSettings != null && protectionSettings.Any();

        if (!hasEnabledProtections)
        {
            if (!hasAnyProtectionSettings)
            {
                Console.WriteLine("No protection settings found (protections.json may be missing or empty)");
                Console.WriteLine("Please input the preferred protections with ',' delimiter, example: StringsEncryption,AntiDe4dot,ControlFlow");
            }
            else
            {
                Console.WriteLine("No protection is enabled in protections.json file, please either enable any protection first or input the preferred with ',' delimiter, example: StringsEncryption,AntiDe4dot");
            }

            while (true)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var protectionInput = Console.ReadLine();
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!string.IsNullOrWhiteSpace(protectionInput))
                    {
                        var inputProtections = protectionInput.Split([','], StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .ToList();

                        if (inputProtections.Any())
                        {
                            // If we have protection settings, validate against them but allow unknown protections
                            if (hasAnyProtectionSettings)
                            {
                                var availableProtections = protectionSettings!.Select(p => p.Name).ToList();
                                var unknownProtections = inputProtections.Where(p => !availableProtections.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

                                if (unknownProtections.Any())
                                {
                                    Console.WriteLine($"The following protection(s) are not found in current protections.json but will be used anyway: {string.Join(", ", unknownProtections)}");
                                    Console.WriteLine($"Available protections in config: {(availableProtections.Any() ? string.Join(", ", availableProtections) : "none")}");
                                }

                                var knownProtections = inputProtections.Where(p => availableProtections.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                                if (knownProtections.Any())
                                {
                                    Console.WriteLine($"Recognized protection(s) from config: {string.Join(", ", knownProtections)}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Cannot validate protection names as protections.json is missing/empty. Using specified protections as-is.");
                            }

                            protections = inputProtections;
                            Console.WriteLine($"Protections successfully specified: {string.Join(", ", protections)}");
                            break;
                        }

                        Console.WriteLine("No valid protections found in input, please try again!");
                    }
                    else
                    {
                        Console.WriteLine("No protections specified, continuing without additional protections...");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Something went wrong while specifying protections: {ex}");
                }
            }
        }
        else
        {
            // Use enabled protections from settings
            protections = protectionSettings!.Where(x => x.Enabled).Select(x => x.Name).ToList();
            Console.WriteLine($"Using enabled protections from settings: {string.Join(", ", protections)}");
        }

        string dependenciesDirectoryName;
        string outputDirectoryName;
        var fileBaseDirectory = Path.GetDirectoryName(fileName);
        if (obfuscationSettings?.ForceObfuscation == true)
        {
            dependenciesDirectoryName = fileBaseDirectory;
            outputDirectoryName = fileBaseDirectory;
        }
        else
        {
            outputDirectoryName = Path.Combine(fileBaseDirectory, obfuscationSettings?.OutputDirectoryName ?? "output");
            dependenciesDirectoryName = Path.Combine(fileBaseDirectory, obfuscationSettings?.ReferencesDirectoryName ?? "libs");
            if (!Directory.Exists(dependenciesDirectoryName))
            {
                while (true)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (Directory.Exists(dependenciesDirectoryName))
                        {
                            Console.WriteLine($"Dependencies (libs) successfully found automatically: {dependenciesDirectoryName}!");
                            break;
                        }

                        Console.WriteLine("Please, specify dependencies (libs) path: ");
                        var newDependenciesDirectoryName = PathFormatterUtility.Format(Console.ReadLine());
                        if (!string.IsNullOrWhiteSpace(newDependenciesDirectoryName))
                        {
                            if (Directory.Exists(newDependenciesDirectoryName))
                            {
                                dependenciesDirectoryName = newDependenciesDirectoryName;
                                Console.WriteLine($"Dependencies (libs) successfully specified: {newDependenciesDirectoryName}!");
                                break;
                            }
                            else
                            {
                                Console.WriteLine("Libs directory doesn't exist, please, try again!");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Unable to specify empty (libs), please, try again!");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Something went wrong while specifying the dependencies (libs) path: {ex}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Dependencies (libs) directory was automatically found in: {dependenciesDirectoryName}");
            }
        }

        ProtectionSettings? finalProtectionSettings = null;
        if (protections.Any())
        {
            finalProtectionSettings = new ProtectionSettings
            {
                Protections = protections.Select(x => new ProtectionSetting
                {
                    Name = x,
                    Enabled = true
                }).ToList()
            };
        }

        Directory.CreateDirectory(outputDirectoryName);
        Directory.CreateDirectory(dependenciesDirectoryName);
        return new ObfuscationNeeds
        {
            FileName = fileName,
            FileBaseDirectory = fileBaseDirectory,
            ReferencesDirectoryName = dependenciesDirectoryName,
            ReferencesDirectoryNames = new List<string> { dependenciesDirectoryName },
            OutputPath = outputDirectoryName,
            Way = ObfuscationNeedsWay.Readline,
            Protections = protections,
            CriticalsFile = criticalsFile,
            ObfuscationFile = obfuscationFile,
        };
    }

    private string GetFileName(string[] args)
    {
        string? file = null;
        if (!args.IsEmpty())
        {
            file = PathFormatterUtility.Format(args[0]);
        }
        return file;
    }

    private string? AskForConfigFile(string configType, string defaultFileName, CancellationToken cancellationToken, bool required = true)
    {
        while (true)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"No {configType} configuration file found ({defaultFileName}).");

                if (required)
                {
                    Console.WriteLine($"The {configType} configuration file is required for BitMono to function properly.");
                    Console.WriteLine($"Please specify the path to your {configType} configuration file:");
                }
                else
                {
                    Console.WriteLine($"Would you like to specify a custom {configType} configuration file? (y/n, or press Enter to skip):");
                }

                var response = Console.ReadLine();
                cancellationToken.ThrowIfCancellationRequested();

                if (!required && (string.IsNullOrWhiteSpace(response) || response.Equals("n", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"Skipping {configType} configuration file.");
                    return null;
                }

                if (required || response.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    if (!required)
                    {
                        Console.WriteLine($"Please specify the path to your {configType} configuration file:");
                    }

                    var filePath = PathFormatterUtility.Format(Console.ReadLine());
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        if (File.Exists(filePath))
                        {
                            Console.WriteLine($"{configType} configuration file successfully specified: {filePath}");
                            return filePath;
                        }
                        else
                        {
                            Console.WriteLine($"File {filePath} does not exist. Please try again{(required ? "" : " or press Enter to skip")}.");
                        }
                    }
                    else
                    {
                        if (required)
                        {
                            Console.WriteLine($"No file path specified. The {configType} configuration file is required. Please try again.");
                        }
                        else
                        {
                            Console.WriteLine($"No file path specified. Skipping {configType} configuration file.");
                            return null;
                        }
                    }
                }
                else if (!required)
                {
                    Console.WriteLine("Please answer 'y' for yes, 'n' for no, or press Enter to skip.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Something went wrong while specifying the {configType} configuration file: {ex}");
            }
        }
    }
}