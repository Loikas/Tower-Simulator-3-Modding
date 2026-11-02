using TS3Modding.Diagnostics;
using UnityEngine;

namespace TS3Modding
{
    public sealed class FrameworkDriver : MonoBehaviour
    {
        private FrameworkRuntime runtime;
        private DiagnosticFiles files;
        private bool failed;
        internal void Initialize(FrameworkRuntime runtime, DiagnosticFiles files)
        {
            this.runtime = runtime; this.files = files;
            files.Append("Driver initialized; active=" + gameObject.activeInHierarchy);
        }
        private void Update()
        {
            if (failed) return;
            try { runtime?.Tick(); }
            catch (System.Exception error) { failed = true; files?.Append("Driver failed: " + error); runtime?.Dispose(); }
        }
        private void OnDisable() { files?.Append("Driver disabled."); }
        private void OnApplicationQuit() { files?.Append("Application quitting."); runtime?.Dispose(); }
        private void OnDestroy() { files?.Append("Driver destroyed."); runtime?.Dispose(); }
    }
}
