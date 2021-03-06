﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Tooling;

namespace Cake.Common.Tools.DupFinder
{
    /// <summary>
    /// DupFinder runner
    /// </summary>
    public sealed class DupFinderRunner : Tool<DupFinderSettings>
    {
        private readonly IFileSystem _fileSystem;
        private readonly ICakeEnvironment _environment;
        private readonly ICakeLog _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="DupFinderRunner"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="environment">The environment.</param>
        /// <param name="processRunner">The process runner.</param>
        /// <param name="globber">The globber.</param>
        /// <param name="log">The logger.</param>
        public DupFinderRunner(IFileSystem fileSystem, ICakeEnvironment environment, IProcessRunner processRunner, IGlobber globber, ICakeLog log)
            : base(fileSystem, environment, processRunner, globber)
        {
            _fileSystem = fileSystem;
            _environment = environment;
            _log = log;
        }

        /// <summary>
        /// Analyses the specified files using the specified settings.
        /// </summary>
        /// <param name="filePaths">The file paths.</param>
        /// <param name="settings">The settings.</param>
        public void Run(IEnumerable<FilePath> filePaths, DupFinderSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (filePaths == null)
            {
                throw new ArgumentNullException("filePaths");
            }

            Run(settings, GetArgument(settings, filePaths));

            if (settings.OutputFile != null)
            {
                AnalyzeResultsFile(settings.OutputFile, settings.ThrowExceptionOnFindingDuplicates);
            }
        }

        /// <summary>
        /// Runs ReSharper's DupFinder using the provided config file.
        /// </summary>
        /// <param name="configFile">The config file.</param>
        public void RunFromConfig(FilePath configFile)
        {
            if (configFile == null)
            {
                throw new ArgumentNullException("configFile");
            }

            Run(new DupFinderSettings(), GetConfigArgument(configFile));
        }

        private ProcessArgumentBuilder GetConfigArgument(FilePath configFile)
        {
            var builder = new ProcessArgumentBuilder();
            builder.AppendQuoted(string.Format(CultureInfo.InvariantCulture, "/config={0}",
                configFile.MakeAbsolute(_environment).FullPath));

            return builder;
        }

        private ProcessArgumentBuilder GetArgument(DupFinderSettings settings, IEnumerable<FilePath> files)
        {
            var builder = new ProcessArgumentBuilder();

            if (settings.Debug)
            {
                builder.Append("/debug");
            }

            if (settings.DiscardCost != null)
            {
                builder.Append(string.Format(CultureInfo.InvariantCulture, "/discard-cost={0}", settings.DiscardCost));
            }

            if (settings.DiscardFieldsName)
            {
                builder.Append("/discard-fields");
            }

            if (settings.DiscardLiterals)
            {
                builder.Append("/discard-literals");
            }

            if (settings.DiscardLocalVariablesName)
            {
                builder.Append("/discard-local-vars");
            }

            if (settings.DiscardTypes)
            {
                builder.Append("/discard-types");
            }

            if (settings.IdlePriority)
            {
                builder.Append("/idle-priority");
            }

            if (settings.ExcludeFilesByStartingCommentSubstring != null &&
                settings.ExcludeFilesByStartingCommentSubstring.Any())
            {
                var joined = string.Join(";", settings.ExcludeFilesByStartingCommentSubstring);
                builder.AppendQuoted(string.Format(CultureInfo.InvariantCulture, "/exclude-by-comment={0}", joined));
            }

            if (settings.ExcludeCodeRegionsByNameSubstring != null && settings.ExcludeCodeRegionsByNameSubstring.Any())
            {
                var joined = string.Join(";", settings.ExcludeCodeRegionsByNameSubstring);
                builder.AppendQuoted(string.Format(CultureInfo.InvariantCulture, "/exclude-code-regions={0}", joined));
            }

            if (settings.ExcludePattern != null && settings.ExcludePattern.Any())
            {
                var joined = string.Join(";", settings.ExcludePattern);
                builder.AppendQuoted(string.Format(CultureInfo.InvariantCulture, "/exclude={0}", joined));
            }

            if (settings.MsBuildProperties != null)
            {
                foreach (var property in settings.MsBuildProperties)
                {
                    builder.AppendQuoted(string.Format(CultureInfo.InvariantCulture, "/properties:{0}={1}", property.Key,
                        property.Value));
                }
            }

            if (settings.NormalizeTypes)
            {
                builder.Append("/normalize-types");
            }

            if (settings.OutputFile != null)
            {
                builder.AppendQuoted(string.Format(CultureInfo.InvariantCulture, "/output={0}",
                    settings.OutputFile.MakeAbsolute(_environment).FullPath));
            }

            if (settings.CachesHome != null)
            {
                builder.AppendQuoted(string.Format(CultureInfo.InvariantCulture, "/caches-home={0}",
                    settings.CachesHome.MakeAbsolute(_environment).FullPath));
            }

            if (settings.ShowStats)
            {
                builder.Append("/show-stats");
            }

            if (settings.ShowText)
            {
                builder.Append("/show-text");
            }

            foreach (var file in files)
            {
                builder.AppendQuoted(file.MakeAbsolute(_environment).FullPath);
            }

            return builder;
        }

        private void AnalyzeResultsFile(FilePath resultsFilePath, bool throwOnDuplicates)
        {
            var anyFailures = false;
            var resultsFile = _fileSystem.GetFile(resultsFilePath);
            var xmlDoc = XDocument.Load(resultsFile.Open(FileMode.Open));
            var duplicates = xmlDoc.Descendants("Duplicate");

            foreach (var duplicate in duplicates)
            {
                _log.Warning("Duplicate Located with a cost of {0}, across {1} Fragments", duplicate.Attribute("Cost"), duplicate.Descendants("Fragment").Count());

                foreach (var fragment in duplicate.Descendants("Fragment"))
                {
                    var fileNameNode = fragment.Descendants("FileName").FirstOrDefault();
                    var lineRangeNode = fragment.Descendants("LineRange").FirstOrDefault();

                    if (fileNameNode != null && lineRangeNode != null)
                    {
                        _log.Warning("File Name: {0} Line Numbers: {1} - {2}", fileNameNode.Value, lineRangeNode.Attribute("Start"), lineRangeNode.Attribute("End"));
                    }
                }

                anyFailures = true;
            }

            if (anyFailures && throwOnDuplicates)
            {
                throw new CakeException("Duplicates found in code base.");
            }
        }

        /// <summary>
        /// Gets the name of the tool.
        /// </summary>
        /// <returns>The name of the tool.</returns>
        protected override string GetToolName()
        {
            return "DupFinder";
        }

        /// <summary>
        /// Gets the possible names of the tool executable.
        /// </summary>
        /// <returns>The tool executable name.</returns>
        protected override IEnumerable<string> GetToolExecutableNames()
        {
            return new[] { "dupfinder.exe" };
        }
    }
}