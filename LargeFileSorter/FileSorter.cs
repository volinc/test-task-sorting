using System.Diagnostics;
using LargeFileShared;

namespace LargeFileSorter;

public static class FileSorter
{
    public const long DefaultMaxChunkSizeInBytes = 1 * 1024 * 1024 * 1024; // 1 GB
    
    public static async Task SortFileAsync(
        string inputFile,
        string outputFile,
        string tempDirectory,
        long maxChunkSizeInBytes = DefaultMaxChunkSizeInBytes,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFile))
            throw new FileNotFoundException("Input file not found.", inputFile);
        if (string.IsNullOrWhiteSpace(outputFile))
            throw new ArgumentException("Output file path cannot be empty.", nameof(outputFile));
        if (string.IsNullOrWhiteSpace(tempDirectory))
            throw new ArgumentException("Temporary directory path cannot be empty.", nameof(tempDirectory));

        Directory.CreateDirectory(tempDirectory); // Ensure temp directory exists

        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine($"Starting sort for '{inputFile}'...");
        Console.WriteLine($"Using temp directory: '{tempDirectory}'");
        Console.WriteLine($"Max chunk size: {maxChunkSizeInBytes / (1024 * 1024)} MB");

        try
        {
            // Phase 1: Create sorted chunks
            Console.WriteLine("Phase 1: Creating sorted chunks...");
            var tempFiles = await CreateSortedChunksAsync(inputFile, tempDirectory, maxChunkSizeInBytes, cancellationToken);
            Console.WriteLine($"Phase 1 completed in {stopwatch.Elapsed}. Created {tempFiles.Count} chunk files.");

            if (cancellationToken.IsCancellationRequested) return;

            // Phase 2: Merge sorted chunks
            if (tempFiles.Any())
            {
                Console.WriteLine("Phase 2: Merging sorted chunks...");
                stopwatch.Restart();
                await MergeSortedChunksAsync(tempFiles, outputFile, cancellationToken);
                Console.WriteLine($"Phase 2 completed in {stopwatch.Elapsed}.");
            }
            else
            {
                // Handle empty input file case
                await File.WriteAllTextAsync(outputFile, string.Empty, cancellationToken);
                Console.WriteLine("Input file was empty or contained no valid lines. Created empty output file.");
            }

            Console.WriteLine($"Successfully sorted '{inputFile}' to '{outputFile}'.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Sort operation cancelled.");
            if (File.Exists(outputFile))
            {
                try
                {
                    File.Delete(outputFile);

                }
                catch
                {
                     // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"An error occurred during sorting: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            throw;
        }
        finally
        {
            Console.WriteLine("Cleaning up temporary files...");
            CleanupTempFiles(tempDirectory);
            Console.WriteLine("Cleanup complete.");
        }
    }

    /// <summary>
    /// Reads the input file, creates sorted chunks, and writes them to temporary files.
    /// </summary>
    private static async Task<List<string>> CreateSortedChunksAsync(
        string inputFile, string tempDirectory, long maxChunkSizeInBytes, CancellationToken cancellationToken)
    {
        var tempFiles = new List<string>();
        var currentChunk = new List<Line>();
        long currentChunkSize = 0;
        var chunkIndex = 0;

        // Use buffer for slightly better performance reading large files
        using var reader = new StreamReader(inputFile, new FileStreamOptions { BufferSize = 65536 }); // 64k buffer

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (Line.TryParse(line, out var entry))
            {
                currentChunk.Add(entry);
                // Estimate size (avoids precise encoding checks for performance)
                currentChunkSize += line.Length * sizeof(char) + IntPtr.Size; // Rough estimate including object overhead
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
        List<Line> chunk, string tempDirectory, int chunkIndex, List<string> tempFiles, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) 
            return;

        Console.WriteLine($"  Processing chunk {chunkIndex} ({chunk.Count} lines)...");

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
        Console.WriteLine($"    Writing sorted chunk {chunkIndex} to {tempFileName}...");

        // Use buffer for slightly better write performance
        await using (var writer = new StreamWriter(tempFileName, false, System.Text.Encoding.UTF8, 65536)) // 64k buffer
        {
            foreach (var entry in chunk)
            {
                // Write the original line back to preserve formatting
                await writer.WriteLineAsync(entry.RowValue.AsMemory(), cancellationToken);
            }
        }
        tempFiles.Add(tempFileName);
        Console.WriteLine($"    Finished writing chunk {chunkIndex}.");
        chunk.Clear(); // Free memory for the next chunk
        GC.Collect(); // Suggest GC after processing a large chunk
    }

    private static async Task MergeSortedChunksAsync(List<string> tempFiles, string outputFile, CancellationToken cancellationToken)
    {
        var readers = new List<StreamReader>(tempFiles.Count);
        var priorityQueue = new PriorityQueue<MergeState, Line>(tempFiles.Count); // Min-heap based on Line comparison

        try
        {
            // Open all temp files and read the first line from each
            foreach (var tempFile in tempFiles)
            {
                if (cancellationToken.IsCancellationRequested) return;
                var reader = new StreamReader(tempFile, new FileStreamOptions { BufferSize = 65536 });
                readers.Add(reader);
                string? firstLine = await reader.ReadLineAsync(cancellationToken);
                if (firstLine != null && Line.TryParse(firstLine, out var entry))
                {
                    priorityQueue.Enqueue(new MergeState(reader, entry), entry);
                }
                else if (firstLine == null)
                {
                    // Empty chunk file? Close reader immediately.
                    reader.Dispose();
                }
                // else: parse error on first line (warning printed by TryParse) - skip this reader? Or handle error?
                // For robustness, let's assume valid lines in chunks or handle empty chunks.
            }

            // Open the output file writer
            await using var writer = new StreamWriter(outputFile, false, System.Text.Encoding.UTF8, 65536); // 64k buffer

            // Merge loop
            while (priorityQueue.TryDequeue(out MergeState state, out Line _)) // We only care about the state
            {
                if (cancellationToken.IsCancellationRequested) return;
                // Write the smallest line (from the dequeued state) to the output
                await writer.WriteLineAsync(state.CurrentLine.RowValue.AsMemory(), cancellationToken);

                // Read the next line from the *same* reader
                string? nextLine = await state.Reader.ReadLineAsync(cancellationToken);
                if (nextLine != null && Line.TryParse(nextLine, out var nextEntry))
                {
                    // Enqueue the next line from this reader
                    priorityQueue.Enqueue(new MergeState(state.Reader, nextEntry), nextEntry);
                }
                else
                {
                    // End of this reader or parsing error on subsequent line
                    state.Reader.Dispose(); // Close the exhausted reader
                }
                // If TryParse failed, a warning was printed. The reader is implicitly dropped from the merge.
            }
        }
        finally
        {
            // Ensure all readers are disposed even if an error occurs
            foreach (var reader in readers)
            {
                try { reader.Dispose(); } catch { /* Ignore dispose errors */ }
            }
        }
    }

    /// <summary>
    /// Helper class for the merge priority queue.
    /// </summary>
    private readonly struct MergeState(StreamReader reader, Line line)
    {
        public StreamReader Reader { get; } = reader;
        public Line CurrentLine { get; } = line;
    }

    /// <summary>
    /// Cleans up temporary files in the specified directory.
    /// </summary>
    private static void CleanupTempFiles(string tempDirectory)
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
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
                
                if (!tempDirInfo.EnumerateFileSystemInfos().Any())
                {
                    try { tempDirInfo.Delete(); } catch { /* Ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Error during temp file cleanup: {ex.Message}");
        }
    }
}