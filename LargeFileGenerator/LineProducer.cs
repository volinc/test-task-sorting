using System.Buffers;
using System.Threading.Channels;
using LargeFileShared;

namespace LargeFileGenerator;

public sealed class LineProducer : ILineProducer
{
    private readonly ILineGenerator _lineGenerator;
    private readonly FileGeneratorSettings _settings;
    private readonly int _producerId;

    public LineProducer(ILineGenerator lineGenerator, FileGeneratorSettings settings, int producerId = 0)
    {
        _lineGenerator = lineGenerator ?? throw new ArgumentNullException(nameof(lineGenerator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _producerId = producerId;
    }

    public async Task ProduceAsync(ChannelWriter<Line[]> channelWriter, CancellationToken cancellationToken)
    {
        var arrayPool = ArrayPool<Line>.Shared;
        Console.WriteLine($"Producer {_producerId}: Starting generation.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = arrayPool.Rent(_settings.LinesPerBatch);
                var count = 0;
                try
                {
                    for (var i = 0; i < _settings.LinesPerBatch; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        batch[count] = _settings.ShouldUseExistingLine(count, out var lineIndex)
                            ? _lineGenerator.Generate(batch[lineIndex])
                            : _lineGenerator.Generate();
                        count++;
                    }

                    if (count > 0)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var finalBatch = new Line[count];
                        Array.Copy(batch, 0, finalBatch, 0, count);
                        // Return rented array *before* async channel write
                        arrayPool.Return(batch, clearArray: false);
                        batch = null;

                        await channelWriter.WriteAsync(finalBatch, cancellationToken);

                        // Allows other tasks to run, improving fairness/responsiveness
                        await Task.Yield();
                    }
                    else
                    {
                        arrayPool.Return(batch, clearArray: false);
                        batch = null;
                    }
                }
                finally
                {
                    // Ensure buffer is returned if an exception occurred mid-batch generation
                    if (batch != null)
                        arrayPool.Return(batch, clearArray: false);
                }

                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Producer {_producerId}: Task cancelled.");
        }
        catch (ChannelClosedException)
        {
            Console.WriteLine($"Producer {_producerId}: Task exiting: Channel closed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Producer {_producerId}: Error: {ex.Message}");
            throw;
        }
        finally
        {
            Console.WriteLine($"Producer {_producerId}: Task finished.");
        }
    }
}