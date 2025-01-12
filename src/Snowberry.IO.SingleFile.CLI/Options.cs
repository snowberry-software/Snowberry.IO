using CommandLine;

namespace Snowberry.IO.SingleFile.CLI;

internal class Options
{
    [Option('i', "input", Required = true, HelpText = "Sets the input single file path.")]
    public string InputFilePath { get; set; } = string.Empty;

    [Option('o', "output", Required = false, HelpText = "Sets the output directory.")]
    public string OutputDirectory { get; set; } = string.Empty;
}
