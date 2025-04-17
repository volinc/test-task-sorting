namespace LargeFileSorter.Tests;

public class FileSorterIntegrationTests : IDisposable
{
    private readonly string _testInputDir;
    private readonly string _testOutputDir;
    private readonly string _testTempDir;

    public FileSorterIntegrationTests()
    {
        // Create unique directories for each test run to avoid conflicts
        var baseTestDir = Path.Combine(Path.GetTempPath(), "FileSorterTests_" + Guid.NewGuid().ToString("N"));
        _testInputDir = Path.Combine(baseTestDir, "Input");
        _testOutputDir = Path.Combine(baseTestDir, "Output");
        _testTempDir = Path.Combine(baseTestDir, "Temp");

        Directory.CreateDirectory(_testInputDir);
        Directory.CreateDirectory(_testOutputDir);
        Directory.CreateDirectory(_testTempDir); // FileSorter creates this too, but good practice here
    }

    public void Dispose()
    {
        try
        {
            var dirInfo = new DirectoryInfo(Path.GetDirectoryName(_testInputDir)!);
            if (dirInfo.Exists)
            {
                dirInfo.Delete(recursive: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to clean up test directory: {ex.Message}");
        }
        GC.SuppressFinalize(this);
    }

    private async Task CreateTestFile(string path, IEnumerable<string> lines)
    {
        await File.WriteAllLinesAsync(path, lines);
    }

    private async Task<List<string>> ReadTestFile(string path)
    {
        if (!File.Exists(path)) return new List<string>();
        return (await File.ReadAllLinesAsync(path)).ToList();
    }

    [Fact]
    public async Task SortFileAsync_BasicSort_ProducesCorrectOutput()
    {
        // Arrange
        var inputFile = Path.Combine(_testInputDir, "basic.txt");
        var outputFile = Path.Combine(_testOutputDir, "basic_sorted.txt");
        var inputLines = new List<string>
        {
            "415. Apple",
            "30432. Something something something",
            "1. Apple",
            "32. Cherry is the best",
            "2. Banana is yellow"
        };
        var expectedLines = new List<string>
        {
            "1. Apple",
            "415. Apple",
            "2. Banana is yellow",
            "32. Cherry is the best",
            "30432. Something something something"
        };
        await CreateTestFile(inputFile, inputLines);

        // Act
        await FileSorter.SortFileAsync(inputFile, outputFile, _testTempDir);

        // Assert
        Assert.True(File.Exists(outputFile));
        var actualLines = await ReadTestFile(outputFile);
        Assert.Equal(expectedLines, actualLines);
    }

    [Fact]
    public async Task SortFileAsync_WithInvalidLines_SkipsInvalidAndSortsValid()
    {
        // Arrange
        var inputFile = Path.Combine(_testInputDir, "invalid.txt");
        var outputFile = Path.Combine(_testOutputDir, "invalid_sorted.txt");
        var inputLines = new List<string>
        {
            "415. Apple",
            "IGNORE ME", // Invalid
            "1. Apple",
            "BadFormat. Cherry", // Invalid
            "2. Banana is yellow",
            " " // Whitespace line
        };
        var expectedLines = new List<string>
        {
            "1. Apple",
            "415. Apple",
            "2. Banana is yellow"
            // Invalid lines are skipped
        };
        await CreateTestFile(inputFile, inputLines);

        // Act
        // Redirect console error for testing warnings is possible but complex.
        // We mainly test that the output contains only sorted valid lines.
        await FileSorter.SortFileAsync(inputFile, outputFile, _testTempDir);

        // Assert
        Assert.True(File.Exists(outputFile));
        var actualLines = await ReadTestFile(outputFile);
        Assert.Equal(expectedLines, actualLines);
    }

    [Fact]
    public async Task SortFileAsync_EmptyInput_CreatesEmptyOutput()
    {
        // Arrange
        string inputFile = Path.Combine(_testInputDir, "empty.txt");
        string outputFile = Path.Combine(_testOutputDir, "empty_sorted.txt");
        await CreateTestFile(inputFile, new List<string>()); // Empty file

        // Act
        await FileSorter.SortFileAsync(inputFile, outputFile, _testTempDir);

        // Assert
        Assert.True(File.Exists(outputFile));
        var actualLines = await ReadTestFile(outputFile);
        Assert.Empty(actualLines);
    }

    [Fact]
    public async Task SortFileAsync_SmallChunks_ForcesChunkingAndMerging()
    {
        // Arrange
        string inputFile = Path.Combine(_testInputDir, "chunking.txt");
        string outputFile = Path.Combine(_testOutputDir, "chunking_sorted.txt");
        var inputLines = new List<string>
        {
            "99. Zebra", // Chunk 3 (or 2 depending on size estimate)
            "5. Fig",   // Chunk 2
            "10. Apple",// Chunk 1
            "2. Date",  // Chunk 1
            "8. Grape", // Chunk 2
            "1. Apple" // Chunk 1
        };
        // Expected sorted order
        var expectedLines = new List<string>
        {
            "1. Apple",
            "10. Apple",
            "2. Date",
            "5. Fig",
            "8. Grape",
            "99. Zebra"
        };
        await CreateTestFile(inputFile, inputLines);

        // Act: Use a very small chunk size to force multiple chunks
        // Estimate size: 6 lines * ~20 chars/line * 2 bytes/char + overhead ~= 300-500 bytes
        await FileSorter.SortFileAsync(inputFile, outputFile, _testTempDir, maxChunkSizeInBytes: 100); // Force chunking

        // Assert
        Assert.True(File.Exists(outputFile));
        var actualLines = await ReadTestFile(outputFile);
        Assert.Equal(expectedLines, actualLines);
        // Also check if temp files were created and cleaned up (optional, harder to assert reliably)
        Assert.False(Directory.EnumerateFiles(_testTempDir, "*.tmp").Any(), "Temporary files should be cleaned up.");
    }

    [Fact]
    public async Task SortFileAsync_DuplicateEntries_MaintainsCorrectOrder()
    {
        // Arrange
        string inputFile = Path.Combine(_testInputDir, "duplicates.txt");
        string outputFile = Path.Combine(_testOutputDir, "duplicates_sorted.txt");
        var inputLines = new List<string>
        {
            "10. Apple",
            "5. Banana",
            "10. Apple", // Duplicate
            "1. Apple",
            "5. Banana", // Duplicate
            "20. Apple"
        };
        var expectedLines = new List<string>
        {
            "1. Apple",
            "10. Apple",
            "10. Apple", // Duplicates maintained, ordered by number
            "20. Apple",
            "5. Banana",
            "5. Banana"  // Duplicates maintained, ordered by number (here they are same)
        };
        await CreateTestFile(inputFile, inputLines);

        // Act
        await FileSorter.SortFileAsync(inputFile, outputFile, _testTempDir);

        // Assert
        Assert.True(File.Exists(outputFile));
        var actualLines = await ReadTestFile(outputFile);
        Assert.Equal(expectedLines, actualLines);
    }
}