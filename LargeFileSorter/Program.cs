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
    PrintUsage();
    return 1;
}

var inputFile = args[0];
var outputFile = args[1];
var tempDirectory = Path.Combine(Path.GetTempPath(), "LargeFileSorter_Temp_" + Guid.NewGuid().ToString("N")[..8]);
var maxChunkSize = FileSorter.DefaultMaxChunkSizeInBytes;

if (args.Length > 2)
{
    tempDirectory = args[2];
    Console.WriteLine($"Using custom temp directory: {tempDirectory}");
}

if (args.Length > 3 && long.TryParse(args[3], out var chunkSizeMb) && chunkSizeMb > 0)
{
    maxChunkSize = chunkSizeMb * 1024 * 1024;
    Console.WriteLine($"Using custom chunk size: {chunkSizeMb} MB");
}

try
{
    await FileSorter.SortFileAsync(inputFile, outputFile, tempDirectory, maxChunkSize, cts.Token);
    Console.WriteLine("Sorting completed successfully.");
    return 0;
}
catch (FileNotFoundException fnfEx)
{
    Console.Error.WriteLine($"Error: Input file not found: {fnfEx.FileName}");
    return 2;
}
catch (IOException ioEx)
{
     Console.Error.WriteLine($"Error: An I/O error occurred: {ioEx.Message}");
     
     if (ioEx.HResult == -2147024784 // 0x80070070 ERROR_DISK_FULL
         || ioEx.Message.Contains("There is not enough space on the disk", StringComparison.OrdinalIgnoreCase))
     {
        Console.Error.WriteLine("Potential cause: Disk full (input, output, or temporary directory).");
     }
     return 3;
}
catch (OperationCanceledException)
{
     Console.Error.WriteLine("Operation was cancelled by the user.");
     return 4;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
    Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
    return 99;
}

void PrintUsage()
{
    Console.WriteLine("Usage: LargeFileSorter <input_file> <output_file> [temp_directory] [chunk_size_mb]");
    Console.WriteLine("  <input_file>: Path to the large text file to sort.");
    Console.WriteLine("  <output_file>: Path where the sorted file will be saved.");
    Console.WriteLine("  [temp_directory]: Optional. Directory for temporary chunk files.");
    Console.WriteLine("                    Defaults to a unique subdirectory in the system temp folder.");
    Console.WriteLine("  [chunk_size_mb]: Optional. Maximum size (in MB) for in-memory sorting chunks.");
    Console.WriteLine($"                   Defaults to {FileSorter.DefaultMaxChunkSizeInBytes / (1024*1024)} MB.");
}