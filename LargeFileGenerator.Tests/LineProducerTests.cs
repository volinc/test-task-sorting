using System.Threading.Channels;
using LargeFileShared;
using Moq;

namespace LargeFileGenerator.Tests;

public class LineProducerTests
{
    private readonly Mock<ILineGenerator> _mockLineGenerator;
    private readonly FileGeneratorSettings _settings;
    private readonly LineProducer _producer;
    private readonly Channel<Line[]> _testChannel;

    public LineProducerTests()
    {
        _mockLineGenerator = new Mock<ILineGenerator>();
        _settings = new FileGeneratorSettings
        {
            LinesPerBatch = 5,
            ShouldUseExistingLine = (int _, out int index) => { index = 0; return false; } 
        };
        _producer = new LineProducer(_mockLineGenerator.Object, _settings, 1);
        _testChannel = Channel.CreateUnbounded<Line[]>(); // Use unbounded for easier testing

        // Setup mock generator
        _mockLineGenerator.Setup(g => g.Generate())
            .Returns(() => new Line(Random.Shared.NextInt64(), "Generated Text"));
        _mockLineGenerator.Setup(g => g.Generate(It.IsAny<Line>()))
            .Returns((Line l) => new Line(Random.Shared.NextInt64(), l.Text)); // Keep text
    }

    [Fact]
    public async Task ProduceAsync_RespectsCancellation_StopsProducing()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var producedItems = new List<Line[]>();

        // Act
        var producerTask = _producer.ProduceAsync(_testChannel.Writer, cts.Token);

        // Let it produce one batch then cancel
        var firstBatch = await _testChannel.Reader.ReadAsync(CancellationToken.None);
        producedItems.Add(firstBatch);
        await cts.CancelAsync(); // Cancel immediately after first batch

        // Consume remaining (should be none after cancellation propagates)
        try { await _testChannel.Reader.ReadAsync(new CancellationTokenSource(100).Token); }
        catch 
        { 
            // ignored
        } // Allow time for producer to stop

        await producerTask; // Wait for producer to finish cleanly

        // Assert
        Assert.Single(producedItems); // Should have only produced one batch before stopping
    }

    [Fact]
    public async Task ProduceAsync_UsesLineGeneratorReuseLogic()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var reuseSettings = new FileGeneratorSettings
        {
            LinesPerBatch = 3,
            // Reuse the previous line (index = count - 1) for the 3rd item (count == 2)
            ShouldUseExistingLine = (int count, out int index) =>
            {
                if (count == 2) { index = count - 1; return true; }
                index = 0; return false;
            }
        };
        var producerWithReuse = new LineProducer(_mockLineGenerator.Object, reuseSettings);

        // Act
        var producerTask = producerWithReuse.ProduceAsync(_testChannel.Writer, cts.Token);
        var batch = await _testChannel.Reader.ReadAsync(cts.Token); // Read one batch
        await cts.CancelAsync(); // Stop producer
        await producerTask;

        // Assert
        Assert.Equal(reuseSettings.LinesPerBatch, batch.Length);
        _mockLineGenerator.Verify(g => g.Generate(), Times.Exactly(2)); // Called for item 0 and 1
        _mockLineGenerator.Verify(g => g.Generate(batch[1]), Times.Once()); // Called for item 2, reusing item 1
    }

    [Fact]
    public async Task ProduceAsync_HandlesChannelClosedExceptionGracefully()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _testChannel.Writer.Complete(); // Close the channel immediately

        // Act
        // Exception should be caught internally, not thrown out
        var producerTask = _producer.ProduceAsync(_testChannel.Writer, cts.Token);
        await producerTask; // Wait for it to finish

        // Assert
        // No exception should be thrown by await. Task status should be RanToCompletion.
        Assert.True(producerTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void Constructor_NullGenerator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("lineGenerator", () => new LineProducer(null!, _settings));
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("settings", () => new LineProducer(_mockLineGenerator.Object, null!));
    }
}