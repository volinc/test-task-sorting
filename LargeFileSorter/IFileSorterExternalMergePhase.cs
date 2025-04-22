namespace LargeFileSorter;

public interface IFileSorterExternalMergePhase
{
    Task MergeSortedChunksAsync(List<string> tempFilePaths, string outputFilePath, 
        CancellationToken cancellationToken);
}