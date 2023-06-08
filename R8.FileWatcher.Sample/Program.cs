using Microsoft.Extensions.Hosting;
using R8.FileMonitor;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddFileMonitor(options =>
        {
            // options.ContentRoot = context.HostingEnvironment.ContentRootPath;
            options.ContentRoot = Directory.GetCurrentDirectory();
            options.FolderPath = "/files";
            options.FileExtensions = new[] { ".txt" };
            options.OutputFileName = "output.txt";
        });
    })
    .Build();

await host.RunAsync();