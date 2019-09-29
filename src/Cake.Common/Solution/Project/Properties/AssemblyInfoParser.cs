// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Cake.Core;
using Cake.Core.IO;

namespace Cake.Common.Solution.Project.Properties
{
    /// <summary>
    /// The assembly info parser.
    /// </summary>
    public sealed class AssemblyInfoParser
    {
        private const string CSharpNonQuotedPattern = @"^\s*\[assembly: (?:System\.Reflection\.)?{0}(?:Attribute)? ?\((?<attributeValue>.*)\)";
        private const string CSharpQuotedPattern = @"^\s*\[assembly: (?:System\.Reflection\.)?{0}(?:Attribute)? ?\(""(?<attributeValue>.*)""\)";
        private const string VBNonQuotedPattern = @"^\s*\<Assembly: (?:System\.Reflection\.)?{0}(?:Attribute)? ?\((?<attributeValue>.*)\)";
        private const string VBQuotedPattern = @"^\s*\<Assembly: (?:System\.Reflection\.)?{0}(?:Attribute)? ?\(""(?<attributeValue>.*)""\)";
        private const string CSharpCustomAttributePattern = @"^\[assembly: (?:System\.Reflection\.)?(?<attributeName>\w*)(?:Attribute)? ?\((?<attributeValue>.*)\)";
        private const string DefaultVersion = "1.0.0.0";

        private readonly IFileSystem _fileSystem;
        private readonly ICakeEnvironment _environment;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssemblyInfoParser"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="environment">The environment.</param>
        public AssemblyInfoParser(IFileSystem fileSystem, ICakeEnvironment environment)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }
            _fileSystem = fileSystem;
            _environment = environment;
        }

        /// <summary>
        /// Parses information from an assembly info file.
        /// </summary>
        /// <param name="assemblyInfoPath">The file path.</param>
        /// <returns>Information about the assembly info content.</returns>
        public AssemblyInfoParseResult Parse(FilePath assemblyInfoPath)
        {
            if (assemblyInfoPath == null)
            {
                throw new ArgumentNullException(nameof(assemblyInfoPath));
            }

            if (assemblyInfoPath.IsRelative)
            {
                assemblyInfoPath = assemblyInfoPath.MakeAbsolute(_environment);
            }

            string nonQuotedPattern = CSharpNonQuotedPattern;
            string quotedPattern = CSharpQuotedPattern;

            // Get the release notes file.
            var file = _fileSystem.GetFile(assemblyInfoPath);
            if (!file.Exists)
            {
                const string format = "Assembly info file '{0}' does not exist.";
                var message = string.Format(CultureInfo.InvariantCulture, format, assemblyInfoPath.FullPath);
                throw new CakeException(message);
            }
            if (file.Path.GetExtension() == ".vb")
            {
                nonQuotedPattern = VBNonQuotedPattern;
                quotedPattern = VBQuotedPattern;
            }

            using (var reader = new StreamReader(file.OpenRead()))
            {
                var content = reader.ReadToEnd();
                return new AssemblyInfoParseResult(
                    ParseSingle(nonQuotedPattern, "CLSCompliant", content),
                    ParseSingle(quotedPattern, "AssemblyCompany", content),
                    ParseSingle(nonQuotedPattern, "ComVisible", content),
                    ParseSingle(quotedPattern, "AssemblyConfiguration", content),
                    ParseSingle(quotedPattern, "AssemblyCopyright", content),
                    ParseSingle(quotedPattern, "AssemblyDescription", content),
                    ParseSingle(quotedPattern, "AssemblyFileVersion", content) ?? DefaultVersion,
                    ParseSingle(quotedPattern, "Guid", content),
                    ParseSingle(quotedPattern, "AssemblyInformationalVersion", content) ?? DefaultVersion,
                    ParseSingle(quotedPattern, "AssemblyProduct", content),
                    ParseSingle(quotedPattern, "AssemblyTitle", content),
                    ParseSingle(quotedPattern, "AssemblyTrademark", content),
                    ParseSingle(quotedPattern, "AssemblyVersion", content) ?? DefaultVersion,
                    ParseMultiple(quotedPattern, "InternalsVisibleTo", content),
                    ParseCustomAttributes(content, "CLSCompliant", "AssemblyCompany", "ComVisible", "AssemblyConfiguration", "AssemblyCopyright", "AssemblyDescription", "AssemblyFileVersion", "Guid", "AssemblyInformationalVersion", "AssemblyProduct", "AssemblyTitle", "AssemblyTrademark", "AssemblyVersion", "InternalsVisibleTo"));
            }
        }

        private static string ParseSingle(string pattern, string attributeName, string content)
        {
            return ParseMultiple(pattern, attributeName, content).SingleOrDefault();
        }

        private static IEnumerable<string> ParseMultiple(string pattern, string attributeName, string content)
        {
            var regex = new Regex(string.Format(CultureInfo.InvariantCulture, pattern, attributeName), RegexOptions.Multiline);
            foreach (Match match in regex.Matches(content))
            {
                if (match.Groups.Count > 0)
                {
                    var value = match.Groups["attributeValue"].Value;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }
        }

        private IEnumerable<AssemblyInfoCustomAttribute> ParseCustomAttributes(string content, params string[] ignoreAttributeNames)
        {
            //var regexNonQuotedPattern = @"^\[assembly: (?:System\.Reflection\.)?{0}(?<attributeName>\w*)(?:Attribute)? ?\((?<attributeValue>.*)\)";
            //var regexQuotedPattern = @"^\[assembly: (?:System\.Reflection\.)?{0}(?<attributeName>\w*)(?:Attribute)? ?\(""(?<attributeValue>.*)""\)";
            //var regexPattern = @"^\[assembly: (?<attributeName>\w*)(?:Attribute)? ?\((?:(?<attributeQuotedValue>"".*"")|(?<attributeValue>.*)?)?\)";
            // ^\[assembly: (?<attributeName>\w*)(?:Attribute)? ?\((?:""(?<attributeQuotedValue>.*)""|(?<attributeValue>.*))\)
            var regexNonQuotedPattern = @"^\[assembly: (?<attributeName>\w*)(?:Attribute)? ?\((?<attributeValue>.*)\)";
            var regexQuotedPattern = @"^\[assembly: (?<attributeName>\w*)(?:Attribute)? ?\(""(?<attributeValue>.*)""\)";

            var ignoredAttributes = new StringBuilder();

            foreach (var name in ignoreAttributeNames)
            {
                ignoredAttributes.AppendFormat("(?!{0})", name);
            }

            var regexNonQuoted = new Regex(string.Format(CultureInfo.InvariantCulture, regexNonQuotedPattern, ignoredAttributes.ToString()), RegexOptions.Multiline);
            foreach (Match match in regexNonQuoted.Matches(content))
            {
                if (match.Groups.Count > 0)
                {
                    var name = match.Groups["attributeName"].Value;
                    var value = match.Groups["attributeValue"].Value;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return new AssemblyInfoCustomAttribute { Name = name, Value = value };
                    }
                }
            }

            //var regexQuoted = new Regex(string.Format(CultureInfo.InvariantCulture, regexQuotedPattern, ignoredAttributes.ToString()), RegexOptions.Multiline);
            //foreach (Match match in regexQuoted.Matches(content))
            //{
            //    if (match.Groups.Count > 0)
            //    {
            //        var name = match.Groups["attributeName"].Value;
            //        var value = match.Groups["attributeValue"].Value;
            //        if (!string.IsNullOrWhiteSpace(value))
            //        {
            //            yield return new AssemblyInfoCustomAttribute { Name = name, Value = value };
            //        }
            //    }
            //}
        }
    }
}