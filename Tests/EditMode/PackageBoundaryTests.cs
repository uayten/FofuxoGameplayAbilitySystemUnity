using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Fofuxo.GameplayAbilitySystem.Editor;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The boundary that makes this a package rather than a folder: nothing in
    /// it may reach into a consumer project. The adapter direction is game to
    /// package, and the compiler is the only honest place to check it.
    /// </summary>
    public sealed class PackageBoundaryTests
    {
        private static readonly string[] ConsumerAssemblies =
        {
            "Assembly-CSharp",
            "Assembly-CSharp-Editor",
            "Assembly-CSharp-firstpass",
            "Assembly-CSharp-Editor-firstpass"
        };

        [Test]
        public void NoPackageAssemblyReferencesTheConsumerProject()
        {
            List<string> offences = new();
            foreach (Assembly assembly in PackageAssemblies())
            {
                foreach (AssemblyName referenced in assembly.GetReferencedAssemblies())
                {
                    foreach (string consumer in ConsumerAssemblies)
                    {
                        if (referenced.Name == consumer)
                        {
                            offences.Add($"{assembly.GetName().Name} -> {consumer}");
                        }
                    }
                }
            }

            Assert.IsEmpty(offences, string.Join("\n", offences));
        }

        [Test]
        public void TheSampleIsNotCompiledIntoThePackage()
        {
            // A sample lives in Samples~, which Unity does not compile until
            // someone imports it. A sample type reachable from here would mean
            // the package ships its own demo content to every consumer.
            foreach (Assembly assembly in PackageAssemblies())
            {
                foreach (System.Type type in assembly.GetTypes())
                {
                    Assert.IsFalse(
                        type.Namespace != null &&
                        type.Namespace.StartsWith("Fofuxo.GameplayAbilitySystem.Samples"),
                        $"{type.FullName} is sample content compiled into {assembly.GetName().Name}");
                }
            }
        }

        private static IEnumerable<Assembly> PackageAssemblies()
        {
            yield return typeof(AbilitySystem).Assembly;
            yield return typeof(AbilityInputRouter).Assembly;
            yield return typeof(AbilityRigidbodyMotor).Assembly;
            yield return typeof(Editor.AbilitySystemDebuggerWindow).Assembly;
        }
    }
}
