namespace LargeFileGenerator;

public sealed class FileGeneratorSettings
{
    public delegate bool ReuseExistingLine(int currentBatchCount, out int lineIndex);

    public string FilePath { get; init; } = "large_file.txt";
    public long TargetSizeBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public int LinesPerBatch { get; init; } = 10_000; // How many lines each producer generates at a time
    public int ChannelCapacity { get; init; } = 100; // Max batches buffered between producers and consumer
    
    public ReuseExistingLine ShouldUseExistingLine { get; init; } = (int count, out int index) =>
    {
        index = 0;
        if (count < 1)
            return false;
        if (DateTime.UtcNow.Ticks % 500L != 0L) 
            return false;
        
        index = count - 1;
        return true;
    };
}