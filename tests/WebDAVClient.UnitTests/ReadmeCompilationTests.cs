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
            var documents = new[] { Path.Combine(AppContext.BaseDirectory, "README.md") }
                .Concat(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "docs"), "*.md", SearchOption.AllDirectories));
            int count = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string document in documents)
            {
                var matches = Regex.Matches(File.ReadAllText(document),
                    @"(?ms)<!-- compile-example: (?<name>[\w-]+) -->\s*```csharp\r?\n(?<code>.*?)^```");
                foreach (Match match in matches)
                {
                    string name = match.Groups["name"].Value;
                    Assert.IsTrue(names.Add(name), "Duplicate complete example: " + name);
                    count++;
                    yield return new object[] { name, match.Groups["code"].Value };
                }
            }
            Assert.AreEqual(11, count, "Keep the complete README and guide example inventory explicit.");
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
