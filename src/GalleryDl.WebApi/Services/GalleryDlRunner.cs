using System.Text;
using CliWrap;
using GalleryDl.WebApi.Options;
using Microsoft.Extensions.Options;

namespace GalleryDl.WebApi.Services;

public sealed record GalleryDlResult(int ExitCode, IReadOnlyList<string> Files, string WorkDir, string StdErrExcerpt);

/// <summary>Thrown when gallery-dl exceeds the configured timeout (as opposed to the client aborting).</summary>
public sealed class GalleryDlTimeoutException(int timeoutSeconds)
    : Exception($"gallery-dl did not finish within {timeoutSeconds} seconds.");

public sealed class GalleryDlRunner(IOptions<GalleryDlOptions> options, ILogger<GalleryDlRunner> logger)
{
    private const int MaxStdErrExcerptLength = 2000;

    /// <summary>
    /// Runs gallery-dl for <paramref name="url"/> in a freshly created work directory and returns the
    /// downloaded file paths. The caller owns the work directory and must delete it after use;
    /// it is only cleaned up here when the run throws.
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

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        logger.LogInformation("Running {Executable} for {Url} in {WorkDir}", o.ExecutablePath, url, workDir);

        CommandResult result;
        try
        {
            result = await Cli.Wrap(o.ExecutablePath)
                .WithArguments(args)
                .WithWorkingDirectory(workDir)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
                .ExecuteAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryDeleteWorkDir(workDir);
            throw new GalleryDlTimeoutException(o.TimeoutSeconds);
        }
        catch
        {
            TryDeleteWorkDir(workDir);
            throw;
        }

        // stdout (output.mode=pipe): one path per downloaded file; skipped files are prefixed with "# ".
        var files = stdOut.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("# "))
            .Select(line => Path.GetFullPath(line, workDir))
            .Where(path => path.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                           && File.Exists(path))
            .Distinct()
            .ToList();

        // Safety net for extractors with unusual output: trust what is actually on disk.
        if (files.Count == 0)
            files = Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories).ToList();

        var err = stdErr.ToString();
        if (err.Length > MaxStdErrExcerptLength)
            err = err[^MaxStdErrExcerptLength..];

        logger.LogInformation("gallery-dl exited with {ExitCode}; {FileCount} file(s) downloaded", result.ExitCode, files.Count);

        return new GalleryDlResult(result.ExitCode, files, workDir, err);
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
