﻿using System;
using System.Collections.Generic;
using log4net;
using CKAN.NetKAN.Model;

namespace CKAN.NetKAN.Services
{
    internal sealed class CachingHttpService : IHttpService
    {
        private readonly NetFileCache _cache;
        private          HashSet<Uri> _requestedURLs  = new HashSet<Uri>();
        private          bool         _overwriteCache = false;
        private Dictionary<Uri, StringCacheEntry> _stringCache = new Dictionary<Uri, StringCacheEntry>();

        // Re-use string value URLs within 2 minutes
        private static readonly TimeSpan stringCacheLifetime = new TimeSpan(0, 2, 0);

        public CachingHttpService(NetFileCache cache, bool overwrite = false)
        {
            _cache          = cache;
            _overwriteCache = overwrite;
        }

        public string DownloadModule(Metadata metadata)
        {
            try
            {
                return DownloadPackage(metadata.Download, metadata.Identifier, metadata.RemoteTimestamp);
            }
            catch (Exception exc)
            {
                var fallback = metadata.FallbackDownload;
                if (fallback == null)
                {
                    throw;
                }
                else
                {
                    return DownloadPackage(fallback, metadata.Identifier, metadata.RemoteTimestamp);
                }
            }
        }

        private string DownloadPackage(Uri url, string identifier, DateTime? updated)
        {
            if (_overwriteCache && !_requestedURLs.Contains(url))
            {
                // Discard cached file if command line says so,
                // but only the first time in each run
                _cache.Remove(url);
            }

            _requestedURLs.Add(url);

            var cachedFile = _cache.GetCachedFilename(url, updated);

            if (!string.IsNullOrWhiteSpace(cachedFile))
            {
                return cachedFile;
            }
            else
            {
                var downloadedFile = Net.Download(url);

                string extension;

                switch (FileIdentifier.IdentifyFile(downloadedFile))
                {
                    case FileType.ASCII:
                        extension = "txt";
                        break;
                    case FileType.GZip:
                        extension = "gz";
                        break;
                    case FileType.Tar:
                        extension = "tar";
                        break;
                    case FileType.TarGz:
                        extension = "tar.gz";
                        break;
                    case FileType.Zip:
                        extension = "zip";
                        string invalidReason;
                        if (!NetFileCache.ZipValid(downloadedFile, out invalidReason))
                        {
                            log.Debug($"{downloadedFile} is not a valid ZIP file: {invalidReason}");
                            throw new Kraken($"{url} is not a valid ZIP file: {invalidReason}");
                        }
                        break;
                    default:
                        extension = "ckan-package";
                        break;
                }

                return _cache.Store(
                    url,
                    downloadedFile,
                    string.Format("netkan-{0}.{1}", identifier, extension),
                    move: true
                );
            }
        }

        public string DownloadText(Uri url)
        {
            return TryGetCached(url, () => Net.DownloadText(url));
        }
        public string DownloadText(Uri url, string authToken, string mimeType = null)
        {
            return TryGetCached(url, () => Net.DownloadText(url, authToken, mimeType));
        }
        
        private string TryGetCached(Uri url, Func<string> uncached)
        {
            if (_stringCache.TryGetValue(url, out StringCacheEntry entry))
            {
                if (DateTime.Now - entry.Timestamp < stringCacheLifetime)
                {
                    // Re-use recent cached request of this URL
                    return entry.Value;
                }
                else
                {
                    // Too old, purge it
                    _stringCache.Remove(url);
                }
            }
            string val = uncached();
            _stringCache.Add(url, new StringCacheEntry()
            {
                Value     = val,
                Timestamp = DateTime.Now
            });
            return val;
        }

        public IEnumerable<Uri> RequestedURLs { get { return _requestedURLs; } }
        public void ClearRequestedURLs()
        {
            _requestedURLs?.Clear();
        }

        private static readonly ILog log = LogManager.GetLogger(typeof(CachingHttpService));
    }

    public class StringCacheEntry
    {
        public string   Value;
        public DateTime Timestamp;
    }
    
}
