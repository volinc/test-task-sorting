namespace LargeFileSorter;

public interface IFileSorterExternalMergePhase
{
    Task MergeSortedChunksAsync(List<string> tempFiles, string outputFilePath, 
        CancellationToken cancellationToken);
}