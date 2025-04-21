using LargeFileSorter;

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("Cancellation requested...");
    cts.Cancel();
    e.Cancel = true;
};

if (args.Length < 2)
{
    await PrintUsageAsync();
    return 1;
}

var inputFile = args[0];
var outputFile = args[1];
var tempDirectory = Path.Combine(Path.GetTempPath(), "LargeFileSorter_Temp_" + Guid.NewGuid().ToString("N")[..8]);
var maxChunkSize = FileSorter.DefaultMaxChunkSizeInBytes;

if (args.Length > 2)
{
    tempDirectory = args[2];
    await Console.Out.WriteLineAsync($"Using custom temp directory: {tempDirectory}");
}

if (args.Length > 3 && long.TryParse(args[3], out var chunkSizeMb) && chunkSizeMb > 0)
{
    maxChunkSize = chunkSizeMb * 1024 * 1024;
    await Console.Out.WriteLineAsync($"Using custom chunk size: {chunkSizeMb} MB");
}

try
{
    var chunkingPhase = new FileSorterChunkingPhase();
    var externalMergePhase = new FileSorterExternalMergePhase();
    var sorter = new FileSorter(chunkingPhase, externalMergePhase);
    await sorter.SortAsync(inputFile, outputFile, tempDirectory, maxChunkSize, cts.Token);
    await Console.Out.WriteLineAsync("Sorting completed successfully.");
    return 0;
}
catch (FileNotFoundException fnfEx)
{
    await Console.Error.WriteLineAsync($"Error: Input file not found: {fnfEx.FileName}");
    return 2;
}
catch (IOException ioEx)
{
     await Console.Error.WriteLineAsync($"Error: An I/O error occurred: {ioEx.Message}");
     
     if (ioEx.HResult == -2147024784 // 0x80070070 ERROR_DISK_FULL
         || ioEx.Message.Contains("There is not enough space on the disk", StringComparison.OrdinalIgnoreCase))
     {
        await Console.Error.WriteLineAsync("Potential cause: Disk full (input, output, or temporary directory).");
     }
     return 3;
}
catch (OperationCanceledException)
{
     await Console.Error.WriteLineAsync("Operation was cancelled by the user.");
     return 4;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"An unexpected error occurred: {ex.Message}");
    await Console.Error.WriteLineAsync($"Stack Trace: {ex.StackTrace}");
    return 99;
}

async Task PrintUsageAsync()
{
    await Console.Out.WriteLineAsync("Usage: LargeFileSorter <input_file> <output_file> [temp_directory] [chunk_size_mb]");
    await Console.Out.WriteLineAsync("  <input_file>: Path to the large text file to sort.");
    await Console.Out.WriteLineAsync("  <output_file>: Path where the sorted file will be saved.");
    await Console.Out.WriteLineAsync("  [temp_directory]: Optional. Directory for temporary chunk files.");
    await Console.Out.WriteLineAsync("                    Defaults to a unique subdirectory in the system temp folder.");
    await Console.Out.WriteLineAsync("  [chunk_size_mb]: Optional. Maximum size (in MB) for in-memory sorting chunks.");
    await Console.Out.WriteLineAsync($"                   Defaults to {FileSorter.DefaultMaxChunkSizeInBytes / (1024*1024)} MB.");
}