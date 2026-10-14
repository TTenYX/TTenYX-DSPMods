using BepInEx.Logging;
using UnityEngine;

namespace BlueprintBeltSaver
{
    /// <summary>Small logging facade so every source file can log without passing the plugin around.</summary>
    internal static class Log
    {
        private static ManualLogSource _source;

        internal static void Init(ManualLogSource source)
        {
            _source = source;
        }

        internal static void Info(string message)
        {
            if (_source != null)
            {
                _source.LogInfo(message);
            }
            else
            {
                Debug.Log("[BlueprintBeltSaver] " + message);
            }
        }

        internal static void Warn(string message)
        {
            if (_source != null)
            {
                _source.LogWarning(message);
            }
            else
            {
                Debug.LogWarning("[BlueprintBeltSaver] " + message);
            }
        }

        internal static void Error(string message)
        {
            if (_source != null)
            {
                _source.LogError(message);
            }
            else
            {
                Debug.LogError("[BlueprintBeltSaver] " + message);
            }
        }
    }
}
