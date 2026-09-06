#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BitMono.Unity.Editor
{
    [CreateAssetMenu(fileName = "BitMonoConfig", menuName = "BitMono/Create BitMono Configuration")]
    public class BitMonoConfig : ScriptableObject
    {
        [Header("BitMono Obfuscation")]
        [Tooltip("Enable BitMono obfuscation during Unity builds")]
        public bool EnableObfuscation = true;

        [Header("Configuration Path")]
        [Tooltip("Custom path to BitMono configuration files (leave empty for auto-detection)")]
        public string ConfigPath = "";

        public string DetectedConfigPath => GetDetectedConfigPath();

        [Header("Protection Settings")]
        [Tooltip("Use Unity UI to edit protections (false = use protections.json file directly)")]
        public bool UseUnityUIForProtections = true;

        [Tooltip("Protection settings loaded from protections.json")]
        public List<ProtectionSetting> ProtectionSettings = new List<ProtectionSetting>();

        [Header("Obfuscation Settings")]
        [Tooltip("Timeout for obfuscation process in minutes (default: 5 minutes)")]
        public int ObfuscationTimeoutMinutes = 5;

        [Tooltip("Enable debug logging for detailed BitMono output (useful for troubleshooting)")]
        public bool EnableDebugLogging = false;

        public string GetDetectedConfigPath()
        {
            if (!string.IsNullOrEmpty(ConfigPath) && Directory.Exists(ConfigPath))
            {
                return ConfigPath;
            }

            var bitMonoCli = FindBitMonoCli();
            if (!string.IsNullOrEmpty(bitMonoCli))
            {
                var cliDir = Path.GetDirectoryName(bitMonoCli);
                if (File.Exists(Path.Combine(cliDir, "obfuscation.json")))
                {
                    return cliDir;
                }
            }

            return "Not found - BitMono.CLI or config files missing";
        }

        private string FindBitMonoCli()
        {
            var paths = new[]
            {
                // Inside package under Assets
                Path.Combine(Application.dataPath, "BitMono.Unity", "BitMono.CLI", "BitMono.CLI.exe"),
                Path.Combine(Application.dataPath, "BitMono.CLI", "BitMono.CLI.exe"),
                // Project root sibling to Assets (local dev / CI setups)
                Path.Combine(Application.dataPath, "..", "BitMono.CLI", "BitMono.CLI.exe"),
                Path.Combine(Application.dataPath, "..", "..", "src", "BitMono.CLI", "bin", "Release", "net462", "BitMono.CLI.exe"),
                "BitMono.CLI.exe"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        public void LoadProtectionsFromFile()
        {
            var configPath = GetDetectedConfigPath();
            if (string.IsNullOrEmpty(configPath) || configPath.Contains("Not found"))
            {
                ProtectionSettings.Clear();
                return;
            }

            var protectionsFile = Path.Combine(configPath, "protections.json");
            if (!File.Exists(protectionsFile))
            {
                ProtectionSettings.Clear();
                return;
            }

            try
            {
                var json = File.ReadAllText(protectionsFile);
                var protectionsData = JsonUtility.FromJson<ProtectionsData>(json);

                ProtectionSettings.Clear();
                if (protectionsData?.Protections != null && protectionsData.Protections.Length > 0)
                {
                    ProtectionSettings.AddRange(protectionsData.Protections);
                }

                UnityEditor.EditorUtility.SetDirty(this);
            }
            catch (Exception)
            {
                ProtectionSettings.Clear();
            }
        }

        public void SaveProtectionsToFile()
        {
            var configPath = GetDetectedConfigPath();
            if (string.IsNullOrEmpty(configPath) || configPath.Contains("Not found"))
                return;

            var protectionsFile = Path.Combine(configPath, "protections.json");

            try
            {
                var protectionsData = new ProtectionsData { Protections = ProtectionSettings.ToArray() };
                var json = JsonUtility.ToJson(protectionsData, true);
                File.WriteAllText(protectionsFile, json);
            }
            catch (Exception)
            {
            }
        }
    }
}
#endif