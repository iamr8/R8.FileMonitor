using System;
using System.Linq;

namespace R8.FileMonitor
{
    public class WatcherOptions
    {
        private string _path;
        private static string? _contentRoot;

        /// <summary>
        /// Gets or sets the relative path to monitor.
        /// </summary>
        /// <remarks>Example: <c>/wwwroot</c></remarks>
        public string FolderPath
        {
            get
            {
                if (string.IsNullOrEmpty(_path))
                    return null;

                var p = _path.Replace("\\", "/");
                if (p.StartsWith("/"))
                    p = p[1..];

                if (!p.EndsWith("/"))
                    p += "/";

                return p;
            }
            set => _path = value;
        }

        /// <summary>
        /// Gets or sets the extensions to monitor.
        /// </summary>
        /// <remarks>Example: <c>new[] { ".js", ".css" }</c></remarks>
        public string[] FileExtensions { get; set; }

        /// <summary>
        /// Gets or sets the output file name.
        /// </summary>
        /// <remarks>Example: <c>monitor-stat.txt</c></remarks>
        public string OutputFileName { get; set; }

        /// <summary>
        /// Gets or sets the excluded sub directories.
        /// </summary>
        public string[] ExcludedPaths { get; set; }

        internal string[] NormalizedExcludedPaths
        {
            get
            {
                if (this.ExcludedPaths == null || !this.ExcludedPaths.Any())
                    return Array.Empty<string>();

                var paths = new string[this.ExcludedPaths.Length];
                for (var i = 0; i < this.ExcludedPaths.Length; i++)
                {
                    var path = this.ExcludedPaths[i];
                    if (path.StartsWith("/"))
                        path = path[1..];
                    
                    if (!path.EndsWith("/"))
                        path += "/";

                    paths[i] = path;
                }

                return paths;
            }
        }
        
        internal string[] NormalizedFileExtensions
        {
            get
            {
                if (!this.FileExtensions.Any())
                    return Array.Empty<string>();

                var extensions = new string[this.FileExtensions.Length];
                for (var i = 0; i < this.FileExtensions.Length; i++)
                {
                    var extension = this.FileExtensions[i];
                    if (!extension.StartsWith("."))
                        extension = "." + extension;

                    extensions[i] = extension;
                }

                return extensions;
            }
        }

        /// <summary>
        /// Gets or sets the content root path.
        /// </summary>
        public string? ContentRoot
        {
            get => _contentRoot?.Replace("\\", "/");
            set => _contentRoot = value;
        }

        internal string FullPath
        {
            get
            {
                string path;
#if DEBUG
                path = System.IO.Path.Combine(ContentRoot, FolderPath).Replace("\\", "/");
#else
            path = Path;
            if (!path.StartsWith("/"))
                path = "/" + path;
#endif
                return path;
            }
        }

        internal string OutputFullPath => System.IO.Path.Combine(FullPath, OutputFileName);
    }
}