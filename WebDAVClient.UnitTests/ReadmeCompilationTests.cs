using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ReadmeCompilationTests
    {
        public static IEnumerable<object[]> Examples()
        {
            string readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "README.md"));
            var matches = Regex.Matches(readme,
                @"(?ms)<!-- compile-example: (?<name>[\w-]+) -->\s*```csharp\r?\n(?<code>.*?)^```");
            Assert.AreEqual(7, matches.Count, "Keep the standalone README example inventory explicit.");
            foreach (Match match in matches)
            {
                yield return new object[]
                {
                    match.Groups["name"].Value, match.Groups["code"].Value
                };
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(Examples), DynamicDataSourceType.Method)]
        public void Standalone_readme_example_compiles_against_the_current_framework_and_library(string name, string source)
        {
            var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
            var references = paths.Append(typeof(DavClient).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("ReadmeExample",
                new[]
                {
                    CSharpSyntaxTree.ParseText(source, path: name + ".cs")
                }, references, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
            using var output = new MemoryStream();
            var result = compilation.Emit(output);
            Assert.IsTrue(result.Success, name + Environment.NewLine + string.Join(Environment.NewLine,
                result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }
    }
}
