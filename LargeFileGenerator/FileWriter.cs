using System.Text;
using System.Threading.Channels;
using LargeFileShared;

namespace LargeFileGenerator;

public sealed class FileWriter : IFileWriter
{
    private const int FileStreamBufferSize = 65536; // 64 KB

    public async Task WriteToFileAsync(ChannelReader<Line[]> reader, string filePath, long targetSizeBytes, 
        CancellationTokenSource cts)
    {
        _ = reader ?? throw new ArgumentNullException(nameof(reader));
        _ = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _ = cts ?? throw new ArgumentNullException(nameof(cts));
        
        if (targetSizeBytes <= 0) 
            throw new ArgumentOutOfRangeException(nameof(targetSizeBytes), "Target size must be positive.");

        long currentSize = 0;
        long linesWritten = 0;
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: FileStreamBufferSize, options: FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: -1, leaveOpen: true);

            var lastReportedProgress = 0L;
            var reportIntervalBytes = Math.Max(targetSizeBytes / 20L, 1 * 1024 * 1024);

            Console.WriteLine($"Consumer: Starting. Writing to {filePath}");
            Console.WriteLine($"Consumer: Target size {targetSizeBytes:N0} bytes.");

            await foreach (var batch in reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
            {
                // Frequent awaits within this loop already yield control implicitly during I/O
                foreach (var line in batch)
                {
                    await writer.WriteLineAsync(line.RawValue.AsMemory(), cts.Token).ConfigureAwait(false);
                }
                linesWritten += batch.Length;
                currentSize = stream.Position;

                if (currentSize >= targetSizeBytes)
                {
                    Console.WriteLine($"\nConsumer: Target size {targetSizeBytes:N0} bytes reached (current: {currentSize:N0}). Stopping producers...");
                    break;
                }

                if (currentSize - lastReportedProgress >= reportIntervalBytes || lastReportedProgress == 0)
                {
                    var percentage = (double)currentSize / targetSizeBytes;
                    Console.WriteLine($"Consumer Progress: {currentSize:N0} / {targetSizeBytes:N0} bytes ({percentage:P1}) - {linesWritten:N0} lines");
                    lastReportedProgress = currentSize;
                }
            }

            await writer.FlushAsync(cts.Token).ConfigureAwait(false);
            currentSize = stream.Position;
            Console.WriteLine($"Consumer: Flushed final writes. Final stream position: {currentSize:N0} bytes ({linesWritten:N0} lines).");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Consumer: Task cancelled (likely target size reached or external cancellation).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Consumer: Error during file write: {ex.Message}");
            Console.WriteLine(ex.ToString());
            if (!cts.IsCancellationRequested)
            {
                Console.WriteLine("Consumer: Signalling cancellation due to consumer error.");
                try { await cts.CancelAsync(); } catch (Exception cancelEx) { Console.WriteLine($"Consumer: Error trying to cancel on error: {cancelEx.Message}"); }
            }
        }
        finally
        {
            await cts.CancelAsync();
            
            if (currentSize > 0) 
                Console.WriteLine($"Consumer: Final reported file size: {currentSize:N0} bytes.");
            
            Console.WriteLine("Consumer: Task finished.");
        }
    }
}