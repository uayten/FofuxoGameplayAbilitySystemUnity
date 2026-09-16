using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The documentation contract: every public type in the runtime assemblies
    /// explains itself where it is declared. A package whose types are only
    /// described in the README goes stale the first time the README does not
    /// get updated with the code.
    /// </summary>
    public sealed class PublicApiDocumentationTests
    {
        private static readonly Regex PublicType = new(
            @"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|readonly\s+|partial\s+|ref\s+)*(?:class|struct|interface|enum)\s+(\w+)",
            RegexOptions.Compiled);

        [Test]
        public void EveryPublicRuntimeTypeIsDocumented()
        {
            List<string> undocumented = new();
            foreach (string file in RuntimeSources())
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    Match match = PublicType.Match(lines[i]);
                    if (match.Success && !IsDocumented(lines, i))
                    {
                        undocumented.Add($"{Path.GetFileName(file)}:{i + 1} {match.Groups[1].Value}");
                    }
                }
            }

            Assert.IsEmpty(
                undocumented,
                "Every public runtime type needs a /// <summary>:\n" +
                string.Join("\n", undocumented));
        }

        [Test]
        public void TheRuntimeSourcesAreFound()
        {
            // Without this the test above passes by reading nothing at all.
            Assert.IsNotEmpty(RuntimeSources(), "no runtime sources were found");
        }

        /// <summary>
        /// Walks back over blank lines and whole attribute blocks - an
        /// attribute may span several lines - to the line that would carry the
        /// documentation.
        /// </summary>
        private static bool IsDocumented(IReadOnlyList<string> lines, int declaration)
        {
            int i = declaration - 1;
            while (i >= 0)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    i--;
                    continue;
                }

                if (line.StartsWith("///"))
                {
                    return true;
                }

                if (line.EndsWith("]"))
                {
                    int depth = 0;
                    while (i >= 0)
                    {
                        depth += Count(lines[i], ']') - Count(lines[i], '[');
                        i--;
                        if (depth == 0)
                        {
                            break;
                        }
                    }

                    continue;
                }

                return false;
            }

            return false;
        }

        private static int Count(string line, char character)
        {
            int total = 0;
            foreach (char item in line)
            {
                if (item == character)
                {
                    total++;
                }
            }

            return total;
        }

        private static List<string> RuntimeSources()
        {
            List<string> files = new();
            string runtime = Path.Combine(PackageRoot(), "Runtime");
            if (Directory.Exists(runtime))
            {
                files.AddRange(Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories));
            }

            return files;
        }

        private static string PackageRoot()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(AbilitySystem).Assembly);
            return package != null
                ? package.resolvedPath
                : Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "Packages",
                    "com.uayten.fofuxogameplayabilitysystem"));
        }
    }
}
