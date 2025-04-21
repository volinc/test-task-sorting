using LargeFileShared;

namespace LargeFileSorter;

public sealed class FileSorterChunkingPhase : IFileSorterChunkingPhase
{
    public async Task<List<string>> CreateSortedChunksAsync(string inputFile, string tempDirectory,
        long maxChunkSizeInBytes, CancellationToken cancellationToken)
    {
        var tempFiles = new List<string>();
        var currentChunk = new List<Line>();
        long currentChunkSize = 0;
        var chunkIndex = 0;

        // Use buffer for slightly better performance reading large files
        using var reader = new StreamReader(inputFile, new FileStreamOptions {BufferSize = 65536}); // 64k buffer

        string? lineRawValue;
        while ((lineRawValue = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (Line.TryParse(lineRawValue, out var line))
            {
                currentChunk.Add(line);
                // Estimate size (avoids precise encoding checks for performance)
                currentChunkSize +=
                    lineRawValue.Length * sizeof(char) + IntPtr.Size; // Rough estimate including object overhead
            }

            if (currentChunkSize >= maxChunkSizeInBytes)
            {
                await ProcessChunkAsync(currentChunk, tempDirectory, chunkIndex++, tempFiles, cancellationToken);
                currentChunkSize = 0; // Reset for next chunk
            }
        }

        // Process the last chunk if it has data
        if (currentChunk.Count > 0)
        {
            await ProcessChunkAsync(currentChunk, tempDirectory, chunkIndex, tempFiles, cancellationToken);
        }

        return tempFiles;
    }

    /// <summary>
    /// Sorts a chunk and writes it to a temporary file.
    /// </summary>
    private static async Task ProcessChunkAsync(
        List<Line> chunk, string tempDirectory, int chunkIndex, List<string> tempFiles,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        await Console.Out.WriteLineAsync($"  Processing chunk {chunkIndex} ({chunk.Count} lines)...");

        // --- Potential Parallelization Point ---
        // Sorting is CPU-bound. We can offload it to a thread pool thread
        // to avoid blocking the async I/O thread if needed, especially if
        // chunk processing becomes a bottleneck.
        // For simplicity now, we sort synchronously within the async method.
        // To parallelize: await Task.Run(() => chunk.Sort(), cancellationToken);
        chunk.Sort(); // Uses Line.CompareTo
        if (cancellationToken.IsCancellationRequested)
            return;

        var tempFileName = Path.Combine(tempDirectory, $"chunk_{chunkIndex}.tmp");
        await Console.Out.WriteLineAsync($"    Writing sorted chunk {chunkIndex} to {tempFileName}...");

        // Use buffer for slightly better write performance
        await using (var writer = new StreamWriter(tempFileName, false, System.Text.Encoding.UTF8, 65536)) // 64k buffer
        {
            foreach (var line in chunk)
            {
                // Write the original line back to preserve formatting
                await writer.WriteLineAsync(line.RawValue.AsMemory(), cancellationToken);
            }
        }

        tempFiles.Add(tempFileName);
        await Console.Out.WriteLineAsync($"    Finished writing chunk {chunkIndex}.");
        chunk.Clear(); // Free memory for the next chunk
        GC.Collect(); // Suggest GC after processing a large chunk
    }
}