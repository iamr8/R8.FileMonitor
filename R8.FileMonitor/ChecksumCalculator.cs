using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

using Microsoft.Extensions.Logging;

namespace R8.FileMonitor
{
    internal class ChecksumCalculator : IDisposable
    {
        private readonly ILogger _logger;

        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        public ChecksumCalculator(ILogger logger)
        {
            this._logger = logger;
        }

        public string? GetMd5(string fullFilePath)
        {
            FileStream? fileStream = null;
            try
            {
                _lock.EnterWriteLock();

                using (var md5 = MD5.Create())
                {
                    fileStream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024, FileOptions.SequentialScan);
                    var hash = md5.ComputeHash(fileStream);
                    var output = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    return output;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while calculating checksum for {Path}", fullFilePath);
            }
            finally
            {
                fileStream?.Dispose();

                _lock.ExitWriteLock();
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