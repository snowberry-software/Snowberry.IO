using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.MSBuild;
using Serilog;
using System.Runtime.InteropServices;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using System.Drawing;

namespace _build;

partial class Build : NukeBuild
{
    private Configuration? _configuration;

    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server and release/main/master branch)")]
    private Configuration Configuration
    {
        get
        {
            if (_configuration != null)
                return _configuration;

            return IsLocalBuild || (!GitRepository.IsOnReleaseBranch() && !GitRepository.IsOnMainOrMasterBranch()) ? Configuration.Debug : Configuration.Release;
        }

        set => _configuration = value;
    }

    [Parameter("The optional package filter of the packages that will be deployed.")]
    private string NuGetPackageFilter
    {
        get; set;
    } = string.Empty;

#nullable disable
    [Required][GitRepository] private readonly GitRepository GitRepository;

    [Required][Solution] private readonly Solution Solution;
#nullable restore

    /// <inheritdoc />
    protected override void OnBuildInitialized()
    {
        base.OnBuildInitialized();

        Environment.SetEnvironmentVariable("USING_NUKE", "1");

        ProcessTasks.LogWorkingDirectory = true;

        Logging.Level = LogLevel.Normal;

        Log.Information("Building FIFES ({Configuration}) using version Nuke {NukeVersion}.",
            Configuration,
            typeof(NukeBuild).Assembly.GetName().Version);

        if (IsLocalBuild)
        {
            Log.Information("\tRepository Name: {id}", GitRepository.Identifier);
            Log.Information("\tRepository Branch: {branch}", GitRepository.Branch);
        }

        Log.Information($"IsMainBranch: {GitRepository.IsOnMainOrMasterBranch()}");
        Log.Information($"IsReleaseBranch: {GitRepository.IsOnReleaseBranch()}");
        Log.Information($"IsLocalBuild: {IsLocalBuild}");
        Log.Information($"Runtime: {RuntimeInformation.RuntimeIdentifier}");
        Log.Information($"Configuration: {Configuration}");
    }

    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(x => x.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(x => SetDefaultOptions(x));
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var tests = Solution.GetAllProjects("*.Tests");

            foreach (var test in tests)
            {
                Log.Information("Testing {name}...", test.Name);

                DotNetTest(x => SetDefaultOptions(x)
                    .SetProjectFile(test)
                    .SetFramework("net9.0"));
            }
        });

    Target Pack => _ => _
       .DependsOn(Test)
       .Executes(() =>
       {
           foreach (var project in Solution.AllProjects)
           {
               if (string.IsNullOrWhiteSpace(project.GetProperty("NukeMarkPackable")))
                   continue;

               Log.Information("Packing {name}...", project.Name);
               DotNetPack(x => SetDefaultOptions(x)
                    .SetProject(project)
                    .SetOutputDirectory(ArtifactsDirectory));
           }
       });

    Target DeployNuGet => _ => _
       .DependsOn(Pack)
       .Executes(() =>
       {
           if (Configuration != Configuration.Release)
           {
               const string message = "Can only deploy Release config!";
               Log.Fatal(message);
               throw new InvalidOperationException(message);
           }

           string? apiKey = Environment.GetEnvironmentVariable("NUGET_KEY");
           if (string.IsNullOrWhiteSpace(apiKey))
           {
               const string message = "No NuGet API key found!";
               Log.Fatal(message);
               throw new InvalidOperationException(message);
           }

           string filter = string.IsNullOrWhiteSpace(NuGetPackageFilter) ? ArtifactsDirectory / "*.nupkg" : ArtifactsDirectory / NuGetPackageFilter;

           DotNetNuGetPush(x => x.SetApiKey(apiKey)
                    .SetTargetPath(filter)
                    .SetSource("https://api.nuget.org/v3/index.json")
                    .EnableSkipDuplicate());
       });
}
