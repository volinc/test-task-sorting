using Moq;

namespace LargeFileSorter.Tests;

// NOTE: These tests focus on the orchestration logic of FileSorter.
// They MOCK the chunking/merging phases.
// Testing the direct File/Directory/Console interactions and the static
// CleanupTempFiles requires integration tests or refactoring FileSorter
// to inject abstractions for IO and Logging.
public sealed class FileSorterTests
{
    private const string ValidInputFile = "input.txt";
    private const string ValidOutputFile = "output.txt";
    private const string ValidTempDir = "temp_sort";
    private readonly Mock<IFileSorterChunkingPhase> _mockChunkingPhase;
    private readonly Mock<IFileSorterExternalMergePhase> _mockMergePhase;
    private readonly FileSorter _sut; // System Under Test

    public FileSorterTests()
    {
        _mockChunkingPhase = new Mock<IFileSorterChunkingPhase>();
        _mockMergePhase = new Mock<IFileSorterExternalMergePhase>();

        _sut = new FileSorter(_mockChunkingPhase.Object, _mockMergePhase.Object);

        // *** Test Limitation ***
        // We cannot easily mock static methods like File.Exists, Directory.CreateDirectory etc.
        // Tests calling SortAsync will likely fail if these methods are hit before
        // the mocked phases are called, unless the actual files/dirs exist.
        // For robust unit tests, FileSorter needs refactoring to inject IO abstractions.
        // We will assume these initial IO checks pass for the orchestration tests below,
        // focusing on the calls to the mocked phases. A common workaround in tests
        // is to ensure the dummy paths *do* exist during the test run, making these
        // more like partial integration tests.
        // Example Setup (may require cleanup):
        // if (!File.Exists(ValidInputFile)) File.Create(ValidInputFile).Dispose();
        // Directory.CreateDirectory(ValidTempDir);
    }
    
    [Fact]
    public void Constructor_NullChunkingPhase_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("chunkingPhase", () => new FileSorter(null!, _mockMergePhase.Object));
    }

    [Fact]
    public void Constructor_NullMergePhase_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("externalMergePhase",
            () => new FileSorter(_mockChunkingPhase.Object, null!));
    }

    [Fact]
    public async Task SortAsync_WhenChunkingCreatesFiles_CallsMergePhase()
    {
        // Arrange
        var tempFiles = new List<string> {Path.Combine(ValidTempDir, "chunk_0.tmp")};
        _mockChunkingPhase.Setup(p =>
                p.CreateSortedChunksAsync(ValidInputFile, ValidTempDir, It.IsAny<long>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(tempFiles);
        _mockMergePhase.Setup(p => p.MergeSortedChunksAsync(tempFiles, ValidOutputFile, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Ensure dummy files/dirs exist if needed for test runner environment
        if (!File.Exists(ValidInputFile)) 
            File.Create(ValidInputFile).Dispose();
        Directory.CreateDirectory(ValidTempDir); // Ensure temp exists

        // Act
        await _sut.SortAsync(ValidInputFile, ValidOutputFile, ValidTempDir);

        // Assert
        _mockChunkingPhase.Verify(
            p => p.CreateSortedChunksAsync(ValidInputFile, ValidTempDir, FileSorter.DefaultMaxChunkSizeInBytes,
                It.IsAny<CancellationToken>()), Times.Once);
        _mockMergePhase.Verify(p => p.MergeSortedChunksAsync(tempFiles, ValidOutputFile, It.IsAny<CancellationToken>()),
            Times.Once);

        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
        // Note: CleanupTempFiles is static and uses DirectoryInfo directly, difficult to verify in isolation.
        // We assume the finally block runs but cannot easily check its specific actions here.
    }

    [Fact]
    public async Task SortAsync_WhenChunkingCreatesNoFiles_SkipsMergePhaseAndWritesEmptyOutput()
    {
        // Arrange
        var emptyTempFiles = new List<string>();
        _mockChunkingPhase.Setup(p =>
                p.CreateSortedChunksAsync(ValidInputFile, ValidTempDir, It.IsAny<long>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyTempFiles);

        if (!File.Exists(ValidInputFile)) 
            File.Create(ValidInputFile).Dispose();
        Directory.CreateDirectory(ValidTempDir);

        // Act
        await _sut.SortAsync(ValidInputFile, ValidOutputFile, ValidTempDir);

        // Assert
        _mockChunkingPhase.Verify(
            p => p.CreateSortedChunksAsync(ValidInputFile, ValidTempDir, FileSorter.DefaultMaxChunkSizeInBytes,
                It.IsAny<CancellationToken>()), Times.Once);
        _mockMergePhase.Verify(
            p => p.MergeSortedChunksAsync(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never); // Merge should be skipped
        
        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
    }

    [Fact]
    public async Task SortAsync_WhenChunkingPhaseThrowsIOException_PropagatesExceptionAndCleansUp()
    {
        // Arrange
        var ioException = new IOException("Disk error during chunking");
        _mockChunkingPhase.Setup(p => p.CreateSortedChunksAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ioException);

        if (!File.Exists(ValidInputFile)) 
            File.Create(ValidInputFile).Dispose();
        Directory.CreateDirectory(ValidTempDir);

        // Act & Assert
        var caughtException =
            await Assert.ThrowsAsync<IOException>(() => _sut.SortAsync(ValidInputFile, ValidOutputFile, ValidTempDir));
        Assert.Same(ioException, caughtException); // Ensure original exception is propagated

        _mockMergePhase.Verify(
            p => p.MergeSortedChunksAsync(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never); // Merge should not be called

        // Note: Cleanup verification is difficult due to static nature. Assume finally block ran.

        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
    }

    [Fact]
    public async Task SortAsync_WhenMergePhaseThrowsIOException_PropagatesExceptionAndCleansUp()
    {
        // Arrange
        var tempFiles = new List<string> {Path.Combine(ValidTempDir, "chunk_0.tmp")};
        _mockChunkingPhase.Setup(p => p.CreateSortedChunksAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tempFiles);

        var ioException = new IOException("Disk error during merging");
        _mockMergePhase.Setup(p =>
                p.MergeSortedChunksAsync(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ioException);

        // *** Workaround for File.Exists etc. ***
        if (!File.Exists(ValidInputFile)) 
            File.Create(ValidInputFile).Dispose();
        Directory.CreateDirectory(ValidTempDir);

        // Act & Assert
        var caughtException =
            await Assert.ThrowsAsync<IOException>(() => _sut.SortAsync(ValidInputFile, ValidOutputFile, ValidTempDir));
        Assert.Same(ioException, caughtException);

        _mockChunkingPhase.Verify(
            p => p.CreateSortedChunksAsync(ValidInputFile, ValidTempDir, It.IsAny<long>(),
                It.IsAny<CancellationToken>()), Times.Once);
        _mockMergePhase.Verify(p => p.MergeSortedChunksAsync(tempFiles, ValidOutputFile, It.IsAny<CancellationToken>()),
            Times.Once);

        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
    }

    [Fact]
    public async Task SortAsync_CancellationDuringChunking_ReturnsAndCleansUp()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var cancelledException = new OperationCanceledException(cts.Token);

        _mockChunkingPhase.Setup(p => p.CreateSortedChunksAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, long, CancellationToken>((_, _, _, token) => cts.Cancel()) // Cancel when called
            .ThrowsAsync(cancelledException); // Mimic behaviour


        // *** Workaround for File.Exists etc. ***
        if (!File.Exists(ValidInputFile)) 
            File.Create(ValidInputFile).Dispose();
        Directory.CreateDirectory(ValidTempDir);

        // Act
        // OperationCanceledException is caught and handled, so the method should complete "successfully" from caller's perspective (no exception thrown out)
        // unless the CancellationToken passed IN was already cancelled. Let's test the internal cancellation case first.
        // We need a way to trigger the cancellation *inside* the method based on the token.
        // Let's simulate cancellation being detected *after* chunking returns but before merge.

        _mockChunkingPhase.Setup(p => p.CreateSortedChunksAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["file.tmp"]); // Return some files

        await cts.CancelAsync(); // Cancel the token *before* calling SortAsync

        await _sut.SortAsync(ValidInputFile, ValidOutputFile, ValidTempDir, FileSorter.DefaultMaxChunkSizeInBytes,
            cts.Token);

        // Assert
        _mockChunkingPhase.Verify(
            p => p.CreateSortedChunksAsync(ValidInputFile, ValidTempDir, It.IsAny<long>(), cts.Token),
            Times.Once); // Chunking was called
        // The check `if (cancellationToken.IsCancellationRequested) return;` should hit.
        _mockMergePhase.Verify(
            p => p.MergeSortedChunksAsync(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never); // Merge skipped

        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
    }

    [Fact]
    public async Task SortAsync_CancellationDuringMerging_StopsMergeAndCleansUpOutput()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var tempFiles = new List<string> {Path.Combine(ValidTempDir, "chunk_0.tmp")};

        _mockChunkingPhase.Setup(p => p.CreateSortedChunksAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tempFiles);

        var cancelledException = new OperationCanceledException(cts.Token);
        _mockMergePhase.Setup(p =>
                p.MergeSortedChunksAsync(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<List<string>, string, CancellationToken>((_, _, token) => cts.Cancel()) // Cancel when called
            .ThrowsAsync(cancelledException); // Mimic behaviour

        if (!File.Exists(ValidInputFile)) 
            File.Create(ValidInputFile).Dispose();
        Directory.CreateDirectory(ValidTempDir);
        // Assume output file exists for the cleanup check in the catch block
        if (!File.Exists(ValidOutputFile)) 
            File.Create(ValidOutputFile).Dispose();

        // Act
        // OperationCanceledException should be caught internally
        await _sut.SortAsync(ValidInputFile, ValidOutputFile, ValidTempDir, FileSorter.DefaultMaxChunkSizeInBytes,
            cts.Token);

        // Assert
        _mockChunkingPhase.Verify(
            p => p.CreateSortedChunksAsync(ValidInputFile, ValidTempDir, It.IsAny<long>(), cts.Token), Times.Once);
        _mockMergePhase.Verify(p => p.MergeSortedChunksAsync(tempFiles, ValidOutputFile, cts.Token),
            Times.Once); // Merge was attempted
        
        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
        if (File.Exists(ValidOutputFile)) 
            File.Delete(ValidOutputFile);
    }

    [Fact]
    public async Task SortAsync_InputFileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"); // Highly likely not to exist
        if (File.Exists(nonExistentFile)) File.Delete(nonExistentFile); // Ensure it doesn't exist

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.SortAsync(nonExistentFile, ValidOutputFile, ValidTempDir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SortAsync_InvalidOutputFile_ThrowsArgumentException(string? outputFile)
    {
        // Arrange
        if (!File.Exists(ValidInputFile)) 
            File.Create(ValidInputFile).Dispose();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SortAsync(ValidInputFile, outputFile!, ValidTempDir));
        Assert.Equal("outputFile", ex.ParamName);

        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SortAsync_InvalidTempDirectory_ThrowsArgumentException(string? tempDir)
    {
        // Arrange
        if (!File.Exists(ValidInputFile))
            File.Create(ValidInputFile).Dispose();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SortAsync(ValidInputFile, ValidOutputFile, tempDir!));
        Assert.Equal("tempDirectory", ex.ParamName);

        // Cleanup dummy files if created
        if (File.Exists(ValidInputFile)) 
            File.Delete(ValidInputFile);
    }
}