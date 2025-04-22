namespace LargeFileSorter;

public interface IFileSorterChunkingPhase
{
    /// <summary>
    /// Reads the input file, creates sorted chunks, and writes them to temporary files.
    /// </summary>
    Task<List<string>> CreateSortedChunksAsync(string inputFilePath, string tempDirectory, long maxChunkSizeInBytes,
        CancellationToken cancellationToken);
}