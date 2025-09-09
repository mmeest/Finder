using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinFormsFileSearcher
{
    public class SearchOptions
    {
        public string RootPath { get; set; }
        public string NameFilter { get; set; } // supports simple wildcard like *report*
        public List<string> Extensions { get; set; } // list of .ext strings (lowercase) or null
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public bool IncludeSubdirectories { get; set; } = true;
        public string ContentSearch { get; set; }
    }

    public class SearchResult
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Extension { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string Attributes { get; set; }
        public string PreviewSnippet { get; set; }
    }

    public class SearchProgress
    {
        public long TotalFiles { get; set; }
        public long ProcessedFiles { get; set; }
        public string Message { get; set; }
    }

    public static class SearchEngine
    {
        // Non-indexed fast search using parallel enumeration and streaming content checks.
        public static async Task<List<SearchResult>> SearchAsync(SearchOptions options, IProgress<SearchProgress> progress, CancellationToken token)
        {
            var results = new ConcurrentBag<SearchResult>();
            var processed = 0L;
            var toProcess = new ConcurrentQueue<string>();

            // Pre-calc possible total? Not reliably fast; we'll report as we go.
            progress?.Report(new SearchProgress { TotalFiles = 0, ProcessedFiles = 0, Message = "Enumerating..." });

            // Safe enumerator that continues on access denied
            IEnumerable<string> EnumerateAllFiles(string root)
            {
                var dirs = new Stack<string>();
                dirs.Push(root);
                while (dirs.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var current = dirs.Pop();
                    string[] subDirs = null;
                    try
                    {
                        subDirs = Directory.GetDirectories(current);
                    }
                    catch { subDirs = Array.Empty<string>(); }

                    string[] files = null;
                    try
                    {
                        files = Directory.GetFiles(current);
                    }
                    catch { files = Array.Empty<string>(); }

                    foreach (var file in files)
                    {
                        yield return file;
                    }

                    if (options.IncludeSubdirectories)
                    {
                        foreach (var d in subDirs)
                            dirs.Push(d);
                    }
                }
            }

            // Build filters
            Func<FileInfo, bool> metadataFilter = fi =>
            {
                if (options.Extensions != null && options.Extensions.Count > 0)
                {
                    if (!options.Extensions.Contains(fi.Extension.ToLowerInvariant())) return false;
                }
                if (options.DateFrom.HasValue && fi.LastWriteTime < options.DateFrom.Value) return false;
                if (options.DateTo.HasValue && fi.LastWriteTime > options.DateTo.Value) return false;
                if (!string.IsNullOrWhiteSpace(options.NameFilter))
                {
                    // simple wildcard * and ? support
                    var pattern = options.NameFilter;
                    try
                    {
                        if (!MatchesWildcard(fi.Name, pattern)) return false;
                    }
                    catch { return false; }
                }
                return true;
            };

            // Enumerate and push to queue
            var enumerated = Task.Run(() =>
            {
                try
                {
                    foreach (var file in EnumerateAllFiles(options.RootPath))
                    {
                        token.ThrowIfCancellationRequested();
                        toProcess.Enqueue(file);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);

            // Start worker tasks
            var workers = new List<Task>();
            var maxParallel = Math.Max(1, Environment.ProcessorCount * 2);

            for (int i = 0; i < maxParallel; i++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (!toProcess.TryDequeue(out var file))
                        {
                            if (enumerated.IsCompleted) break;
                            await Task.Delay(50, token).ConfigureAwait(false);
                            continue;
                        }

                        Interlocked.Increment(ref processed);
                        progress?.Report(new SearchProgress { TotalFiles = 0, ProcessedFiles = processed, Message = $"Processed {processed} files" });

                        try
                        {
                            var fi = new FileInfo(file);
                            if (!fi.Exists) continue;

                            if (!metadataFilter(fi)) continue;

                            bool contentMatched = true; // default if no content search
                            string snippet = null;
                            if (!string.IsNullOrWhiteSpace(options.ContentSearch))
                            {
                                // Quick heuristic: check if file is likely text
                                if (await IsTextFileAsync(fi.FullName, token).ConfigureAwait(false))
                                {
                                    var match = await FindInFileAsync(fi.FullName, options.ContentSearch, token).ConfigureAwait(false);
                                    contentMatched = match != null;
                                    snippet = match;
                                }
                                else contentMatched = false;
                            }

                            if (contentMatched)
                            {
                                var r = new SearchResult
                                {
                                    Name = fi.Name,
                                    Path = fi.FullName,
                                    Extension = fi.Extension,
                                    Size = fi.Length,
                                    Modified = fi.LastWriteTime,
                                    Attributes = fi.Attributes.ToString(),
                                    PreviewSnippet = snippet
                                };
                                results.Add(r);
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch { /* ignore per-file errors */ }
                    }
                }, token));
            }

            await enumerated.ConfigureAwait(false);
            await Task.WhenAll(workers).ConfigureAwait(false);

            return results.OrderBy(r => r.Name).ToList();
        }

        // Simple wildcard matcher supporting * and ?
        private static bool MatchesWildcard(string text, string pattern)
        {
            // Convert wildcard to regex
            var esc = System.Text.RegularExpressions.Regex.Escape(pattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".");
            return System.Text.RegularExpressions.Regex.IsMatch(text, "^" + esc + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static async Task<bool> IsTextFileAsync(string path, CancellationToken token)
        {
            const int sampleSize = 512;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
                {
                    var buffer = new byte[sampleSize];
                    var read = await fs.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read == 0) return true; // empty file treat as text
                    int suspicious = 0;
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == 0) { suspicious++; break; }
                    }
                    return suspicious == 0;
                }
            }
            catch { return false; }
        }

        private static async Task<string> FindInFileAsync(string path, string query, CancellationToken token)
        {
            // Search case-insensitive, return a short snippet if found
            var q = query;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, useAsync: true))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                {
                    string line;
                    long lineno = 0;
                    while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        token.ThrowIfCancellationRequested();
                        lineno++;
                        if (line.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var idx = line.IndexOf(q, StringComparison.OrdinalIgnoreCase);
                            var start = Math.Max(0, idx - 40);
                            var len = Math.Min(line.Length - start, 160);
                            return $"...{line.Substring(start, len)}...";
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}