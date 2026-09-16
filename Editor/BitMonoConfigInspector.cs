#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.TextCore.Text;

namespace BitMono.Unity.Editor
{
    [CustomEditor(typeof(BitMonoConfig))]
    public class BitMonoConfigInspector : UnityEditor.Editor
    {
        private void OnEnable()
        {
            var config = (BitMonoConfig)target;
            if (config.UseUnityUIForProtections && (config.ProtectionSettings == null || config.ProtectionSettings.Count == 0))
            {
                config.LoadProtectionsFromFile();
                EditorUtility.SetDirty(config);
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement
            {
                name = "bitmono-config-root"
            };

            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/BitMono.Unity/Editor/BitMonoConfigInspector.uss");
            if (stylesheet != null)
            {
                root.styleSheets.Add(stylesheet);
            }

            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(
                "Assets/BitMono.Unity/Font/Aller_Std_Rg SDF.asset");
            if (fontAsset != null)
            {
                root.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }

            var headerSection = new VisualElement { name = "bm-section-header" };

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.Center;

            var headerLabel = new Label("BitMono Obfuscation") { name = "bm-header-label" };
            if (fontAsset != null)
            {
                headerLabel.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            headerRow.Add(headerLabel);

            var logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/BitMono.Unity/Logo/BitMonoLogo.png");
            if (logoTexture != null)
            {
                var logoImage = new Image
                {
                    name = "bm-header-logo",
                    image = logoTexture
                };
                headerRow.Add(logoImage);
            }

            headerSection.Add(headerRow);

            var enableObfToggle = new Toggle
            {
                label = "Enable Obfuscation",
                tooltip =
                    "Enable BitMono obfuscation during Unity builds. You can also control this programmatically using EditorPrefs.SetBool(\"BitMono.Obfuscation\", true/false). When disabled, obfuscation will be skipped entirely.",
                name = "bm-toggle-enable-obfuscation"
            };
            enableObfToggle.BindProperty(serializedObject.FindProperty("EnableObfuscation"));
            headerSection.Add(enableObfToggle);

            var obfuscationStateLabel = new Label
            {
                name = "bm-label-obfuscation-state"
            };
            if (fontAsset != null)
            {
                obfuscationStateLabel.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            headerSection.Add(obfuscationStateLabel);
            root.Add(headerSection);

            var pathSection = new VisualElement { name = "bm-section-config-path" };
            var pathTitle = new Label("Configuration Path") { name = "bm-section-title" };
            if (fontAsset != null)
            {
                pathTitle.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            pathSection.Add(pathTitle);

            var configPathField = new TextField("Config Path")
            {
                name = "bm-field-config-path",
                tooltip = "Leave empty for auto-detection. Set a custom path to override auto-detection."
            };
            configPathField.BindProperty(serializedObject.FindProperty("ConfigPath"));
            pathSection.Add(configPathField);

            var effectivePathField = new TextField("Effective Path")
            {
                name = "bm-field-effective-path",
                isReadOnly = true
            };
            effectivePathField.SetEnabled(false);
            pathSection.Add(effectivePathField);

            var warningBox = new HelpBox(
                "Auto detection failed. Please set a custom config path or ensure BitMono.CLI is accessible.",
                HelpBoxMessageType.Warning)
            {
                name = "bm-help-autodetect-warning"
            };
            if (fontAsset != null)
            {
                warningBox.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            pathSection.Add(warningBox);
            root.Add(pathSection);

            root.schedule.Execute(() =>
            {
                var config = (BitMonoConfig)target;

                var effectivePath = string.IsNullOrEmpty(config.ConfigPath) ? config.DetectedConfigPath : config.ConfigPath;
                var isAutoDetected = string.IsNullOrEmpty(config.ConfigPath);
                effectivePathField.label = isAutoDetected
                    ? "Effective Path (Auto-detected)"
                    : "Effective Path (Custom)";
                effectivePathField.value = effectivePath;

                warningBox.style.display =
                    isAutoDetected && effectivePath.Contains("Not found")
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;

                if (config.EnableObfuscation)
                {
                    obfuscationStateLabel.text = "Obfuscation is enabled. Build output will be protected by BitMono.";
                    obfuscationStateLabel.style.color = new StyleColor(new Color(0.55f, 0.92f, 0.6f));
                }
                else
                {
                    obfuscationStateLabel.text = "Obfuscation is disabled. Builds will not be obfuscated until you enable this option.";
                    obfuscationStateLabel.style.color = new StyleColor(new Color(0.85f, 0.6f, 0.4f));
                }
            }).Every(250);

            var obfSection = new VisualElement { name = "bm-section-obfuscation" };
            var obfTitle = new Label("Obfuscation Settings") { name = "bm-section-title" };
            if (fontAsset != null)
            {
                obfTitle.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            obfSection.Add(obfTitle);

            var timeoutField = new IntegerField("Timeout (minutes)")
            {
                tooltip =
                    "How long to wait for obfuscation to complete before timing out. Increase this if you have large assemblies or complex protections that take longer to process. Decrease this if obfuscation gets stuck on problematic protections or so. Default: 5 minutes",
                name = "bm-field-timeout"
            };
            timeoutField.BindProperty(serializedObject.FindProperty("ObfuscationTimeoutMinutes"));
            obfSection.Add(timeoutField);

            var debugToggle = new Toggle("Enable Debug Logging")
            {
                tooltip =
                    "Show detailed BitMono output in the Unity Console during builds. Useful for troubleshooting obfuscation issues, seeing which protections are running, and monitoring the obfuscation process. Only shows when this is enabled.",
                name = "bm-toggle-debug-logging"
            };
            debugToggle.BindProperty(serializedObject.FindProperty("EnableDebugLogging"));
            obfSection.Add(debugToggle);
            root.Add(obfSection);

            var protectionSection = new VisualElement { name = "bm-section-protections" };
            var protectionTitle = new Label("Protection Settings") { name = "bm-section-title" };
            protectionTitle.AddToClassList("bm-section-title-protections");
            if (fontAsset != null)
            {
                protectionTitle.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            protectionSection.Add(protectionTitle);

            var useUiToggle = new Toggle("Use Unity UI for Protections")
            {
                name = "bm-toggle-use-ui-protections"
            };
            var useUiProperty = serializedObject.FindProperty("UseUnityUIForProtections");
            useUiToggle.BindProperty(useUiProperty);
            protectionSection.Add(useUiToggle);

            var protectionsWarning = new VisualElement { name = "bm-help-no-protections" };

            var protectionsWarningIcon = new Label("!") { name = "bm-help-no-protections-icon" };
            var protectionsWarningText = new Label(
                "No protections are enabled! Obfuscation will fail. Please enable at least one protection below.")
            {
                name = "bm-help-no-protections-text"
            };

            if (fontAsset != null)
            {
                protectionsWarningText.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
                protectionsWarningIcon.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }

            protectionsWarning.Add(protectionsWarningIcon);
            protectionsWarning.Add(protectionsWarningText);
            protectionSection.Add(protectionsWarning);

            var il2cppNote = new Label(
                "Note: IL2CPP is not supported by BitMono yet. These protections are known to fail under IL2CPP and should be used only with the Mono backend: ObjectReturnType, NoNamespaces, FullRenamer, AntiDebugBreakpoints, BillionNops, StringsEncryption, UnmanagedString, DotNetHook, CallToCalli, BitMethodDotnet.")
            {
                name = "bm-help-il2cpp-note"
            };
            if (fontAsset != null)
            {
                il2cppNote.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            protectionSection.Add(il2cppNote);

            var protectionsGrid = new VisualElement
            {
                name = "bm-protections-grid"
            };
            protectionSection.Add(protectionsGrid);
            BuildProtectionsGrid(protectionsGrid, (BitMonoConfig)target, fontAsset);

            var buttonsRow = new VisualElement { name = "bm-row-protection-buttons" };
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.justifyContent = Justify.FlexStart;

            var loadButton = new Button
            {
                text = "Load from File",
                name = "bm-button-load-protections"
            };
            loadButton.clicked += () =>
            {
                var config = (BitMonoConfig)target;
                config.LoadProtectionsFromFile();
                EditorUtility.SetDirty(config);
                serializedObject.Update();
                BuildProtectionsGrid(protectionsGrid, config, fontAsset);
            };

            var saveButton = new Button
            {
                text = "Save to File",
                name = "bm-button-save-protections"
            };
            saveButton.clicked += () =>
            {
                var config = (BitMonoConfig)target;
                config.SaveProtectionsToFile();
            };

            buttonsRow.Add(loadButton);
            buttonsRow.Add(saveButton);
            protectionSection.Add(buttonsRow);

            var uiDisabledBanner = new VisualElement { name = "bm-ui-disabled-banner" };

            var uiDisabledIcon = new Label("!") { name = "bm-ui-disabled-icon" };
            var uiDisabledText = new Label(
                "Unity UI is disabled. BitMono will use protections.json file directly. Edit the protections.json file in your text editor to configure protections.")
            {
                name = "bm-ui-disabled-text"
            };

            if (fontAsset != null)
            {
                uiDisabledText.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
                uiDisabledIcon.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }

            uiDisabledBanner.Add(uiDisabledIcon);
            uiDisabledBanner.Add(uiDisabledText);
            protectionSection.Add(uiDisabledBanner);
            root.Add(protectionSection);

            var footerBanner = new VisualElement { name = "bm-info-banner" };

            var footerIcon = new Label("!") { name = "bm-info-banner-icon" };
            var footerText = new Label(
                "BitMono will automatically detect configuration files from BitMono.CLI location. You can set a custom path if needed, or leave it empty for auto-detection. Protections can be configured either through the Unity UI (recommended - no file writing needed) or by directly editing the protections.json file.")
            {
                name = "bm-info-banner-text"
            };

            if (fontAsset != null)
            {
                footerText.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
                footerIcon.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }

            footerBanner.Add(footerIcon);
            footerBanner.Add(footerText);
            root.Add(footerBanner);

            root.schedule.Execute(() =>
            {
                var config = (BitMonoConfig)target;

                if (config.EnableObfuscation)
                {
                    pathSection.style.display = DisplayStyle.Flex;
                    obfSection.style.display = DisplayStyle.Flex;
                    protectionSection.style.display = DisplayStyle.Flex;
                    footerBanner.style.display = DisplayStyle.Flex;

                    if (config.UseUnityUIForProtections)
                    {
                        var enabledCount = config.ProtectionSettings?.Count(p => p.Enabled) ?? 0;
                        protectionsWarning.style.display = enabledCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                    else
                    {
                        protectionsWarning.style.display = DisplayStyle.None;
                    }

                    protectionsGrid.style.display = config.UseUnityUIForProtections
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                    buttonsRow.style.display = config.UseUnityUIForProtections
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                    uiDisabledBanner.style.display = config.UseUnityUIForProtections
                        ? DisplayStyle.None
                        : DisplayStyle.Flex;
                }
                else
                {
                    pathSection.style.display = DisplayStyle.None;
                    obfSection.style.display = DisplayStyle.None;
                    protectionSection.style.display = DisplayStyle.None;
                    footerBanner.style.display = DisplayStyle.None;

                    protectionsWarning.style.display = DisplayStyle.None;
                    protectionsGrid.style.display = DisplayStyle.None;
                    buttonsRow.style.display = DisplayStyle.None;
                    uiDisabledBanner.style.display = DisplayStyle.None;
                }
            }).Every(250);

            return root;
        }

        private void BuildProtectionsGrid(VisualElement grid, BitMonoConfig config, FontAsset fontAsset)
        {
            grid.Clear();

            if (config.ProtectionSettings == null || config.ProtectionSettings.Count == 0)
            {
                var emptyLabel = new Label("No protections loaded. Use 'Load from File' to import protections.json.")
                {
                    name = "bm-label-no-protections"
                };
                if (fontAsset != null)
                {
                    emptyLabel.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
                }
                grid.Add(emptyLabel);
                return;
            }

            foreach (var protection in config.ProtectionSettings)
            {
                var card = new VisualElement();
                card.name = "bm-protection-card";
                card.AddToClassList("bm-protection-card");

                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.justifyContent = Justify.SpaceBetween;

                var nameLabel = new Label(protection.Name)
                {
                    name = "bm-protection-name"
                };
                if (fontAsset != null)
                {
                    nameLabel.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
                }

                headerRow.Add(nameLabel);
                card.Add(headerRow);

                var description = new Label(GetProtectionDescription(protection.Name))
                {
                    name = "bm-protection-description"
                };
                if (fontAsset != null)
                {
                    description.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
                }
                description.AddToClassList("bm-protection-description");
                card.Add(description);

                if (protection.Name == "ObjectReturnType" ||
                    protection.Name == "NoNamespaces" ||
                    protection.Name == "FullRenamer" ||
                    protection.Name == "AntiDebugBreakpoints" ||
                    protection.Name == "BillionNops" ||
                    protection.Name == "StringsEncryption" ||
                    protection.Name == "UnmanagedString" ||
                    protection.Name == "DotNetHook" ||
                    protection.Name == "CallToCalli" ||
                    protection.Name == "BitMethodDotnet")
                {
                    var il2cppWarning = new Label(
                        "IL2CPP is not supported for this protection; use it only with the Mono backend.")
                    {
                        name = "bm-protection-il2cpp-note"
                    };
                    if (fontAsset != null)
                    {
                        il2cppWarning.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
                    }
                    il2cppWarning.AddToClassList("bm-protection-il2cpp-note-inline");
                    card.Add(il2cppWarning);
                }

                UpdateProtectionCardVisual(card, protection.Enabled);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    protection.Enabled = !protection.Enabled;
                    EditorUtility.SetDirty(config);
                    UpdateProtectionCardVisual(card, protection.Enabled);
                });

                grid.Add(card);
            }
        }

        private void UpdateProtectionCardVisual(VisualElement card, bool enabled)
        {
            card.RemoveFromClassList("bm-protection-card-enabled");
            card.RemoveFromClassList("bm-protection-card-disabled");
            card.AddToClassList(enabled ? "bm-protection-card-enabled" : "bm-protection-card-disabled");
        }

        private string GetProtectionDescription(string name)
        {
            switch (name)
            {
                case "AntiILdasm":
                    return "Prevents simple inspection with ILDASM by adding ILDASM-disabling attributes. Usually safe, but always test protected builds.";
                case "AntiDe4dot":
                    return "Applies patterns intended to break popular .NET deobfuscator de4dot. Can confuse tools, so verify your pipeline after enabling.";
                case "ObjectReturnType":
                    return "Changes method signatures to use object return types to make analysis harder. May affect reflection or dynamic code; use with care.";
                case "NoNamespaces":
                    return "Removes namespaces so types are placed in the global namespace. Can impact debugging and tooling; recommended only for release builds.";
                case "FullRenamer":
                    return "Renames types and members to unreadable identifiers. Strong protection but may break code relying on reflection or hard-coded names.";
                case "AntiDebugBreakpoints":
                    return "Introduces constructs that interfere with debugger breakpoints. May make debugging harder even in development builds.";
                case "BillionNops":
                    return "Inserts many no-op instructions to clutter disassembly. Can increase file size and may impact performance; enable only if needed.";
                case "StringsEncryption":
                    return "Encrypts managed strings and decrypts them at runtime. Adds overhead; test for performance and compatibility issues.";
                case "UnmanagedString":
                    return "Moves string data to unmanaged memory to hinder extraction. Can be more fragile on some runtimes; test thoroughly.";
                case "DotNetHook":
                    return "Hooks selected .NET methods to alter or monitor behavior. Advanced feature that requires careful testing before production use.";
                case "CallToCalli":
                    return "Replaces direct calls with calli (indirect calls) to confuse decompilers. May affect some tools and profilers; enable with caution.";
                case "AntiDecompiler":
                    return "Applies patterns that make high-level decompilation unreliable. Some decompilers or analysis tools may misbehave with this enabled.";
                case "BitMethodDotnet":
                    return "BitMono-specific method-level protection for .NET methods. Intended for release builds; always validate protected output.";
                case "BitDecompiler":
                    return "BitMono-specific decompiler-resistance transforms. May be unstable with some toolchains; test on target environments.";
                case "BitTimeDateStamp":
                    return "Modifies PE timestamp metadata to hinder file correlation. Generally safe but still recommended to verify signing/distribution flows.";
                case "BitDotNet":
                    return "BitMono-specific protection set focused on .NET runtimes. Combines multiple techniques, so enable gradually and test thoroughly.";
                case "BitMono":
                    return "General BitMono protection pass combining multiple techniques. Strong but can be aggressive; recommended only after full QA.";
                default:
                    return "Protection description not available.";
            }
        }
    }
}
#endif