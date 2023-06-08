# FileMonitor
Monitoring file changes in a directory and its subdirectories.

## Usage
```csharp
// ... other services

services.AddFileMonitor(options =>
{
    // REQUIRED: The root directory of the files to be monitored.
    options.ContentRoot = Directory.GetCurrentDirectory(); // Or context.HostingEnvironment.ContentRootPath;
    
    // REQUIRED: The relative path of the directory to be monitored.    
    options.FolderPath = "/files";
    
    // REQUIRED: The file extensions to be monitored.
    options.FileExtensions = new[] { ".txt" };
    
    // REQUIRED: The output file name to store the file changes and their MD5 checksums.
    options.OutputFileName = "output.txt";
    
    // OPTIONAL: The sub directories to be excluded.
    options.ExcludedPaths = new[] { "/files/excluded" };
});
```

... and, **TADAAA**!!!
The `output.txt` file will be created in the `ContentRoot` directory, and it will contain the file changes and their MD5 checksums.