using CommandLine;
using Serilog;
using Snowberry.IO.SingleFile;
using Snowberry.IO.SingleFile.CLI;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
#if DEBUG
    .WriteTo.Debug()
#endif
    .CreateLogger();

int exitCode = 0;

Parser.Default.ParseArguments<Options>(args)
    .WithParsed(o => exitCode = Run(o))
    .WithNotParsed(errors => exitCode = errors.All(e => e is HelpRequestedError or VersionRequestedError) ? 0 : 1);

return exitCode;

static int Run(Options o)
{
    if (!File.Exists(o.InputFilePath))
    {
        Log.Fatal("Could not find input file path: {input}", o.InputFilePath);
        return 1;
    }

    Log.Information("Processing: {path}", o.InputFilePath);

    try
    {
        if (!SingleFileBundle.TryOpen(o.InputFilePath, out var bundle))
        {
            Log.Fatal("Specified file is not a published single file!");
            return 1;
        }

        using (bundle)
        {
            var header = bundle.Header;
            Log.Information($"{"Bundle ID:",-24} {{id}}", Printable(header.Id));
            Log.Information($"{"Bundle Version:",-24} {{major}}.{{minor}}", header.MajorVersion, header.MinorVersion);
            Log.Information($"{"Bundle Flags:",-24} {{flags}}", header.Flags);
            Log.Information($"{"Bundle File Count:",-24} {{count}}", bundle.Entries.Count);
            Log.Information("");

            foreach (var entry in bundle.Entries)
                Log.Information("File: {type}/{path} ({size} bytes{compressed})", entry.Type, Printable(entry.RelativePath), entry.Size, entry.IsCompressed ? ", compressed" : string.Empty);

            if (!string.IsNullOrWhiteSpace(o.OutputDirectory))
            {
                bundle.ExtractToDirectory(o.OutputDirectory, overwrite: o.Force);
                Log.Information("Extracted {count} files to {directory}", bundle.Entries.Count, o.OutputDirectory);
            }
        }
    }
    catch (Exception e)
    {
        // Messages can contain entry paths from the file, so only the sanitized message is logged.
        Log.Fatal("Could not process file: {input} ({error}: {message})", o.InputFilePath, e.GetType().Name, Printable(e.Message));
        return 1;
    }

    Log.Information("Done...");
    return 0;
}

// Entry paths and the bundle ID come from the file; escape control characters so they cannot inject terminal escape sequences.
static string Printable(string value)
{
    var builder = new System.Text.StringBuilder(value.Length);

    foreach (char c in value)
    {
        if (char.IsControl(c))
            builder.Append($"\\u{(int)c:X4}");
        else
            builder.Append(c);
    }

    return builder.ToString();
}