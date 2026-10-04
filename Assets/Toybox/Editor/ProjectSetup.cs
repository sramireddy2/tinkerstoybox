using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Toybox.EditorTools
{
    /// <summary>
    /// Marks a static, parameterless method as one step of the project setup. <see cref="ProjectSetup.Run"/>
    /// finds every such method in the Toybox.Editor assembly and runs them in ascending order (ties go by
    /// type name, then method name). A step must be idempotent: running it twice leaves the project as
    /// running it once does.
    ///
    /// Every area keeps its steps in a file of its own, Editor/Setup/&lt;Area&gt;Setup.cs. Orders 0-99 belong
    /// to CoreSetup; areas use 100 and up, and a later step may replace what an earlier one assigned.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class SetupStepAttribute : Attribute
    {
        public int Order { get; }

        public SetupStepAttribute(int order) => Order = order;
    }

    /// <summary>One discovered setup step.</summary>
    public sealed class SetupStep
    {
        public int Order { get; }
        /// <summary>"Type.Method", e.g. "CoreSetup.ConfigurePlayer".</summary>
        public string Name { get; }
        public MethodInfo Method { get; }

        public SetupStep(int order, MethodInfo method)
        {
            Order = order;
            Method = method;
            Name = method.DeclaringType.Name + "." + method.Name;
        }

        public void Invoke() => Method.Invoke(null, null);

        public override string ToString() => Order + " " + Name;
    }

    /// <summary>
    /// Idempotent project configuration. Everything that would normally be clicked together in the
    /// editor (render pipeline assets, player settings, layers, materials, the bootstrap scene) is created
    /// by setup steps, so the project can be rebuilt from source control alone:
    ///
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.Run
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.Run -UnityArgs '-toyboxSteps','RoomSetup'
    ///
    /// This class only finds the steps and runs them; it holds none itself. -toyboxSteps limits the run to
    /// steps whose name contains the given text (several, separated by commas).
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Toybox/Scenes/Main.unity";

        [MenuItem("Toybox/Run Project Setup")]
        public static void Run()
        {
            IReadOnlyList<SetupStep> steps = Discover();
            string only = ToyboxArgs.Get("-toyboxSteps");
            if (!string.IsNullOrEmpty(only)) steps = Filter(steps, only);
            int failed = RunSteps(steps);
            AssetDatabase.SaveAssets();
            if (failed > 0) throw new InvalidOperationException("Project setup: " + failed + " of " + steps.Count + " steps failed (see the errors above).");
            Debug.Log("[Toybox] Project setup complete (" + steps.Count + " steps).");
        }

        /// <summary>Lists the steps without running them: tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.List</summary>
        public static void List()
        {
            foreach (SetupStep step in Discover()) Debug.Log("[Toybox] setup step: " + step);
        }

        /// <summary>The setup steps of an assembly (the editor assembly if none is given), in the order they run.</summary>
        public static List<SetupStep> Discover(Assembly assembly = null)
        {
            assembly ??= typeof(ProjectSetup).Assembly;
            var steps = new List<SetupStep>();
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(flags))
                {
                    SetupStepAttribute attribute = method.GetCustomAttribute<SetupStepAttribute>();
                    if (attribute == null) continue;
                    if (method.GetParameters().Length != 0 || method.IsGenericMethodDefinition)
                        throw new InvalidOperationException("[SetupStep] " + type.Name + "." + method.Name + " must be a static method without parameters.");
                    steps.Add(new SetupStep(attribute.Order, method));
                }
            }
            steps.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
            return steps;
        }

        /// <summary>The steps whose name contains one of the comma-separated texts.</summary>
        public static List<SetupStep> Filter(IReadOnlyList<SetupStep> steps, string names)
        {
            string[] wanted = names.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<SetupStep>();
            foreach (SetupStep step in steps)
                foreach (string name in wanted)
                    if (step.Name.IndexOf(name.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        kept.Add(step);
                        break;
                    }
            return kept;
        }

        /// <summary>
        /// Runs the steps in the order given, logging "[Toybox] setup: name" before each. A step that throws
        /// is reported and the rest still run. Returns how many failed.
        /// </summary>
        public static int RunSteps(IReadOnlyList<SetupStep> steps)
        {
            int failed = 0;
            foreach (SetupStep step in steps)
            {
                Debug.Log("[Toybox] setup: " + step.Name);
                try
                {
                    step.Invoke();
                }
                catch (Exception e)
                {
                    failed++;
                    Exception cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    Debug.LogError("[Toybox] setup FAILED: " + step.Name + ": " + cause.GetType().Name + ": " + cause.Message);
                    Debug.LogException(cause);
                }
            }
            return failed;
        }
    }
}
