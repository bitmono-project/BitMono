using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BitMono.Host;
using BitMono.Host.Extensions;
using BitMono.Host.Modules;
using BitMono.Obfuscation.Files;
using BitMono.Obfuscation.Starter;
using BitMono.Shared.Models;
using Shouldly;
using Xunit;

namespace BitMono.Obfuscation.Tests.Reflection;

// End-to-end proof that reflection survives obfuscation: obfuscate the reflection-heavy sample exe
// (renaming on, reflection-exclude on) and run it. Its Main exits non-zero if any reflection lookup
// returns null, so a green run means the analyzer kept the reflected members intact.
[Collection(global::BitMono.Obfuscation.Tests.ObfuscationEndToEndCollection.Name)]
public class ReflectionEndToEndTests
{
    private const string FixtureName = "BitMono.Obfuscation.TestCases.Reflection";

    [Fact]
    public async Task ObfuscatedReflectionHeavyExe_StillRuns()
    {
        await RunObfuscatedAsync(new[] { "FullRenamer" });
    }

    [Fact]
    public async Task ObfuscatedWithRenamingAndIlProtection_StillRuns()
    {
        // CallToCalli rewrites method-body IL; combined with renaming this exercises both the rename
        // exclusion and the GetILAsByteArray method-body exclusion on the same assembly.
        await RunObfuscatedAsync(new[] { "FullRenamer", "CallToCalli" });
    }

    [Fact]
    public async Task WithoutReflectionExclusion_ObfuscatedExe_Breaks()
    {
        // Negative control: with the reflection analysis off, renaming scrambles the reflected members
        // and the sample fails - proving the passing tests above actually depend on the analyzer.
        await RunObfuscatedAsync(new[] { "FullRenamer" }, reflectionExclude: false, expectRunSuccess: false);
    }

    private static async Task RunObfuscatedAsync(string[] protections, bool reflectionExclude = true,
        bool expectRunSuccess = true)
    {
        var fixtureBin = FindFixtureBinDirectory();
        var inputDll = Path.Combine(fixtureBin, FixtureName + ".dll");
        File.Exists(inputDll).ShouldBeTrue($"fixture must be built at {inputDll}");

        var temp = Path.Combine(Path.GetTempPath(), "bitmono-refl-e2e", Guid.NewGuid().ToString("N"));
        var inputDir = Path.Combine(temp, "in");
        var outputDir = Path.Combine(temp, "out");
        Directory.CreateDirectory(outputDir);
        CopyDirectory(fixtureBin, inputDir);

        try
        {
            var workInput = Path.Combine(inputDir, FixtureName + ".dll");
            var succeeded = await ObfuscateAsync(workInput, inputDir, outputDir, protections, reflectionExclude);
            succeeded.ShouldBeTrue();

            var obfuscated = FindObfuscatedOutput(outputDir, inputDll);
            obfuscated.ShouldNotBeNull("the engine must write an obfuscated assembly that differs from the input");

            // Bring the runtime config / deps next to the obfuscated dll so `dotnet` can launch it.
            StageRunPrerequisites(inputDir, Path.GetDirectoryName(obfuscated)!);

            var (exitCode, stdout, stderr) = Run("dotnet", obfuscated!);
            if (expectRunSuccess)
            {
                exitCode.ShouldBe(0, $"obfuscated reflection sample must run cleanly.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
                stdout.ShouldContain("OK");
            }
            else
            {
                exitCode.ShouldNotBe(0, "without reflection exclusion the renamed members break the lookups");
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<bool> ObfuscateAsync(string inputDll, string referencesDir, string outputDir,
        string[] protections, bool reflectionExclude)
    {
        var obfuscationSettings = new ObfuscationSettings
        {
            ReflectionMembersObfuscationExclude = reflectionExclude,
            ForceObfuscation = true,
            Watermark = false,
            Tips = false,
            NotifyProtections = false,
            WpfBamlRewrite = false,
            StripObfuscationAttributes = true,
            OutputPEImageBuildErrors = false,
            FailOnNoRequiredDependency = false,
            NoInliningMethodObfuscationExclude = true,
            ObfuscationAttributeObfuscationExclude = true,
            ObfuscateAssemblyAttributeObfuscationExclude = true,
            Preset = "Custom",
            ReferencesDirectoryName = "libs",
            OutputDirectoryName = outputDir,
            RandomStrings = new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta" }
        };
        var protectionSettings = new ProtectionSettings
        {
            Protections = new List<ProtectionSetting>()
        };
        foreach (var name in protections)
        {
            protectionSettings.Protections.Add(new ProtectionSetting { Name = name, Enabled = true });
        }

        var module = new BitMonoModule(
            configureContainer: container => container.AddProtections(),
            obfuscationSettings: obfuscationSettings,
            protectionSettings: protectionSettings,
            criticalsFile: Path.Combine(AppContext.BaseDirectory, "criticals.json"));

        var serviceProvider = await new BitMonoApplication().RegisterModule(module).BuildAsync(CancellationToken.None);
        var starter = new BitMonoStarter(serviceProvider);
        var info = new IncompleteFileInfo(inputDll, new[] { referencesDir }, outputDir);
        return await starter.StartAsync(info, CancellationToken.None);
    }

    private static string? FindObfuscatedOutput(string searchRoot, string inputDll)
    {
        var inputBytes = File.ReadAllBytes(inputDll);
        foreach (var candidate in Directory.GetFiles(searchRoot, FixtureName + ".dll", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(candidate);
            if (!BytesEqual(bytes, inputBytes))
            {
                return candidate;
            }
        }
        return null;
    }

    private static void StageRunPrerequisites(string fromDir, string toDir)
    {
        foreach (var name in new[] { FixtureName + ".runtimeconfig.json", FixtureName + ".deps.json" })
        {
            var source = Path.Combine(fromDir, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(toDir, name), overwrite: true);
            }
        }
    }

    private static string FindFixtureBinDirectory()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name; // bin/<Config>/<tfm>
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "BitMono.sln")))
        {
            root = root.Parent;
        }
        root.ShouldNotBeNull("the repo root (BitMono.sln) must be locatable from the test output");
        return Path.Combine(root!.FullName, "test", "TestBinaries", "DotNet", FixtureName, "bin", configuration, "net10.0");
    }

    private static (int exitCode, string stdout, string stderr) Run(string fileName, string argument)
    {
        var startInfo = new ProcessStartInfo(fileName, $"\"{argument}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(argument)!
        };
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        return (process.ExitCode, stdout, stderr);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
