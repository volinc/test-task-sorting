using System.Diagnostics;

namespace LargeFileSorter;

public sealed class FileSorter
{
    public const long DefaultMaxChunkSizeInBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    
    private readonly IFileSorterChunkingPhase _chunkingPhase;
    private readonly IFileSorterExternalMergePhase _externalMergePhase;

    public FileSorter(
        IFileSorterChunkingPhase chunkingPhase,
        IFileSorterExternalMergePhase externalMergePhase)
    {
        _chunkingPhase = chunkingPhase ?? throw new ArgumentNullException(nameof(chunkingPhase));
        _externalMergePhase = externalMergePhase ?? throw new ArgumentNullException(nameof(externalMergePhase));
    }
    
    public async Task SortAsync(
        string inputFilePath,
        string outputFilePath,
        string tempDirectory,
        long maxChunkSizeInBytes = DefaultMaxChunkSizeInBytes,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException("Input file not found.", inputFilePath);
        if (string.IsNullOrWhiteSpace(outputFilePath))
            throw new ArgumentException("Output file path cannot be empty.", nameof(outputFilePath));
        if (string.IsNullOrWhiteSpace(tempDirectory))
            throw new ArgumentException("Temporary directory path cannot be empty.", nameof(tempDirectory));

        Directory.CreateDirectory(tempDirectory); // Create a temporary directory if it does not exist

        var stopwatch = Stopwatch.StartNew();
        await Console.Out.WriteLineAsync($"Starting sort for '{inputFilePath}'...");
        await Console.Out.WriteLineAsync($"Using temp directory: '{tempDirectory}'");
        await Console.Out.WriteLineAsync($"Max chunk size: {maxChunkSizeInBytes / (1024 * 1024)} MB");

        try
        {
            // Phase 1: Create sorted chunks
            await Console.Out.WriteLineAsync("Phase 1: Creating sorted chunks...");
            var tempFiles = await _chunkingPhase.CreateSortedChunksAsync(inputFilePath, tempDirectory, maxChunkSizeInBytes, cancellationToken);
            await Console.Out.WriteLineAsync($"Phase 1 completed in {stopwatch.Elapsed}. Created {tempFiles.Count} chunk files.");

            if (cancellationToken.IsCancellationRequested) 
                return;

            // Phase 2: Merge sorted chunks
            if (tempFiles.Any())
            {
                await Console.Out.WriteLineAsync("Phase 2: Merging sorted chunks...");
                stopwatch.Restart();
                await _externalMergePhase.MergeSortedChunksAsync(tempFiles, outputFilePath, cancellationToken);
                await Console.Out.WriteLineAsync($"Phase 2 completed in {stopwatch.Elapsed}.");
            }
            else
            {
                // Handle empty input file case
                await File.WriteAllTextAsync(outputFilePath, string.Empty, cancellationToken);
                await Console.Out.WriteLineAsync("Input file was empty or contained no valid lines. Created empty output file.");
            }

            await Console.Out.WriteLineAsync($"Successfully sorted '{inputFilePath}' to '{outputFilePath}'.");
        }
        catch (OperationCanceledException)
        {
            await Console.Out.WriteLineAsync("Sort operation cancelled.");
            if (File.Exists(outputFilePath))
            {
                try
                {
                    File.Delete(outputFilePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"An error occurred during sorting: {ex.Message}");
            await Console.Error.WriteLineAsync(ex.StackTrace);
            throw;
        }
        finally
        {
            await Console.Out.WriteLineAsync("Cleaning up temporary files...");
            CleanupTempFiles(tempDirectory);
            await Console.Out.WriteLineAsync("Cleanup complete.");
        }
    }

    private static void CleanupTempFiles(string tempDirectory)
    {
        try
        {
            if (!Directory.Exists(tempDirectory)) 
                return;
            
            var tempDirInfo = new DirectoryInfo(tempDirectory);
            foreach (var file in tempDirInfo.GetFiles("chunk_*.tmp"))
            {
                try
                {
                    file.Delete();
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"Warning: Could not delete temp file '{file.FullName}': {ex.Message}");
                }
            }

            if (tempDirInfo.EnumerateFileSystemInfos().Any()) 
                return;

            try
            {
                tempDirInfo.Delete();
            } 
            catch 
            { 
                // Ignore
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Error during temp file cleanup: {ex.Message}");
        }
    }
}