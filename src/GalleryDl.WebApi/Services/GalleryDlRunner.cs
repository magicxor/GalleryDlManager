using System.Text;
using CliWrap;
using GalleryDl.WebApi.Options;
using Microsoft.Extensions.Options;

namespace GalleryDl.WebApi.Services;

public sealed record GalleryDlResult(
    int ExitCode,
    IReadOnlyList<string> Files,
    string WorkDir,
    string StdErrExcerpt,
    bool TimedOut = false);

/// <summary>Thrown when gallery-dl exceeds the configured timeout without downloading a single file.</summary>
public sealed class GalleryDlTimeoutException(int timeoutSeconds)
    : Exception($"gallery-dl did not download any files within {timeoutSeconds} seconds.");

public sealed class GalleryDlRunner(IOptions<GalleryDlOptions> options, ILogger<GalleryDlRunner> logger)
{
    private const int MaxStdErrExcerptLength = 2000;

    /// <summary>
    /// Runs gallery-dl for <paramref name="url"/> in a freshly created work directory and returns the
    /// downloaded file paths. The process is killed as soon as <paramref name="take"/> files are done:
    /// on "queue"-style extractors (search results that expand into albums) --range applies per album,
    /// so left alone gallery-dl would keep pulling one file from every album in the listing. On timeout
    /// the files downloaded so far are returned as a partial result (TimedOut = true); the timeout is
    /// only an error when nothing was downloaded at all.
    /// The caller owns the work directory and must delete it after use; it is only cleaned up here
    /// when the run throws.
    /// </summary>
    public async Task<GalleryDlResult> DownloadAsync(string url, int skip, int take, CancellationToken ct)
    {
        var o = options.Value;
        var workRoot = o.WorkRootPath ?? Path.Combine(Path.GetTempPath(), "gallery-dl-work");
        var workDir = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var args = new List<string>
        {
            "--range", $"{skip + 1}-{skip + take}",
            "--filter", $"extension in ({string.Join(", ", o.AllowedExtensions.Select(e => $"'{e}'"))})",
            "-D", workDir,
            "-o", "output.mode=pipe",
            "--no-input",
        };
        if (o.BlacklistTags.Count > 0)
        {
            args.Add("--tags-blacklist");
            args.Add(string.Join(",", o.BlacklistTags));
        }
        args.AddRange(o.ExtraArgs);
        args.Add(url);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

        var sync = new object();
        var fileLines = new List<string>();
        var earlyStopped = false;

        void OnStdOutLine(string line)
        {
            line = line.Trim();
            // output.mode=pipe: one path per downloaded file; skipped files are prefixed with "# ".
            if (line.Length == 0 || line.StartsWith("# "))
                return;

            lock (sync)
            {
                fileLines.Add(line);
                if (fileLines.Count >= take && !earlyStopped)
                {
                    // We have everything the caller asked for - stop gallery-dl instead of letting
                    // it walk the rest of the listing.
                    earlyStopped = true;
                    runCts.Cancel();
                }
            }
        }

        var stdErr = new StringBuilder();
        logger.LogInformation("Running {Executable} for {Url} in {WorkDir}", o.ExecutablePath, url, workDir);

        var exitCode = 0;
        var timedOut = false;
        try
        {
            var result = await Cli.Wrap(o.ExecutablePath)
                .WithArguments(args)
                .WithWorkingDirectory(workDir)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(OnStdOutLine))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
                .ExecuteAsync(runCts.Token);
            exitCode = result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                TryDeleteWorkDir(workDir);
                throw;
            }

            bool wasEarlyStop;
            lock (sync)
            {
                wasEarlyStop = earlyStopped;
            }

            if (!wasEarlyStop)
            {
                timedOut = true;
                if (CollectFiles().Count == 0)
                {
                    TryDeleteWorkDir(workDir);
                    throw new GalleryDlTimeoutException(o.TimeoutSeconds);
                }
            }
        }
        catch
        {
            TryDeleteWorkDir(workDir);
            throw;
        }

        var files = CollectFiles();

        // Safety net for extractors with unusual output, only on a clean run: trust the disk.
        if (files.Count == 0 && exitCode == 0 && !timedOut)
            files = Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories).Take(take).ToList();

        var err = stdErr.ToString();
        if (err.Length > MaxStdErrExcerptLength)
            err = err[^MaxStdErrExcerptLength..];

        logger.LogInformation("gallery-dl finished (exit {ExitCode}, timed out: {TimedOut}); {FileCount} file(s) downloaded",
            exitCode, timedOut, files.Count);

        return new GalleryDlResult(exitCode, files, workDir, err, timedOut);

        List<string> CollectFiles()
        {
            List<string> snapshot;
            lock (sync)
            {
                snapshot = [.. fileLines];
            }

            return snapshot
                .Select(line => Path.GetFullPath(line, workDir))
                .Where(path => path.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                               && File.Exists(path))
                .Distinct()
                .Take(take)
                .ToList();
        }
    }

    private void TryDeleteWorkDir(string workDir)
    {
        try
        {
            Directory.Delete(workDir, recursive: true);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to clean up work dir {WorkDir}", workDir);
        }
    }
}
