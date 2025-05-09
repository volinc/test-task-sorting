using System.Text;

namespace LargeFileSorter.Tests;

public sealed class FileSorterExternalMergePhaseTests : IClassFixture<FileSorterTestFixture>
{
    private readonly FileSorterTestFixture _fixture;
    private readonly FileSorterExternalMergePhase _mergePhase;

    public FileSorterExternalMergePhaseTests(FileSorterTestFixture fixture)
    {
        _fixture = fixture;
        _mergePhase = new FileSorterExternalMergePhase();
        // Ensure temp dir exists for placing mock chunk files
        Directory.CreateDirectory(_fixture.TempDir);
    }

    private string CreateTempChunkFile(string name, string content)
    {
        // Create files directly inside the TempDir for merge tests
        var path = Path.Combine(_fixture.TempDir, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }


    [Fact]
    public async Task MergeSortedChunksAsync_EmptyInputList_CreatesEmptyOutputFile()
    {
        // Arrange
        var emptyList = new List<string>();
        var outputFilePath = _fixture.GetOutputPath("empty_merge_output.txt");

        // Act
        await _mergePhase.MergeSortedChunksAsync(emptyList, outputFilePath, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(outputFilePath));
        var content = await File.ReadAllTextAsync(outputFilePath);
        Assert.Equal(0, content.Length);
    }

    [Fact]
    public async Task MergeSortedChunksAsync_SingleChunk_CopiesContentToOutput()
    {
        // Arrange
        const string chunkContent = "1. Apple\n5. Banana\n10. Cherry\n";
        var chunkFile = CreateTempChunkFile("chunk_0.tmp", chunkContent);
        var inputFiles = new List<string> {chunkFile};
        var outputFilePath = _fixture.GetOutputPath("single_merge_output.txt");

        // Act
        await _mergePhase.MergeSortedChunksAsync(inputFiles, outputFilePath, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(outputFilePath));
        var actualOutput = await File.ReadAllTextAsync(outputFilePath);
        Assert.Equal(chunkContent, actualOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task MergeSortedChunksAsync_MultipleChunks_MergesSortedCorrectly()
    {
        // Arrange
        const string chunk0Content = "5. Apple\n15. Manatee\n";
        const string chunk1Content = "1. Ant\n99. Zebra\n";
        const string chunk2Content = "10. Cherry\n20. Orange\n";

        var chunkFile0 = CreateTempChunkFile("chunk_0.tmp", chunk0Content);
        var chunkFile1 = CreateTempChunkFile("chunk_1.tmp", chunk1Content);
        var chunkFile2 = CreateTempChunkFile("chunk_2.tmp", chunk2Content);

        var inputFiles = new List<string> {chunkFile0, chunkFile1, chunkFile2};
        var outputFilePath = _fixture.GetOutputPath("multi_merge_output.txt");

        const string expectedOutputContent = "1. Ant\n5. Apple\n10. Cherry\n15. Manatee\n20. Orange\n99. Zebra\n";

        // Act
        await _mergePhase.MergeSortedChunksAsync(inputFiles, outputFilePath, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(outputFilePath));
        var actualOutput = await File.ReadAllTextAsync(outputFilePath);
        Assert.Equal(expectedOutputContent, actualOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task MergeSortedChunksAsync_OneChunkIsEmpty_MergesOthersCorrectly()
    {
        // Arrange
        const string chunk0Content = "5. Apple\n15. Manatee\n";
        const string chunk1Content = ""; // Empty chunk
        const string chunk2Content = "10. Cherry\n20. Orange\n";

        var chunkFile0 = CreateTempChunkFile("chunk_0.tmp", chunk0Content);
        var chunkFile1 = CreateTempChunkFile("chunk_1_empty.tmp", chunk1Content);
        var chunkFile2 = CreateTempChunkFile("chunk_2.tmp", chunk2Content);

        var inputFilePaths = new List<string> {chunkFile0, chunkFile1, chunkFile2};
        var outputFilePath = _fixture.GetOutputPath("empty_chunk_merge_output.txt");

        const string
            expectedOutputContent =
                "5. Apple\n10. Cherry\n15. Manatee\n20. Orange\n"; // Sorted order without empty chunk data

        // Act
        await _mergePhase.MergeSortedChunksAsync(inputFilePaths, outputFilePath, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(outputFilePath));
        var actualOutput = await File.ReadAllTextAsync(outputFilePath);
        Assert.Equal(expectedOutputContent, actualOutput.Replace("\r\n", "\n"));
    }
}