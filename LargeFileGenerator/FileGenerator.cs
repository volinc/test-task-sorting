using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using LargeFileShared;

namespace LargeFileGenerator;

public sealed class FileGenerator : IFileGenerator
{
    private readonly ILineGenerator _lineGenerator;
    private readonly FileGeneratorSettings _settings;
    
    public FileGenerator(ILineGenerator lineGenerator, FileGeneratorSettings settings)
    {
        _lineGenerator = lineGenerator;
        _settings = settings;
    }

    public async Task GenerateAsync()
    {
        Console.WriteLine($"Starting generation of file: {_settings.OutputFile}");
        Console.WriteLine($"Target size: {_settings.TargetSizeBytes:N0} bytes.");
        Console.WriteLine($"Using {Environment.ProcessorCount} producer threads.");
        Console.WriteLine($"Generating batches of {_settings.LinesPerBatch:N0} lines.");
        var stopwatch = Stopwatch.StartNew();
        
        var cts = new CancellationTokenSource();

        var channel = Channel.CreateBounded<Line[]>(new BoundedChannelOptions(_settings.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var consumerTask = ConsumeAndWriteAsync(channel.Reader, _settings.OutputFile, _settings.TargetSizeBytes, cts);
        
        var producerTasks = new List<Task>();
        var producerCount = Environment.ProcessorCount;
        for (var i = 0; i < producerCount; i++)
        {
            producerTasks.Add(ProduceLinesAsync(channel.Writer, cts.Token));
        }

        await Task.WhenAll(producerTasks);
        Console.WriteLine("All producers finished generating or were cancelled.");

        channel.Writer.Complete();
        Console.WriteLine("Channel marked as complete.");

        // Wait until the consumer has processed all items in the channel or hit the size limit
        await consumerTask;

        stopwatch.Stop();
        Console.WriteLine($"\n--------------------------------------------------");
        Console.WriteLine($"File generation finished.");
        Console.WriteLine($"Elapsed time: {stopwatch.Elapsed}");

        try
        {
            var fileInfo = new FileInfo(_settings.OutputFile);
            Console.WriteLine(fileInfo.Exists
                ? $"Actual file size: {fileInfo.Length:N0} bytes ({(double) fileInfo.Length / (1024 * 1024 * 1024):F2} GB)"
                : "File was not created or writing failed early.");
        }
        catch (Exception ex) 
        {
            Console.WriteLine($"Error getting file info: {ex.Message}");
        }
        Console.WriteLine($"--------------------------------------------------");
    }

    internal async Task ProduceLinesAsync(ChannelWriter<Line[]> channelWriter, CancellationToken cancellationToken)
    {
        var arrayPool = ArrayPool<Line>.Shared;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = arrayPool.Rent(_settings.LinesPerBatch);
                var count = 0;
                for (var i = 0; i < _settings.LinesPerBatch; i++)
                {
                    if (cancellationToken.IsCancellationRequested) 
                        break;

                    batch[count] = _settings.ShouldUseExistingLine(count, out var lineIndex)
                        ? _lineGenerator.Generate(batch[lineIndex])
                        : _lineGenerator.Generate();
                    
                    count++;
                }

                if (count > 0)
                {
                    var finalBatch = new Line[count];
                    Array.Copy(batch, 0, finalBatch, 0, count);
                    arrayPool.Return(batch);

                    await channelWriter.WriteAsync(finalBatch, cancellationToken);
                }
                else
                {
                    arrayPool.Return(batch);
                }

                if (cancellationToken.IsCancellationRequested) 
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Producer task cancelled.");
        }
        catch (ChannelClosedException)
        {
            Console.WriteLine("Producer task exiting: Channel closed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Producer error: {ex.Message}");
            throw;
        }
        finally
        {
            Console.WriteLine("Producer task finished.");
        }
    }

    internal static async Task ConsumeAndWriteAsync(ChannelReader<Line[]> reader, string filePath, long targetSizeBytes, CancellationTokenSource cts)
    {
        // Increased buffer size for potentially better performance with large writes
        const int fileStreamBufferSize = 65536; // 64 KB

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: fileStreamBufferSize, 
                options: FileOptions.Asynchronous);
            
            await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: -1, leaveOpen: true);

            var lastReportedProgress = 0L;
            var reportInterval = targetSizeBytes / 20L; // Report progress ~20 times

            Console.WriteLine($"Consumer starting. Writing to {filePath}");

            // Read batches from the channel until it's completed and empty
            await foreach (var batch in reader.ReadAllAsync(cts.Token))
            {
                foreach (var line in batch)
                {
                    await writer.WriteLineAsync(line.RawValue.AsMemory(), cts.Token); // Use WriteLineAsync with ReadOnlyMemory<char>
                }

                // Check file size periodically
                if (stream.Position >= targetSizeBytes)
                {
                    Console.WriteLine($"\nTarget size {targetSizeBytes:N0} bytes reached. Stopping producers...");
                    await cts.CancelAsync(); // Signal producers to stop
                    break; // Exit consumer loop
                }

                if (stream.Position - lastReportedProgress > reportInterval || stream.Position == 0)
                {
                    Console.WriteLine($"Progress: {stream.Position:N0} / {targetSizeBytes:N0} bytes ({(double)stream.Position / targetSizeBytes:P1})");
                    lastReportedProgress = stream.Position;
                }
            }

            // Ensure all buffered data is written to the underlying stream/disk
            await writer.FlushAsync();
            Console.WriteLine("Consumer flushed final writes.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Consumer task cancelled (likely target size reached or external cancellation).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Consumer error during file write: {ex.Message}");
            Console.WriteLine(ex.ToString());
            
            if (!cts.IsCancellationRequested)
            {
                Console.WriteLine("Signalling cancellation due to consumer error.");
                await cts.CancelAsync();
            }
        }
        finally
        {
            Console.WriteLine("Consumer task finished.");
        }
    }
}