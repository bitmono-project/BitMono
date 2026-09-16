using System;
using UnityEngine;

namespace BitMono.Unity.Editor
{
    [Serializable]
    public class ProtectionSetting
    {
        public string Name;
        public bool Enabled;
    }

    [Serializable]
    public class ProtectionsData
    {
        public ProtectionSetting[] Protections;
    }
}
