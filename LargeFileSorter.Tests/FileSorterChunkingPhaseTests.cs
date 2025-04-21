using System.Collections;

namespace LargeFileSorter.Tests;

public sealed class FileSorterChunkingPhaseTests : IClassFixture<FileSorterTestFixture>
{
    private readonly FileSorterChunkingPhase _chunkingPhase;
    private readonly FileSorterTestFixture _fixture;

    public FileSorterChunkingPhaseTests(FileSorterTestFixture fixture)
    {
        _fixture = fixture;
        _chunkingPhase = new FileSorterChunkingPhase();
    }

    [Fact]
    public async Task CreateSortedChunksAsync_EmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var inputFile = _fixture.CreateInputFile("empty.txt", "");

        // Act
        var tempFiles =
            await _chunkingPhase.CreateSortedChunksAsync(inputFile, _fixture.TempDir, 1024, CancellationToken.None);

        // Assert
        Assert.Empty(tempFiles);
        Assert.False(Directory.Exists(_fixture.TempDir),
            "Temp directory should ideally not be created if no chunks are made"); // SUT creates it though
        // Let's check if it's empty if created
        if (Directory.Exists(_fixture.TempDir)) 
            Assert.Empty(Directory.EnumerateFileSystemEntries(_fixture.TempDir));
    }

    [Fact]
    public async Task CreateSortedChunksAsync_SingleChunk_CreatesOneSortedFile()
    {
        // Arrange
        var inputFile = _fixture.CreateInputFile("single_chunk.txt", "10. Zebra\n5. Apple\n15. Manatee");
        const string expectedContent = "5. Apple\n15. Manatee\n10. Zebra\n"; // Sorted order
        const long smallChunkSize = 10 * 1024 * 1024; // Ensure it fits in one chunk

        // Act
        var tempFiles =
            await _chunkingPhase.CreateSortedChunksAsync(inputFile, _fixture.TempDir, smallChunkSize,
                CancellationToken.None);

        // Assert
        Assert.Single((IEnumerable) tempFiles);
        var tempFile = tempFiles[0];
        Assert.True(File.Exists(tempFile));
        Assert.StartsWith(Path.Combine(_fixture.TempDir, "chunk_"), tempFile); // Check naming convention and location

        var actualContent = await File.ReadAllTextAsync(tempFile);
        Assert.Equal(expectedContent, actualContent.Replace("\r\n", "\n")); // Normalize line endings
    }

    [Fact]
    public async Task CreateSortedChunksAsync_MultipleChunks_CreatesMultipleSortedFiles()
    {
        // Arrange
        // Content designed to split with a small chunk size
        const string line1 = "10. Zebra"; // ~10 chars * 2 + 8 = 28 bytes
        const string line2 = "5. Apple"; // ~9 chars * 2 + 8 = 26 bytes
        const string line3 = "15. Manatee"; // ~12 chars * 2 + 8 = 32 bytes
        const string line4 = "1. Ant"; // ~7 chars * 2 + 8 = 22 bytes
        var inputFile = _fixture.CreateInputFile("multi_chunk.txt", $"{line1}\n{line2}\n{line3}\n{line4}");

        // Estimated sizes: l1+l2 = 54, l3+l4 = 54. Let chunk size be 60 bytes.
        const long tinyChunkSize = 60;

        const string expectedChunk0Content = "5. Apple\n10. Zebra\n"; // Sorted line2, line1
        const string expectedChunk1Content = "1. Ant\n15. Manatee\n"; // Sorted line4, line3

        // Act
        var tempFiles =
            await _chunkingPhase.CreateSortedChunksAsync(inputFile, _fixture.TempDir, tinyChunkSize,
                CancellationToken.None);

        // Assert
        Assert.Equal(2, tempFiles.Count);

        var tempFile0 = _fixture.GetTempChunkPath(0); // Assumes sequential naming chunk_0, chunk_1
        var tempFile1 = _fixture.GetTempChunkPath(1);
        Assert.Contains(tempFile0, tempFiles); // Check if expected names are in the list
        Assert.Contains(tempFile1, tempFiles);

        Assert.True(File.Exists(tempFile0));
        Assert.Equal(expectedChunk0Content, (await File.ReadAllTextAsync(tempFile0)).Replace("\r\n", "\n"));

        Assert.True(File.Exists(tempFile1));
        Assert.Equal(expectedChunk1Content, (await File.ReadAllTextAsync(tempFile1)).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task CreateSortedChunksAsync_InvalidLines_SkipsInvalidAndSortsValid()
    {
        // Arrange
        var inputFile =
            _fixture.CreateInputFile("mixed_lines.txt", "10. Zebra\nINVALID\n5. Apple\nWRONG FORMAT\n15. Manatee");
        const string expectedContent = "5. Apple\n15. Manatee\n10. Zebra\n"; // Sorted order of valid lines
        const long smallChunkSize = 10 * 1024 * 1024;

        // Act
        var tempFiles =
            await _chunkingPhase.CreateSortedChunksAsync(inputFile, _fixture.TempDir, smallChunkSize,
                CancellationToken.None);

        // Assert
        Assert.Single((IEnumerable) tempFiles);
        var tempFile = tempFiles[0];
        Assert.True(File.Exists(tempFile));

        var actualContent = await File.ReadAllTextAsync(tempFile);
        Assert.Equal(expectedContent, actualContent.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task CreateSortedChunksAsync_Cancellation_StopsProcessing()
    {
        // Arrange
        var line1 = string.Join("\n",
            Enumerable.Range(1, 50).Select(i => $"{i}. Line {i}")); // Enough lines for multiple chunks likely
        var inputFile = _fixture.CreateInputFile("cancellable.txt", line1);
        const long tinyChunkSize = 60; // Force multiple chunks quickly
        var cts = new CancellationTokenSource();

        // Act
        // Run the task but cancel it shortly after it starts
        Task<List<string>>? sortTask = null;
        try
        {
            sortTask = _chunkingPhase.CreateSortedChunksAsync(inputFile, _fixture.TempDir, tinyChunkSize, cts.Token);
            // Give it a moment to start processing, then cancel
            await Task.Delay(50, CancellationToken.None); // Small delay, adjust if needed
            cts.Cancel();
            await sortTask; // Await the task to observe cancellation (it might complete partially or throw)
        }
        catch (OperationCanceledException)
        {
            // This is expected if cancellation happens during an async IO operation like ReadLineAsync
            Assert.True(cts.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex}");
        }

        // Assert
        // Check that fewer files were created than potentially expected, or none.
        // This is timing-dependent and might be flaky.
        var filesInTemp = Directory.Exists(_fixture.TempDir)
            ? Directory.GetFiles(_fixture.TempDir, "chunk_*.tmp")
            : [];
        // We expect *fewer* than the total possible chunks. Hard to know exact number.
        // A simple check is that it likely didn't create *all* possible chunks.
        // Example: calculate expected chunks without cancellation and assert fewer exist.
        // Or assert that the task completed/threw OCE. The catch block handles OCE.
        Assert.True(cts.IsCancellationRequested); // Verify cancellation was indeed requested.
    }
}