using System.Text;
using LargeFileShared;
using Moq;

namespace LargeFileSorter.Tests;

public sealed class FileSorterIntegrationTests : IDisposable
{
    private readonly string _baseTestDirectory;
    private readonly string _inputFilePath;
    private readonly string _outputFilePath;
    private readonly FileSorter _sut; // System Under Test
    private readonly string _tempDirectory;

    public FileSorterIntegrationTests()
    {
        // Create unique directories/files for each test run to avoid conflicts
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), $"FileSorterTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_baseTestDirectory);

        _tempDirectory = Path.Combine(_baseTestDirectory, "Temp");
        Directory.CreateDirectory(_tempDirectory);

        _inputFilePath = Path.Combine(_baseTestDirectory, "input.txt");
        _outputFilePath = Path.Combine(_baseTestDirectory, "output.txt");

        // --- Important: Use REAL implementations for integration test ---
        var chunkingPhase = new FileSorterChunkingPhase();
        var mergePhase = new FileSorterExternalMergePhase();
        _sut = new FileSorter(chunkingPhase, mergePhase);
    }

    // --- IDisposable Implementation for Cleanup ---
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_baseTestDirectory)) Directory.Delete(_baseTestDirectory, true);
        }
        catch (IOException ex)
        {
            // Log or output cleanup errors, but don't fail the test run
            Console.WriteLine(
                $"Warning: Could not fully clean up test directory {_baseTestDirectory}. Error: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine(
                $"Warning: Could not fully clean up test directory {_baseTestDirectory} due to permissions. Error: {ex.Message}");
        }
    }

    // --- Test Data Generation Helper ---
    private static async Task CreateInputFileAsync(IEnumerable<string> lines, string path)
    {
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8);
    }

    private static List<string> GetValidInputLines(IEnumerable<string> rawLines)
    {
        return rawLines.Where(l => Line.TryParse(l, out _)).ToList();
    }

    private static List<Line> GetParsedLines(IEnumerable<string> rawLines)
    {
        var parsedLines = new List<Line>();
        foreach (var rawLine in rawLines)
            if (Line.TryParse(rawLine, out var line))
                parsedLines.Add(line);

        return parsedLines;
    }

    [Fact]
    public async Task SortAsync_WithValidData_ProducesCorrectlySortedOutputFile()
    {
        // Arrange
        var inputLinesRaw = new List<string>
        {
            "415. Apple",
            "300. Banana", // Will sort before Apple based on text
            "99. Cherry",
            "1. Apple", // Same text as 415, but lower number
            "1000. Date",
            "50. Banana", // Same text as 300, but lower number
            "invalid line format", // Should be skipped
            "200. Fig",
            "", // Empty line, should be skipped
            "   ", // Whitespace line, should be skipped
            "75. Apple" // Another apple
        };
        await CreateInputFileAsync(inputLinesRaw, _inputFilePath);

        var expectedValidInputLines = GetValidInputLines(inputLinesRaw);
        var expectedParsedAndSortedLines = GetParsedLines(expectedValidInputLines);
        expectedParsedAndSortedLines.Sort(); // Sort using Line.CompareTo for expected order

        var inputFileSize = new FileInfo(_inputFilePath).Length; // Get size *before* potential modification by SUT

        // Act
        await _sut.SortAsync(_inputFilePath, _outputFilePath, _tempDirectory);

        // Assert
        Assert.True(File.Exists(_outputFilePath), "Output file should be created.");

        // Read Output
        var outputLinesRaw = await File.ReadAllLinesAsync(_outputFilePath, Encoding.UTF8);
        var outputLinesParsed = GetParsedLines(outputLinesRaw);

        // 1. Size Check (Number of Valid Lines)
        //    We compare the count of *valid* input lines to the count of output lines.
        Assert.Equal(expectedValidInputLines.Count, outputLinesRaw.Length);

        // 2. Correct Sorting Check
        Assert.Equal(expectedParsedAndSortedLines.Count,
            outputLinesParsed.Count); // Ensure same number of lines were parsed
        Assert.Equal(expectedParsedAndSortedLines, outputLinesParsed); // Checks order and content using Line's equality

        // 3. All Output Lines Existed in Input Check
        var validInputLinesSet = new HashSet<string>(expectedValidInputLines);
        foreach (var outputLine in outputLinesRaw) Assert.Contains(outputLine, validInputLinesSet);

        // --- Additional Checks ---

        // 4. Temporary Directory Cleanup Check
        Assert.False(Directory.Exists(_tempDirectory),
            "Temporary directory should be removed or empty after successful sort.");

        // 5. Input File Unchanged Check (Good practice)
        Assert.Equal(inputFileSize, new FileInfo(_inputFilePath).Length);
        var inputLinesAfterSort = await File.ReadAllLinesAsync(_inputFilePath);
        Assert.Equal(inputLinesRaw, inputLinesAfterSort); // Content check
    }

    // --- Test for Empty Input ---
    [Fact]
    public async Task SortAsync_WithEmptyInputFile_CreatesEmptyOutputFile()
    {
        // Arrange
        await CreateInputFileAsync([], _inputFilePath);

        // Act
        await _sut.SortAsync(_inputFilePath, _outputFilePath, _tempDirectory);

        // Assert
        Assert.True(File.Exists(_outputFilePath));
        var outputLines = await File.ReadAllLinesAsync(_outputFilePath);
        Assert.Empty(outputLines);
        var outputFileSize = new FileInfo(_outputFilePath).Length;
        Assert.Equal(0, outputFileSize);

        Assert.False(Directory.Exists(_tempDirectory),
            "Temporary directory should be removed or empty even for empty input.");
    }

    // --- Test for Input with Only Invalid Lines ---
    [Fact]
    public async Task SortAsync_WithOnlyInvalidLines_CreatesEmptyOutputFile()
    {
        // Arrange
        var inputLinesRaw = new List<string>
        {
            "invalid line 1",
            "another bad line",
            "100 Apple without dot space"
        };
        await CreateInputFileAsync(inputLinesRaw, _inputFilePath);

        // Act
        await _sut.SortAsync(_inputFilePath, _outputFilePath, _tempDirectory);

        // Assert
        Assert.True(File.Exists(_outputFilePath));
        var outputLines = await File.ReadAllLinesAsync(_outputFilePath);
        Assert.Empty(outputLines);
        var outputFileSize = new FileInfo(_outputFilePath).Length;
        Assert.Equal(0, outputFileSize);

        Assert.False(Directory.Exists(_tempDirectory),
            "Temporary directory should be removed or empty for invalid-only input.");
    }

    // --- Test that forces chunking (using small chunk size) ---
    [Fact]
    public async Task SortAsync_WithSmallChunkSize_ForcesChunkingAndSortsCorrectly()
    {
        // Arrange
        var inputLinesRaw = new List<string>
        {
            // Generate enough lines to exceed a small chunk size
            "50. Zebra", "10. Ant", "99. Yak", "5. Bee", "60. Xylophone",
            "25. Cat", "70. Whale", "15. Dog", "85. Vulture", "30. Elephant",
            "95. Unicorn", "40. Frog", "1. Aardvark", "45. Gorilla"
        };
        // Calculate a rough size - make chunk size smaller than one or two lines
        // Rough estimate: average line length * 2 bytes/char + object overhead
        const long smallChunkSize = 10 * 2 + 16; // Very small to force many chunks

        await CreateInputFileAsync(inputLinesRaw, _inputFilePath);

        var expectedValidInputLines = GetValidInputLines(inputLinesRaw);
        var expectedParsedAndSortedLines = GetParsedLines(expectedValidInputLines);
        expectedParsedAndSortedLines.Sort();

        // Act
        await _sut.SortAsync(_inputFilePath, _outputFilePath, _tempDirectory, smallChunkSize);

        // Assert (same checks as the main test)
        Assert.True(File.Exists(_outputFilePath));
        var outputLinesRaw = await File.ReadAllLinesAsync(_outputFilePath, Encoding.UTF8);
        var outputLinesParsed = GetParsedLines(outputLinesRaw);

        Assert.Equal(expectedValidInputLines.Count, outputLinesRaw.Length); // Line count
        Assert.Equal(expectedParsedAndSortedLines, outputLinesParsed); // Sorting order
        var validInputLinesSet = new HashSet<string>(expectedValidInputLines);
        foreach (var outputLine in outputLinesRaw)
            Assert.Contains(outputLine, validInputLinesSet); // Existence

        Assert.False(Directory.Exists(_tempDirectory)); // Cleanup
    }
}