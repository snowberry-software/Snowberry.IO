using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nuke.Common.Tools.DotNet;

namespace _build;

partial class Build
{
    private DotNetBuildSettings SetDefaultOptions(DotNetBuildSettings settings)
    {
        return settings
            .SetVerbosity(DotNetVerbosity.Quiet)
            .SetProjectFile(Solution)
            .SetConfiguration(Configuration)
            .EnableNoLogo()
            .SetWarningLevel(Configuration == Configuration.Release ? 0 : null);
    }

    private DotNetPackSettings SetDefaultOptions(DotNetPackSettings settings)
    {
        return settings.SetVerbosity(DotNetVerbosity.Quiet)
            .SetConfiguration(Configuration)
            .EnableNoLogo()
            .SetWarningLevel(Configuration == Configuration.Release ? 0 : null);
    }

    private DotNetTestSettings SetDefaultOptions(DotNetTestSettings settings) => settings
                    .SetVerbosity(DotNetVerbosity.Quiet)
                    .SetConfiguration(Configuration)
                    .EnableNoBuild()
                    .EnableNoRestore();
}
