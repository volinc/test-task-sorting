using System.Text;

namespace LargeFileSorter.Tests;

public class FileSorterTestFixture : IDisposable
{
    public FileSorterTestFixture()
    {
        TestRootDir = Path.Combine(Path.GetTempPath(), "FileSorterTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(TestRootDir);
        TempDir = Path.Combine(TestRootDir, "SorterTemp");
    }

    public string TestRootDir { get; }
    public string TempDir { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TestRootDir)) 
                Directory.Delete(TestRootDir, true);
        }
        catch (Exception ex)
        {
            // Log or output cleanup error if needed, but don't fail tests
            Console.WriteLine($"Warning: Failed to clean up test directory '{TestRootDir}'. Reason: {ex.Message}");
        }
    }

    public string CreateInputFile(string fileName, string content)
    {
        var path = Path.Combine(TestRootDir, fileName);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    public string GetOutputPath(string fileName)
    {
        return Path.Combine(TestRootDir, fileName);
    }

    public string GetTempChunkPath(int index)
    {
        // Match the naming convention used in ProcessChunkAsync
        return Path.Combine(TempDir, $"chunk_{index}.tmp");
    }
}