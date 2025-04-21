using System.Diagnostics;
using System.Threading.Channels;
using LargeFileShared;

namespace LargeFileGenerator;

public sealed class FileGenerator
{
    private readonly ILineGenerator _lineGenerator;
    private readonly FileGeneratorSettings _settings;
    private readonly Func<ILineGenerator, FileGeneratorSettings, int, ILineProducer> _producerFactory;
    private readonly IFileWriter _fileWriter;
    
    public FileGenerator(
        ILineGenerator lineGenerator,
        FileGeneratorSettings settings,
        IFileWriter fileWriter,
        Func<ILineGenerator, FileGeneratorSettings, int, ILineProducer>? producerFactory = null)
    {
        _lineGenerator = lineGenerator ?? throw new ArgumentNullException(nameof(lineGenerator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
        _producerFactory = producerFactory ?? ((lg, s, id) => new LineProducer(lg, s, id));
    }

    public async Task GenerateAsync()
    {
        var cts = new CancellationTokenSource();
        try
        {
            await GenerateInternalAsync(cts);
        }
        finally
        {
            cts.Dispose();
        }
    }
    
    internal async Task GenerateInternalAsync(CancellationTokenSource cts)
    {
        Console.WriteLine($"Starting generation of file: {_settings.OutputFilePath}");
        Console.WriteLine($"Target size: {_settings.TargetSizeBytes:N0} bytes.");
        var producerCount = Environment.ProcessorCount;
        Console.WriteLine($"Using {producerCount} producer threads.");
        Console.WriteLine($"Generating batches of {_settings.LinesPerBatch:N0} lines.");
        var stopwatch = Stopwatch.StartNew();
        
        var channel = Channel.CreateBounded<Line[]>(new BoundedChannelOptions(_settings.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var consumerTask = _fileWriter.WriteToFileAsync(
            channel.Reader,
            _settings.OutputFilePath,
            _settings.TargetSizeBytes,
            cts
        );

        var producerTasks = new List<Task>(producerCount);
        for (var i = 0; i < producerCount; i++)
        {
            var producer = _producerFactory(_lineGenerator, _settings, i + 1);
            producerTasks.Add(producer.ProduceAsync(channel.Writer, cts.Token));
        }

        await Task.WhenAll(producerTasks).ConfigureAwait(false);
        Console.WriteLine("Orchestrator: All producers finished generating or were cancelled.");

        channel.Writer.Complete();
        Console.WriteLine("Orchestrator: Channel marked as complete.");

        await consumerTask.ConfigureAwait(false);
        Console.WriteLine("Orchestrator: Consumer task finished.");

        stopwatch.Stop();
        Console.WriteLine($"\n--------------------------------------------------");
        Console.WriteLine($"File generation finished.");
        Console.WriteLine($"Elapsed time: {stopwatch.Elapsed}");

        try
        {
            var fileInfo = new FileInfo(_settings.OutputFilePath);
            Console.WriteLine(fileInfo.Exists
                ? $"Actual file size: {fileInfo.Length:N0} bytes ({(double)fileInfo.Length / (1024 * 1024 * 1024):F2} GB)"
                : "File was not created or writing failed early.");
            Console.WriteLine($"Target Size was: {_settings.TargetSizeBytes:N0} bytes");
            if(fileInfo.Exists)
            {
                double diff = fileInfo.Length - _settings.TargetSizeBytes;
                Console.WriteLine($"Difference: {diff:N0} bytes (Actual size is {(diff >= 0 ? "larger" : "smaller")})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting file info: {ex.Message}");
        }
        Console.WriteLine($"--------------------------------------------------");
    }
}