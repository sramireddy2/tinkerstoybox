using System;
using System.Globalization;
using System.Linq;
using UnityEditor;

namespace Toybox.EditorTools
{
    /// <summary>
    /// Arguments for editor scripts started from tools/unity.ps1. They arrive on the command line for a
    /// cold batch run, or through the batch server's session state when an editor is already running.
    /// </summary>
    public static class ToyboxArgs
    {
        const string ServerArgsKey = "Toybox.Server.Args";

        static string[] All()
        {
            string[] commandLine = Environment.GetCommandLineArgs();
            string served = SessionState.GetString(ServerArgsKey, "");
            return string.IsNullOrEmpty(served) ? commandLine : served.Split('\n').Concat(commandLine).ToArray();
        }

        public static bool Has(string name) => Array.IndexOf(All(), name) >= 0;

        public static string Get(string name, string fallback = null)
        {
            string[] args = All();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        public static int GetInt(string name, int fallback)
        {
            string value = Get(name);
            return value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        public static float GetFloat(string name, float fallback)
        {
            string value = Get(name);
            return value != null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
        }
    }
}
