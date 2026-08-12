using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using BitMono.Core.Services;
using BitMono.Host;
using BitMono.Host.Extensions;
using BitMono.Host.Modules;
using BitMono.Obfuscation.Files;
using BitMono.Obfuscation.Starter;
using BitMono.Shared.Models;

namespace BitMono.Obfuscation.Tests.Protections;

internal sealed class LocalVariableEncodingEndToEndFixture : IDisposable
{
    internal const string FixtureName = "BitMono.Obfuscation.TestCases.LocalVariableEncoding";

    private const string ExpectedOutput =
        "0,1|-128,127|0,255|-32768,32767|0,65535|-2147483648,2147483647|0,4294967295|" +
        "-9223372036854775808,9223372036854775807|0,18446744073709551615|0,65535|5|58|" +
        "10000000022|18,14|63|19,38|98|42|15|25.0|5";

    private readonly string _temporaryDirectory;

    internal LocalVariableEncodingEndToEndFixture(string temporaryDirectoryName)
    {
        string fixtureBinaryDirectory = FindFixtureBinaryDirectory();
        string fixtureAssemblyPath = Path.Combine(fixtureBinaryDirectory, FixtureName + ".dll");
        File.Exists(fixtureAssemblyPath).ShouldBeTrue($"fixture must be built at {fixtureAssemblyPath}");

        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            temporaryDirectoryName,
            Guid.NewGuid().ToString("N"));
        InputDirectory = Path.Combine(_temporaryDirectory, "in");
        OutputDirectory = Path.Combine(_temporaryDirectory, "out");
        Directory.CreateDirectory(OutputDirectory);
        CopyDirectory(fixtureBinaryDirectory, InputDirectory);
        WorkingAssemblyPath = Path.Combine(InputDirectory, FixtureName + ".dll");
        InjectInitializationMethods(WorkingAssemblyPath);
    }

    internal string InputDirectory { get; }

    internal string OutputDirectory { get; }

    internal string WorkingAssemblyPath { get; }

    internal async Task AssertExecutionAsync(string assemblyPath, string artifactName)
    {
        var result = await RunAssemblyAsync(assemblyPath);
        AssertExactExecution(result, artifactName);
        AssertInjectedMethodsExecute(assemblyPath, 0, 17, 23, 32, artifactName);
    }

    internal async Task<bool> ObfuscateAsync(RandomNext? randomNext = null)
    {
        var obfuscationSettings = new ObfuscationSettings
        {
            ReflectionMembersObfuscationExclude = true,
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
            OutputDirectoryName = OutputDirectory,
            RandomStrings = new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta" }
        };
        var protectionSettings = new ProtectionSettings
        {
            Protections = new List<ProtectionSetting>
            {
                new() { Name = "LocalVariableEncoding", Enabled = true }
            }
        };

        var module = new BitMonoModule(
            configureContainer: container =>
            {
                if (randomNext != null)
                    container.Register<RandomNext>(randomNext).AsSingleton();

                container.AddProtections();
            },
            obfuscationSettings: obfuscationSettings,
            protectionSettings: protectionSettings,
            criticalsFile: Path.Combine(AppContext.BaseDirectory, "criticals.json"));

        var serviceProvider = await new BitMonoApplication().RegisterModule(module).BuildAsync(CancellationToken.None);
        var starter = new BitMonoStarter(serviceProvider);
        var fileInformation = new IncompleteFileInfo(
            WorkingAssemblyPath,
            new[] { InputDirectory },
            OutputDirectory);
        return await starter.StartAsync(fileInformation, CancellationToken.None);
    }

    internal string PrepareProtectedAssembly()
    {
        string protectedAssemblyPath = FindObfuscatedOutput(OutputDirectory, WorkingAssemblyPath);
        StageRunPrerequisites(InputDirectory, Path.GetDirectoryName(protectedAssemblyPath)!);
        return protectedAssemblyPath;
    }

    public void Dispose()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private static void InjectInitializationMethods(string assemblyPath)
    {
        var module = ModuleDefinition.FromFile(assemblyPath);
        var programType = module.GetAllTypes().Single(type => type.FullName == FixtureName + ".Program");
        LocalVariableEncodingAddressTakenFixture.Inject(module, programType);

        var defaultInitializedMethod = CreateInjectedMethod(module, "DefaultInitializedLocal", true);
        var defaultInitializedVariable = defaultInitializedMethod.CilMethodBody!.LocalVariables[0];
        defaultInitializedMethod.CilMethodBody.Instructions.Add(new CilInstruction(
            CilOpCodes.Ldloc,
            defaultInitializedVariable));
        defaultInitializedMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
        defaultInitializedMethod.CilMethodBody.ComputeMaxStack();
        programType.Methods.Add(defaultInitializedMethod);

        var uninitializedMethod = CreateInjectedMethod(module, "UninitializedLocal", false);
        var uninitializedVariable = uninitializedMethod.CilMethodBody!.LocalVariables[0];
        uninitializedMethod.CilMethodBody.Instructions.Add(CilInstruction.CreateLdcI4(17));
        uninitializedMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Stloc, uninitializedVariable));
        uninitializedMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Ldloc, uninitializedVariable));
        uninitializedMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
        uninitializedMethod.CilMethodBody.ComputeMaxStack();
        programType.Methods.Add(uninitializedMethod);

        var branchTargetMethod = CreateInjectedMethod(module, "BranchTargetStore", true);
        var branchTargetVariable = branchTargetMethod.CilMethodBody!.LocalVariables[0];
        var targetedStore = new CilInstruction(CilOpCodes.Stloc, branchTargetVariable);
        var targetedStoreLabel = targetedStore.CreateLabel();
        branchTargetMethod.CilMethodBody.Instructions.Add(CilInstruction.CreateLdcI4(23));
        branchTargetMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Br, targetedStoreLabel));
        branchTargetMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Nop));
        branchTargetMethod.CilMethodBody.Instructions.Add(targetedStore);
        branchTargetMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Ldloc, branchTargetVariable));
        branchTargetMethod.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
        branchTargetMethod.CilMethodBody.ComputeMaxStack();
        programType.Methods.Add(branchTargetMethod);

        var exceptionBoundaryMethod = CreateInjectedMethod(module, "ExceptionBoundaryLocal", true);
        var exceptionBoundaryBody = exceptionBoundaryMethod.CilMethodBody!;
        var exceptionBoundaryVariable = exceptionBoundaryBody.LocalVariables[0];
        var tryStart = new CilInstruction(CilOpCodes.Ldloc, exceptionBoundaryVariable);
        var handlerStart = new CilInstruction(CilOpCodes.Ldloc, exceptionBoundaryVariable);
        var returnLoad = new CilInstruction(CilOpCodes.Ldloc, exceptionBoundaryVariable);
        var tryStartLabel = tryStart.CreateLabel();
        var handlerStartLabel = handlerStart.CreateLabel();
        var returnLoadLabel = returnLoad.CreateLabel();
        exceptionBoundaryBody.Instructions.Add(CilInstruction.CreateLdcI4(31));
        exceptionBoundaryBody.Instructions.Add(new CilInstruction(CilOpCodes.Stloc, exceptionBoundaryVariable));
        exceptionBoundaryBody.Instructions.Add(tryStart);
        exceptionBoundaryBody.Instructions.Add(new CilInstruction(CilOpCodes.Pop));
        exceptionBoundaryBody.Instructions.Add(new CilInstruction(CilOpCodes.Leave, returnLoadLabel));
        exceptionBoundaryBody.Instructions.Add(handlerStart);
        exceptionBoundaryBody.Instructions.Add(CilInstruction.CreateLdcI4(1));
        exceptionBoundaryBody.Instructions.Add(new CilInstruction(CilOpCodes.Add));
        exceptionBoundaryBody.Instructions.Add(new CilInstruction(CilOpCodes.Stloc, exceptionBoundaryVariable));
        exceptionBoundaryBody.Instructions.Add(new CilInstruction(CilOpCodes.Endfinally));
        exceptionBoundaryBody.Instructions.Add(returnLoad);
        exceptionBoundaryBody.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
        exceptionBoundaryBody.ExceptionHandlers.Add(new CilExceptionHandler
        {
            HandlerType = CilExceptionHandlerType.Finally,
            TryStart = tryStartLabel,
            TryEnd = handlerStartLabel,
            HandlerStart = handlerStartLabel,
            HandlerEnd = returnLoadLabel
        });
        exceptionBoundaryBody.ComputeMaxStack();
        programType.Methods.Add(exceptionBoundaryMethod);

        programType.Methods.Add(CreateStoredUnsupportedLocalMethod(
            module,
            "BooleanLocal",
            module.CorLibTypeFactory.Boolean));

        var booleanBackedEnum = new TypeDefinition(
            FixtureName,
            "BooleanBackedEnum",
            TypeAttributes.NotPublic | TypeAttributes.Sealed,
            module.DefaultImporter.ImportType(typeof(Enum)));
        booleanBackedEnum.Fields.Add(new FieldDefinition(
            "value__",
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RuntimeSpecialName,
            new FieldSignature(module.CorLibTypeFactory.Boolean)));
        module.TopLevelTypes.Add(booleanBackedEnum);
        programType.Methods.Add(CreateStoredUnsupportedLocalMethod(
            module,
            "BooleanBackedEnumLocal",
            new TypeDefOrRefSignature(booleanBackedEnum, isValueType: true)));

        programType.Methods.Add(CreateUnsupportedLocalMethod(
            module,
            "UnsupportedIntPtrLocal",
            module.CorLibTypeFactory.IntPtr));
        programType.Methods.Add(CreateUnsupportedLocalMethod(
            module,
            "UnsupportedUIntPtrLocal",
            module.CorLibTypeFactory.UIntPtr));
        programType.Methods.Add(CreateUnsupportedLocalMethod(
            module,
            "UnsupportedPointerLocal",
            module.CorLibTypeFactory.Int32.MakePointerType()));
        programType.Methods.Add(CreateUnsupportedLocalMethod(
            module,
            "UnsupportedByReferenceLocal",
            module.CorLibTypeFactory.Int32.MakeByReferenceType()));
        programType.Methods.Add(CreateUnsupportedLocalMethod(
            module,
            "UnsupportedPinnedByReferenceLocal",
            module.CorLibTypeFactory.Int32.MakeByReferenceType().MakePinnedType()));

        var genericParameterSignature = new GenericParameterSignature(
            module,
            GenericParameterType.Method,
            0);
        var genericParameterMethod = CreateUnsupportedLocalMethod(
            module,
            "UnsupportedMethodGenericParameterLocal",
            genericParameterSignature);
        genericParameterMethod.Signature = MethodSignature.CreateStatic(
            module.CorLibTypeFactory.Void,
            1,
            Array.Empty<TypeSignature>());
        genericParameterMethod.GenericParameters.Add(new GenericParameter("T"));
        programType.Methods.Add(genericParameterMethod);

        var missingAssemblyReference = new AssemblyReference(
            "BitMono.LocalVariableEncoding.Missing",
            new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(missingAssemblyReference);
        var missingValueTypeReference = new TypeReference(
            module,
            missingAssemblyReference,
            "BitMono.LocalVariableEncoding.Missing",
            "MissingValueType");
        var unresolvedValueTypeMethod = CreateUnsupportedLocalMethod(
            module,
            "UnresolvedValueTypeLocal",
            new TypeDefOrRefSignature(missingValueTypeReference, isValueType: true));
        unresolvedValueTypeMethod.CilMethodBody!.Instructions.OptimizeMacros();
        programType.Methods.Add(unresolvedValueTypeMethod);

        string injectedAssemblyPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath)!,
            Path.GetFileNameWithoutExtension(assemblyPath) + ".injected.dll");
        module.Write(injectedAssemblyPath);
        File.Move(injectedAssemblyPath, assemblyPath, overwrite: true);
    }

    private static MethodDefinition CreateInjectedMethod(
        ModuleDefinition module,
        string methodName,
        bool initializeLocals)
    {
        var method = new MethodDefinition(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodSignature.CreateStatic(module.CorLibTypeFactory.Int32));
        var body = method.CilMethodBody = new CilMethodBody
        {
            InitializeLocals = initializeLocals
        };
        body.LocalVariables.Add(new CilLocalVariable(module.CorLibTypeFactory.Int32));
        return method;
    }

    private static MethodDefinition CreateUnsupportedLocalMethod(
        ModuleDefinition module,
        string methodName,
        TypeSignature variableType)
    {
        var method = new MethodDefinition(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodSignature.CreateStatic(module.CorLibTypeFactory.Void));
        var body = method.CilMethodBody = new CilMethodBody
        {
            InitializeLocals = true
        };
        var variable = new CilLocalVariable(variableType);
        body.LocalVariables.Add(variable);
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ldloc, variable));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Pop));
        body.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
        body.ComputeMaxStack();
        return method;
    }

    private static MethodDefinition CreateStoredUnsupportedLocalMethod(
        ModuleDefinition module,
        string methodName,
        TypeSignature variableType)
    {
        var method = CreateUnsupportedLocalMethod(module, methodName, variableType);
        var body = method.CilMethodBody!;
        var variable = body.LocalVariables[0];
        body.Instructions.InsertRange(0,
        [
            CilInstruction.CreateLdcI4(0),
            new CilInstruction(CilOpCodes.Stloc, variable)
        ]);
        body.Instructions.OptimizeMacros();
        body.ComputeMaxStack();
        return method;
    }

    private static void AssertInjectedMethodsExecute(
        string assemblyPath,
        int expectedDefaultInitializedValue,
        int expectedUninitializedValue,
        int expectedBranchTargetValue,
        int expectedExceptionBoundaryValue,
        string artifactName)
    {
        var invocationResult = LoadAndInvokeInjectedMethods(assemblyPath);
        invocationResult.DefaultInitializedValue.ShouldBe(expectedDefaultInitializedValue,
            $"{artifactName} default-initialized local result");
        invocationResult.UninitializedValue.ShouldBe(expectedUninitializedValue,
            $"{artifactName} uninitialized-method result");
        invocationResult.BranchTargetValue.ShouldBe(expectedBranchTargetValue,
            $"{artifactName} branch-target store result");
        invocationResult.ExceptionBoundaryValue.ShouldBe(expectedExceptionBoundaryValue,
            $"{artifactName} exception-boundary local result");
        invocationResult.AddressTakenAndEligibleValue.ShouldBe(57, $"{artifactName} mixed local result");

        for (int collectionAttempt = 0;
             invocationResult.LoadContextReference.IsAlive && collectionAttempt < 10;
             collectionAttempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        invocationResult.LoadContextReference.IsAlive.ShouldBeFalse(
            $"{artifactName} collectible load context must unload");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        int DefaultInitializedValue,
        int UninitializedValue,
        int BranchTargetValue,
        int ExceptionBoundaryValue,
        int AddressTakenAndEligibleValue,
        WeakReference LoadContextReference) LoadAndInvokeInjectedMethods(string assemblyPath)
    {
        var loadContext = new AssemblyLoadContext(
            "LocalVariableEncodingEndToEnd-" + Guid.NewGuid().ToString("N"),
            isCollectible: true);
        var loadContextReference = new WeakReference(loadContext);

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var programType = assembly.GetType(FixtureName + ".Program", throwOnError: true)!;
            int defaultInitializedValue = InvokeInjectedMethod(programType, "DefaultInitializedLocal");
            int uninitializedValue = InvokeInjectedMethod(programType, "UninitializedLocal");
            int branchTargetValue = InvokeInjectedMethod(programType, "BranchTargetStore");
            int exceptionBoundaryValue = InvokeInjectedMethod(programType, "ExceptionBoundaryLocal");
            int addressTakenAndEligibleValue = InvokeInjectedMethod(
                programType,
                LocalVariableEncodingAddressTakenFixture.MethodName);
            return (defaultInitializedValue, uninitializedValue, branchTargetValue, exceptionBoundaryValue,
                addressTakenAndEligibleValue, loadContextReference);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static int InvokeInjectedMethod(Type programType, string methodName)
    {
        var method = programType.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.DeclaredOnly);
        method.ShouldNotBeNull($"injected method {methodName} must be present");
        return (int)method!.Invoke(null, null)!;
    }

    private static void AssertExactExecution(
        (int ExitCode, string StandardOutput, string StandardError) result,
        string artifactName)
    {
        result.ExitCode.ShouldBe(0,
            $"{artifactName} fixture exit code\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
        result.StandardOutput.ShouldBe(ExpectedOutput + Environment.NewLine,
            $"{artifactName} fixture output must match exactly");
        result.StandardError.ShouldBeEmpty($"{artifactName} fixture must not write to stderr");
    }

    private static string FindObfuscatedOutput(string searchRoot, string baselineAssemblyPath)
    {
        byte[] baselineBytes = File.ReadAllBytes(baselineAssemblyPath);
        string[] candidates = Directory.GetFiles(searchRoot, FixtureName + ".dll", SearchOption.AllDirectories)
            .Where(candidate => !File.ReadAllBytes(candidate).SequenceEqual(baselineBytes))
            .ToArray();
        candidates.Length.ShouldBe(1, "the engine must write exactly one changed fixture assembly");
        return candidates[0];
    }

    private static void StageRunPrerequisites(string sourceDirectory, string destinationDirectory)
    {
        foreach (string fileName in new[] { FixtureName + ".runtimeconfig.json", FixtureName + ".deps.json" })
        {
            string sourcePath = Path.Combine(sourceDirectory, fileName);
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, Path.Combine(destinationDirectory, fileName), overwrite: true);
        }
    }

    private static string FindFixtureBinaryDirectory()
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        DirectoryInfo? repositoryDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (repositoryDirectory != null &&
               !File.Exists(Path.Combine(repositoryDirectory.FullName, "BitMono.sln")))
            repositoryDirectory = repositoryDirectory.Parent;

        repositoryDirectory.ShouldNotBeNull("the repository root must be locatable from the test output");
        return Path.Combine(repositoryDirectory!.FullName, "test", "TestBinaries", "DotNet", FixtureName, "bin",
            configuration, "net10.0");
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAssemblyAsync(
        string assemblyPath)
    {
        var startInformation = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!
        };
        startInformation.ArgumentList.Add(assemblyPath);

        using var process = new Process { StartInfo = startInformation };
        process.Start().ShouldBeTrue();
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync();
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw new TimeoutException($"fixture process exceeded 60 seconds: {assemblyPath}");
        }

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourcePath in Directory.GetFiles(sourceDirectory))
        {
            string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }
}
