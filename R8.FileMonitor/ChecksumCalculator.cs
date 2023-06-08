using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace R8.FileMonitor
{
    internal class ChecksumCalculator : IDisposable
    {
        private readonly ILogger _logger;

        private readonly ReaderWriterLockSlim _lock = new();

        public ChecksumCalculator(ILogger logger)
        {
            this._logger = logger;
        }

        public string? GetMd5(string fullFilePath)
        {
            var fileStreamOptions = new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.Read
            };

            FileStream fileStream = null;
            try
            {
                _lock.EnterWriteLock();

                using var md5 = MD5.Create();
                fileStream = new FileStream(fullFilePath, fileStreamOptions);
                var hash = md5.ComputeHash(fileStream);
                var output = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                md5.Dispose();
                return output;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while calculating checksum for {Path}", fullFilePath);
            }
            finally
            {
                _lock.ExitWriteLock();

                fileStream?.Dispose();
            }

            return null;
        }

        private void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            _lock.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}