namespace LargeFileGenerator;

public sealed class FileGeneratorSettings
{
    public delegate bool ReuseExistingLine(int currentBatchCount, out int lineIndex);

    public const string DefaultOutputFile = "large_file.txt";
    public const long DefaultTargetSizeBytes = 20L * 1024 * 1024 * 1024; // 20GB
    
    public string OutputFilePath { get; set; } = DefaultOutputFile;
    public long TargetSizeBytes { get; set; } = DefaultTargetSizeBytes;
    public int LinesPerBatch { get; set; } = 10_000; // How many lines each producer generates at a time
    public int ChannelCapacity { get; set; } = 1_000; // Max batches buffered between producers and consumer
    
    public ReuseExistingLine ShouldUseExistingLine { get; set; } = (int count, out int index) =>
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