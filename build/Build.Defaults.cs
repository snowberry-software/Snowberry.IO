using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nuke.Common.Tools.DotNet;

namespace _build;

partial class Build
{
    public DotNetBuildSettings SetDefaultOptions(DotNetBuildSettings settings)
    {
        return settings
            .SetVerbosity(DotNetVerbosity.Normal)
            .SetProjectFile(Solution)
            .SetConfiguration(Configuration)
            .EnableNoLogo()
            .SetWarningLevel(Configuration == Configuration.Release ? 0 : null);
    }

    public DotNetTestSettings SetDefaultOptions(DotNetTestSettings settings) => settings
                    .SetVerbosity(DotNetVerbosity.Normal)
                    .SetConfiguration(Configuration)
                    .EnableNoBuild()
                    .EnableNoRestore();
}
