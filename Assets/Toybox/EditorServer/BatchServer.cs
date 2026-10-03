using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Toybox.EditorServer
{
    /// <summary>
    /// Keeps one headless editor alive and serves compile / test / exec requests dropped into
    /// tools/out/server as JSON files, so a test run costs a script reload instead of a full editor start.
    /// Active only when Unity is launched with -toyboxServer (see tools/unity.ps1).
    ///
    /// This lives in its own assembly with no dependency on game code: when game code fails to compile
    /// the old server keeps running and can report the errors.
    /// </summary>
    [InitializeOnLoad]
    public static class BatchServer
    {
        const string Dir = "tools/out/server";
        const string KeyPhase = "Toybox.Server.Phase";
        const string KeyRequest = "Toybox.Server.Request";
        const string KeyStable = "Toybox.Server.StableSince";

        /// <summary>Newline-separated arguments of the request being served; read by editor scripts.</summary>
        public const string KeyArgs = "Toybox.Server.Args";

        [Serializable]
        class Request
        {
            public string id;
            public string command;
            public string filter;
            public string method;
            public string[] args;
        }

        [Serializable]
        class Response
        {
            public string id;
            public bool ok;
            public string[] lines;
        }

        [Serializable]
        class Alive
        {
            public int pid;
            public string phase;
        }

        static readonly List<string> CompileErrors = new List<string>();
        static readonly List<string> LogLines = new List<string>();
        static double nextHeartbeat;
        static TestRunnerApi api;
        static int pid;

        static BatchServer()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-toyboxServer") < 0) return;

            Directory.CreateDirectory(Dir);
            pid = System.Diagnostics.Process.GetCurrentProcess().Id;

            string phase = SessionState.GetString(KeyPhase, "");
            if (phase == "")
            {
                // Fresh process: discard requests left behind by an earlier session.
                foreach (string stale in Directory.GetFiles(Dir, "req-*.json")) File.Delete(stale);
                SessionState.SetString(KeyPhase, "idle");
            }
            else if (phase == "testing")
            {
                // Test callbacks do not survive a domain reload.
                Respond(Current(), false, new[] { "The scripting domain reloaded during the test run; run it again." });
            }

            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Pump;
        }

        static void Pump()
        {
            double now = EditorApplication.timeSinceStartup;
            string phase = SessionState.GetString(KeyPhase, "idle");

            if (now >= nextHeartbeat)
            {
                nextHeartbeat = now + 1.0;
                WriteAlive(phase);
            }

            if (phase == "idle")
            {
                string file = Directory.GetFiles(Dir, "req-*.json").OrderBy(File.GetCreationTimeUtc).FirstOrDefault();
                if (file == null)
                {
                    Thread.Sleep(30);
                    return;
                }

                string json;
                try
                {
                    json = File.ReadAllText(file);
                    File.Delete(file);
                }
                catch (IOException)
                {
                    return;
                }
                Request request = null;
                try
                {
                    request = JsonUtility.FromJson<Request>(json);
                }
                catch (ArgumentException) { }
                if (request == null || string.IsNullOrEmpty(request.id)) return;

                if (request.command == "quit")
                {
                    Respond(request, true, new[] { "server stopped" });
                    EditorApplication.Exit(0);
                    return;
                }

                CleanOldResponses();
                CompileErrors.Clear();
                LogLines.Clear();
                SessionState.SetString(KeyRequest, json);
                SessionState.SetString(KeyArgs, string.Join("\n", request.args ?? new string[0]));
                SessionState.SetFloat(KeyStable, (float)now);
                SessionState.SetString(KeyPhase, "refresh");
                WriteAlive("refresh");
                AssetDatabase.Refresh();
                return;
            }

            if (phase == "refresh")
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    SessionState.SetFloat(KeyStable, (float)now);
                    return;
                }
                // A compile triggered by the refresh starts a moment later; wait until things stay quiet.
                if (now - SessionState.GetFloat(KeyStable, 0f) < 0.75) return;

                Request request = Current();
                if (request == null)
                {
                    SessionState.SetString(KeyPhase, "idle");
                    return;
                }
                if (EditorUtility.scriptCompilationFailed)
                {
                    var lines = new List<string> { "COMPILE ERRORS (" + CompileErrors.Count + "):" };
                    lines.AddRange(CompileErrors.Distinct().Take(50).Select(e => "  " + e));
                    if (CompileErrors.Count == 0) lines.Add("  (run .\\tools\\typecheck.ps1 for the list)");
                    Respond(request, false, lines);
                    return;
                }
                Run(request);
            }
        }

        static void Run(Request request)
        {
            switch (request.command)
            {
                case "compile":
                    Respond(request, true, new[] { "COMPILE: OK" });
                    break;
                case "test":
                    StartTests(request);
                    break;
                case "exec":
                    Exec(request);
                    break;
                default:
                    Respond(request, false, new[] { "Unknown command: " + request.command });
                    break;
            }
        }

        static void StartTests(Request request)
        {
            if (api == null)
            {
                api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.hideFlags = HideFlags.HideAndDontSave;
                api.RegisterCallbacks(new Callbacks());
            }
            var filter = new Filter { testMode = TestMode.EditMode };
            if (!string.IsNullOrEmpty(request.filter)) filter.groupNames = new[] { request.filter };
            SessionState.SetString(KeyPhase, "testing");
            WriteAlive("testing");
            api.Execute(new ExecutionSettings(filter));
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (SessionState.GetString(KeyPhase, "idle") != "testing") return;
                int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                var lines = new List<string>
                {
                    "TESTS: total=" + total + " passed=" + result.PassCount + " failed=" + result.FailCount +
                    " skipped=" + result.SkipCount + " duration=" + result.Duration.ToString("F1") + "s",
                };
                CollectFailures(result, lines);
                if (total == 0) lines.Add("  (no tests matched the filter)");
                lines.AddRange(LogLines.Where(l => l.StartsWith("[Toybox]")).Take(60));
                Respond(Current(), result.FailCount == 0 && total > 0, lines);
            }
        }

        static void CollectFailures(ITestResultAdaptor result, List<string> lines)
        {
            if (result.HasChildren)
            {
                foreach (ITestResultAdaptor child in result.Children) CollectFailures(child, lines);
                return;
            }
            if (result.TestStatus != TestStatus.Failed) return;
            lines.Add("  FAILED: " + result.Test.FullName);
            if (!string.IsNullOrEmpty(result.Message))
                foreach (string line in result.Message.Trim().Split('\n').Take(14))
                    lines.Add("      " + line.TrimEnd());
            if (!string.IsNullOrEmpty(result.StackTrace))
                foreach (string line in result.StackTrace.Trim().Split('\n').Where(l => l.Contains("Assets/")).Take(4))
                    lines.Add("      " + line.Trim());
        }

        static void Exec(Request request)
        {
            try
            {
                int dot = request.method.LastIndexOf('.');
                string typeName = request.method.Substring(0, dot);
                string methodName = request.method.Substring(dot + 1);
                Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
                MethodInfo method = type?.GetMethod(methodName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    Respond(request, false, new[] { "Method not found: " + request.method });
                    return;
                }
                method.Invoke(null, null);
                Respond(request, true, LogLines.Take(120).ToList());
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                var lines = new List<string>(LogLines.Take(120)) { "EXCEPTION: " + inner.GetType().Name + ": " + inner.Message };
                lines.AddRange(inner.StackTrace.Split('\n').Take(10).Select(l => "  " + l.Trim()));
                Respond(request, false, lines);
            }
        }

        static Request Current()
        {
            string json = SessionState.GetString(KeyRequest, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Request>(json);
        }

        static void Respond(Request request, bool ok, IEnumerable<string> lines)
        {
            SessionState.SetString(KeyPhase, "idle");
            SessionState.SetString(KeyRequest, "");
            SessionState.SetString(KeyArgs, "");
            if (request == null) return;
            var response = new Response { id = request.id, ok = ok, lines = lines.ToArray() };
            string path = Dir + "/res-" + request.id + ".json";
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(response));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
            WriteAlive("idle");
        }

        static void CleanOldResponses()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
            foreach (string file in Directory.GetFiles(Dir, "res-*.json"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                }
                catch (IOException) { }
            }
        }

        static void WriteAlive(string phase)
        {
            try
            {
                File.WriteAllText(Dir + "/alive.json", JsonUtility.ToJson(new Alive { pid = pid, phase = phase }));
            }
            catch (IOException) { }
        }

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            foreach (CompilerMessage message in messages)
                if (message.type == CompilerMessageType.Error)
                    CompileErrors.Add(message.message);
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (LogLines.Count >= 200) return;
            if (condition.StartsWith("[Toybox]")) LogLines.Add(condition);
            else if (type == LogType.Exception) LogLines.Add("EXCEPTION: " + condition);
        }
    }
}
