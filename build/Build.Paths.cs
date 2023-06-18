using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nuke.Common.IO;

namespace _build;

partial class Build
{
    private static AbsolutePath SourceDirectory => RootDirectory / "src";
}
