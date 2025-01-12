[![NuGet Version](https://img.shields.io/nuget/v/Snowberry.IO.SingleFile.svg?logo=nuget)](https://www.nuget.org/packages/Snowberry.IO.SingleFile/)

# Snowberry.IO.SingleFile

A library for reading and modifying bundles from single-file published .NET applications.

## How to use it

### Reading and modifying the data from a single-file published .NET application

```csharp
using var singleFileBinaryData = SingleFileBinaryData.GetFromFile(o.InputFilePath);

// To access the bundle manifest
var bundleManifest = singleFileBinaryData.BundleManifest;

// To access information about the bundle's files
var fileEntry = bundleManifest.FileEntries[0];

// To read the file's data
using var fileEntryStream = singleFileBinaryData.GetStream(fileEntry);

// Do stuff...

// Modify the file entry
SingleFileBinaryData.ModifyFileEntry(fileEntry, new byte[]);
// or
fileEntry.ModifiedFileEntryMeta?.Dispose();
fileEntry.ModifiedFileEntryMeta = new()
{
    ModifiedDataStream = stream,
    FileLocation = new()
    {
        Offset = 0,
        Size = stream.Length
    }
};

// Repack the application
singleFileBinaryData.Save("output.exe", TargetInfo.Windows, new BundlerOptions());
```
