using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace R8.FileMonitor
{
    internal class FileWatcher : IHostedService, IDisposable
    {
        private readonly WatcherOptions _options;

        private readonly ILogger<FileWatcher> _logger;

        public FileWatcher(WatcherOptions options, ILogger<FileWatcher> logger)
        {
            if (string.IsNullOrWhiteSpace(options.FullPath))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(options.FullPath));

            if (string.IsNullOrWhiteSpace(options.ContentRoot))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(options.ContentRoot));

            if (string.IsNullOrWhiteSpace(options.FolderPath))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(options.FolderPath));

            if (string.IsNullOrWhiteSpace(options.OutputFileName))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(options.OutputFileName));

            if (options.FileExtensions == null || !options.FileExtensions.Any())
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(options.FileExtensions));

            _options = options;
            _logger = logger;
            _watcher = new PhysicalFileProvider(_options.FullPath)
            {
                UseActivePolling = true,
                UsePollingFileWatcher = true
            };
        }

        private readonly ConcurrentDictionary<string, FileChange> _cachedFiles = new();

        private PhysicalFileProvider _watcher;

        private IChangeToken? _fileChangeToken;

        private bool _hasChanges;

        private static readonly ReaderWriterLockSlim Lock = new();

        private const string StartState = "--BEGIN";
        private const string EndState = "--END";
        private const char DelimiterColon = ':';
        private const char DelimiterSpace = ' ';

        private const string Filter = "*.*";

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting monitoring files ({Filter}) in '{Path}'", string.Join(", ", _options.FileExtensions), _options.FullPath);

            this.Read();

            var isDirectoryExists = Directory.Exists(_options.FullPath);
            if (isDirectoryExists)
            {
                StartWatch();
            }
            else
            {
                _logger.LogError("The given directory '{Path}' does not exist", _options.FullPath);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopped monitoring files ({Filter}) in '{Path}'", string.Join(", ", _options.FileExtensions), _options.FullPath);

            return Task.CompletedTask;
        }

        private void UpdateCachedFiles(IDirectoryContents contents)
        {
            if (!contents.Any())
                return;

            for (var i = 0; i < contents.Count(); i++)
            {
                var content = contents.ElementAt(i);

                if (string.IsNullOrEmpty(content.PhysicalPath))
                    continue;

                var relativePath = content.PhysicalPath
                    .Replace("\\", "/")
                    .Split(_options.FullPath)[1];
                switch (content)
                {
                    case PhysicalFileInfo fileInfo when fileInfo.Name.Equals(_options.OutputFileName):
                        continue;
                    case PhysicalFileInfo fileInfo when this._cachedFiles.TryGetValue(relativePath, out var existingChange):
                    {
                        if (existingChange.LastModified >= fileInfo.LastModified)
                            continue;

                        var existingChecksum = existingChange.Checksum;

                        string? newChecksum;
                        using (var md5 = new ChecksumCalculator(_logger))
                            newChecksum = md5.GetMd5(fileInfo.PhysicalPath);

                        if (existingChecksum == newChecksum)
                            continue;

                        var file = this._cachedFiles.AddOrUpdate(relativePath, new FileChange
                        {
                            LastModified = fileInfo.LastModified.DateTime,
                            Checksum = newChecksum
                        }, (key, file) =>
                        {
                            file.Checksum = newChecksum;
                            return file;
                        });
                        _logger.LogDebug("`{RelativePath}` updated", relativePath);
                        _hasChanges = true;

                        break;
                    }
                    case PhysicalFileInfo fileInfo:
                    {
                        var fileExt = Path.GetExtension(fileInfo.Name);
                        if (string.IsNullOrWhiteSpace(fileExt))
                            continue; // Ignore files without extension
                        if (!_options.NormalizedFileExtensions.Any(ext => ext.Equals(fileExt, StringComparison.OrdinalIgnoreCase)))
                            continue; // Ignore unaccepted file extensions

                        this._cachedFiles.AddOrUpdate(relativePath, new FileChange
                        {
                            LastModified = fileInfo.LastModified.DateTime
                        }, (key, file) =>
                        {
                            file.LastModified = fileInfo.LastModified.DateTime;
                            return file;
                        });
                        _logger.LogDebug("`{RelativePath}` created/restored", relativePath);
                        _hasChanges = true;

                        break;
                    }
                    case PhysicalDirectoryInfo directoryInfo:
                    {
                        if (IsPathExcluded(relativePath))
                            continue; // Ignored excluded folders

                        var directoryContents = _watcher.GetDirectoryContents(relativePath);
                        if (directoryContents.Exists)
                            UpdateCachedFiles(directoryContents);
                        break;
                    }
                }
            }
        }

        private bool IsPathExcluded(string filePath)
        {
            for (var index = 0; index < _options.NormalizedExcludedPaths.Length; index++)
            {
                var excludedPath = _options.NormalizedExcludedPaths[index];
                if (filePath.StartsWith(excludedPath))
                    return true;
            }

            return false;
        }

        private void StartWatch()
        {
            var watcherContents = _watcher.GetDirectoryContents("/");
            if (watcherContents.Any())
            {
                var files = Directory.GetFiles(_options.FullPath, Filter, SearchOption.AllDirectories);
                var diskFiles = new List<string>();
                for (var i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    file = file.Replace("\\", "/");
                    if (!_options.NormalizedFileExtensions.Any(ext => Path.GetExtension(file).Equals(ext, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    file = file.Split(_options.FullPath)[1];
                    if (!IsPathExcluded(file))
                        diskFiles.Add(file);
                }

                lock (this._cachedFiles)
                {
                    UpdateCachedFiles(watcherContents);

                    var deletedFiles = this._cachedFiles
                        .Where(cachedFile => diskFiles.All(diskFile => !diskFile.Equals(cachedFile.Key, StringComparison.OrdinalIgnoreCase)))
                        .ToDictionary(cachedFile => cachedFile.Key, cachedFile => cachedFile.Value);
                    if (deletedFiles.Any())
                    {
                        foreach (var deletedFile in deletedFiles)
                        {
                            var deleted = this._cachedFiles.TryRemove(deletedFile);
                            if (deleted)
                            {
                                _logger.LogDebug("`{DeletedFileKey}` deleted", deletedFile.Key);
                                _hasChanges = true;
                            }
                            else
                            {
                                _logger.LogError("Fail to delete `{DeletedFileKey}`", deletedFile.Key);
                            }
                        }
                    }

                    UpdateMd5();
                    if (_hasChanges && this.WriteOutput())
                    {
                        _hasChanges = false;
                    }
                }
            }

            _fileChangeToken = _watcher.Watch($"**/{Filter}");
            _fileChangeToken.RegisterChangeCallback(Notify, default);
        }

        private void Notify(object? state) => StartWatch();

        private void Read()
        {
            _logger.LogDebug("Loading `{Output}`", _options.OutputFileName);

            var isDirectoryExists = Directory.Exists(_options.FullPath);
            if (!isDirectoryExists)
            {
                _logger.LogWarning("The given directory '{Path}' does not exist", _options.FullPath);
                return;
            }

            var fileStreamOptions = new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.OpenOrCreate,
                Options = FileOptions.SequentialScan,
                Share = FileShare.Read
            };
            FileStream fileStream = null;
            try
            {
                Lock.EnterWriteLock();

                fileStream = new FileStream(_options.OutputFullPath, fileStreamOptions);
                var sr = new StreamReader(fileStream);
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();

                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (line.Equals(StartState) || line.Equals(EndState))
                        continue;

                    if (!line.Contains(DelimiterSpace) && !line.Contains(DelimiterColon))
                        continue;

                    try
                    {
                        string checksum;
                        string cachedFile;
                        if (line.Contains(DelimiterColon))
                        {
                            var arr = line.Split(DelimiterColon);
                            cachedFile = arr[0];
                            checksum = arr[1];
                        }
                        else if (line.Contains(DelimiterSpace))
                        {
                            var delimiterIndex = line.IndexOf(DelimiterSpace);
                            checksum = line[..delimiterIndex];
                            cachedFile = line[(delimiterIndex + 1)..];
                        }
                        else
                        {
                            continue;
                        }

                        var diskFilePath = Path.Combine(_options.FullPath, cachedFile);
                        var lastModified = File.GetLastWriteTime(diskFilePath);
                        if (lastModified == DateTime.MinValue)
                        {
                            _logger.LogWarning("The given file '{FilePath}' inside '{Output}' does not exist", diskFilePath, _options.OutputFileName);
                        }

                        if (IsPathExcluded(cachedFile))
                            continue;

                        this._cachedFiles.AddOrUpdate(cachedFile, new FileChange
                        {
                            Checksum = checksum,
                            LastModified = lastModified
                        }, (key, file) =>
                        {
                            file.Checksum = checksum;
                            file.LastModified = lastModified;
                            return file;
                        });
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error while reading {Output}", _options.OutputFileName);
                    }
                }

                sr.Dispose();
                _logger.LogInformation("`{Output}` loaded", _options.OutputFileName);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while reading {Output}", _options.OutputFileName);
            }
            finally
            {
                Lock.ExitWriteLock();

                fileStream?.Dispose();
            }
        }

        private void UpdateMd5()
        {
            for (var i = 0; i < this._cachedFiles.Count; i++)
            {
                var cachedFile = this._cachedFiles.ElementAt(i);
                var cachedFullPath = Path.Combine(_options.FullPath, cachedFile.Key);
                if (!string.IsNullOrEmpty(cachedFile.Value.Checksum))
                    continue;

                using (var md5 = new ChecksumCalculator(_logger))
                    cachedFile.Value.Checksum = md5.GetMd5(cachedFullPath);
            }
        }

        private bool WriteOutput()
        {
            _logger.LogDebug("Updating `{Output}`", _options.OutputFileName);

            try
            {
                var sb = new StringBuilder(1024 * 4);
                for (var i = 0; i < this._cachedFiles.Count; i++)
                {
                    var cachedFile = this._cachedFiles.ElementAt(i);
                    sb
                        .Append(cachedFile.Key.Replace("\\", "/"))
                        .Append(DelimiterColon)
                        .Append(cachedFile.Value.Checksum);

                    if (i != this._cachedFiles.Count - 1)
                        sb.AppendLine();
                }

                File.WriteAllText(_options.OutputFullPath, sb.ToString(), Encoding.UTF8);
                _logger.LogInformation("`{Output}` updated", _options.OutputFileName);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while saving {Output}", _options.OutputFileName);
            }

            return false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            Lock.Dispose();
            _watcher.Dispose();
            _watcher = null;
        }
    }
}