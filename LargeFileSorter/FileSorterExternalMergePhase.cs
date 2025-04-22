using LargeFileShared;

namespace LargeFileSorter;

public sealed class FileSorterExternalMergePhase : IFileSorterExternalMergePhase
{
    public async Task MergeSortedChunksAsync(List<string> tempFilePaths, string outputFilePath, 
        CancellationToken cancellationToken)
    {   
        var readers = new List<StreamReader>(tempFilePaths.Count);
        var priorityQueue = new PriorityQueue<MergeState, Line>(tempFilePaths.Count); // Min-heap based on Line comparison

        try
        {
            // Open all temp files and read the first line from each
            foreach (var tempFile in tempFilePaths)
            {
                if (cancellationToken.IsCancellationRequested) 
                    return;
                
                var reader = new StreamReader(tempFile, new FileStreamOptions { BufferSize = 65536 });
                readers.Add(reader);
                var firstLineRawValue = await reader.ReadLineAsync(cancellationToken);
                if (firstLineRawValue != null && Line.TryParse(firstLineRawValue, out var line))
                {
                    priorityQueue.Enqueue(new MergeState(reader, line), line);
                }
                else if (firstLineRawValue == null)
                {
                    // Empty chunk file? Close reader immediately.
                    reader.Dispose();
                }
                // else: parse error on first line (warning printed by TryParse) - skip this reader? Or handle error?
                // For robustness, let's assume valid lines in chunks or handle empty chunks.
            }

            // Open the output file writer
            await using var writer = new StreamWriter(outputFilePath, false, System.Text.Encoding.UTF8, 65536); // 64k buffer

            // Merge loop
            while (priorityQueue.TryDequeue(out var state, out _)) // We only care about the state
            {
                if (cancellationToken.IsCancellationRequested) 
                    return;
                
                // Write the smallest line (from the dequeued state) to the output
                await writer.WriteLineAsync(state.CurrentLine.RawValue.AsMemory(), cancellationToken);

                // Read the next line from the *same* reader
                var nextLine = await state.Reader.ReadLineAsync(cancellationToken);
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
                try
                {
                    reader.Dispose();
                }
                catch
                {
                    // Ignore dispose errors
                }
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
}