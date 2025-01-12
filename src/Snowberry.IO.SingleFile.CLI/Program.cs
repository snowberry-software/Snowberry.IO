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

Parser.Default.ParseArguments<Options>(args)
.WithParsed(o =>
{
    if (!File.Exists(o.InputFilePath))
    {
        Log.Fatal("Could not find input file path: {input}", o.InputFilePath);
        Environment.Exit(-1);
    }

    Log.Information("Processing: {path}", o.InputFilePath);

    bool useOutputDirectory = !string.IsNullOrWhiteSpace(o.OutputDirectory);

    try
    {
        using var singleFileBinaryData = SingleFileBinaryData.GetFromFile(o.InputFilePath);

        if (singleFileBinaryData == null)
        {
            Log.Fatal("Specified file is not a published single file!");
            Environment.Exit(-1);
        }

        if (singleFileBinaryData.BundleManifest == null)
            return;

        var bundleManifest = singleFileBinaryData.BundleManifest;
        Log.Information($"{"Bundle ID:",-24} {{id}}", bundleManifest.BundleID);
        Log.Information($"{"Bundle Major Version:",-24} {{ver}}", bundleManifest.BundleMajorVersion);
        Log.Information($"{"Bundle Minor Version:",-24} {{ver}}", bundleManifest.BundleMinorVersion);
        Log.Information($"{"Bundle Flags:",-24} {{flags}}", bundleManifest.Flags);
        Log.Information($"{"Bundle File Count:",-24} {{count}}", bundleManifest.FileEntries.Count);
        Log.Information("");

        string? outputDirectory = useOutputDirectory ? o.OutputDirectory : null;

        if (useOutputDirectory && !Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory!);

        for (int i = 0; i < bundleManifest.FileEntries.Count; i++)
        {
            var fileEntry = bundleManifest.FileEntries[i];

            Log.Information("File: {type}/{path}", fileEntry.FileType, fileEntry.RelativePath);

            if (useOutputDirectory)
            {
                using var fileEntryStream = singleFileBinaryData.GetStream(fileEntry);

                if (fileEntryStream == null)
                {
                    Log.Fatal("    Could not open stream for file entry!");
                    Environment.Exit(-1);
                }

                string filePath = Path.Combine(outputDirectory!, fileEntry.RelativePath);
                string? directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                fileEntryStream.CopyTo(fs);
            }
        }
    }
    catch (Exception e)
    {
        Log.Fatal(e, "Could not process file: {input}", o.InputFilePath);
    }

    Log.Information("Done...");
});