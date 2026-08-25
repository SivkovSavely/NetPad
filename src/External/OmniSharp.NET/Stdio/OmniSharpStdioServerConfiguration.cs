using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OmniSharp.Stdio
{
    internal class OmniSharpStdioServerConfiguration : OmniSharpServerConfiguration
    {
        public OmniSharpStdioServerConfiguration(Func<Process> processGetter) : base(OmniSharpServerProtocolType.Stdio)
        {
            ProcessGetter = processGetter ?? throw new ArgumentNullException(nameof(processGetter));
        }

        public OmniSharpStdioServerConfiguration(
            string executablePath,
            string args,
            string? dotNetSdkRootDirectoryPath,
            string? workingDirectory,
            IReadOnlyDictionary<string, string?>? environmentVariables = null) : base(OmniSharpServerProtocolType.Stdio)
        {
            ExecutablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
            ExecutableArgs = args ?? throw new ArgumentNullException(nameof(args));
            DotNetSdkRootDirectoryPath = dotNetSdkRootDirectoryPath;
            WorkingDirectory = workingDirectory;
            EnvironmentVariables = environmentVariables;
        }

        public Func<Process>? ProcessGetter { get; }
        public string? ExecutablePath { get; }
        public string? ExecutableArgs { get; }
        public bool ExternallyManagedProcess => ProcessGetter != null;
        public string? DotNetSdkRootDirectoryPath { get; }
        public string? WorkingDirectory { get; }

        /// <summary>Additional environment variables to set on the spawned process.</summary>
        public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; }
    }
}
