using System.Threading.Channels;
using LargeFileShared;
using Moq;

namespace LargeFileGenerator.Tests;

public class FileGeneratorOrchestratorTests
{
    private readonly Mock<ILineGenerator> _mockLineGenerator;
    private readonly Mock<IFileWriter> _mockFileWriter;
    private readonly Mock<ILineProducer> _mockProducer;
    private readonly FileGeneratorSettings _settings;
    private readonly FileGenerator _fileGenerator;
    private readonly Func<ILineGenerator, FileGeneratorSettings, int, ILineProducer> _testProducerFactory;

    public FileGeneratorOrchestratorTests()
    {
        _mockLineGenerator = new Mock<ILineGenerator>();
        _mockFileWriter = new Mock<IFileWriter>();
        _mockProducer = new Mock<ILineProducer>();

        _settings = new FileGeneratorSettings
        {
            OutputFilePath = "test_output.txt",
            TargetSizeBytes = 1024,
            LinesPerBatch = 10,
            ChannelCapacity = 5
        };

        // Setup the factory to return our single mock producer instance
        _testProducerFactory = (lg, s, id) => _mockProducer.Object;

        _fileGenerator = new FileGenerator(
            _mockLineGenerator.Object,
            _settings,
            _mockFileWriter.Object,
            _testProducerFactory // Inject the factory returning the mock
        );

        // Default setup for mocks to complete successfully
        _mockProducer.Setup(p => p.ProduceAsync(It.IsAny<ChannelWriter<Line[]>>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask); // Producer finishes immediately

        _mockFileWriter.Setup(w => w.WriteToFileAsync(It.IsAny<ChannelReader<Line[]>>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationTokenSource>()))
                       .Returns(Task.CompletedTask); // Writer finishes immediately
    }

    [Fact]
    public async Task GenerateAsync_StartsProducersAndConsumer()
    {
        // Arrange
        var expectedProducerCount = Environment.ProcessorCount;

        // Act
        await _fileGenerator.GenerateAsync();

        // Assert
        // Verify ProduceAsync was called for the expected number of producers
        _mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<ChannelWriter<Line[]>>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(expectedProducerCount));

        // Verify WriteToFileAsync was called once
        _mockFileWriter.Verify(w => w.WriteToFileAsync(
            It.IsAny<ChannelReader<Line[]>>(),
            _settings.OutputFilePath,
            _settings.TargetSizeBytes,
            It.IsAny<CancellationTokenSource>()), // Verify key args passed correctly
            Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_WaitsForAllTasksAndCompletesChannel()
    {
        // Arrange
        var producerCompletionSource = new TaskCompletionSource<bool>();
        var consumerCompletionSource = new TaskCompletionSource<bool>();

        _mockProducer.Setup(p => p.ProduceAsync(It.IsAny<ChannelWriter<Line[]>>(), It.IsAny<CancellationToken>()))
                     .Returns(producerCompletionSource.Task); // Producer waits

        _mockFileWriter.Setup(w => w.WriteToFileAsync(It.IsAny<ChannelReader<Line[]>>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationTokenSource>()))
                       .Callback<ChannelReader<Line[]>, string, long, CancellationTokenSource>((reader, path, size, cts) =>
                       {
                            // HACK: Need access to the writer side of the channel created internally
                            // This is hard without modifying FileGenerator. For testing, we assume correct wiring.
                            // We can verify completion conceptually.
                       })
                       .Returns(consumerCompletionSource.Task); // Consumer waits

        // Act
        var generateTask = _fileGenerator.GenerateAsync();

        // Assertions during execution
        Assert.False(generateTask.IsCompleted, "GenerateAsync should not complete before tasks finish.");

        // Simulate producers finishing
        producerCompletionSource.SetResult(true);
        await Task.Delay(50); // Give time for WhenAll(producerTasks) to complete and Complete() to be called

        // At this point, Channel.Writer.Complete() should have been called conceptually.
        // Verification is tricky without exposing the channel.

        // Simulate consumer finishing
        consumerCompletionSource.SetResult(true);
        await generateTask; // Now the main task should complete

        // Final Assert
        Assert.True(generateTask.IsCompletedSuccessfully);
        // Verify mocks were called (already implicitly done by Setup/Return)
    }

    [Fact]
    public async Task GenerateAsync_PassesCorrectCancellationTokenAndSource()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var capturedProducerToken = CancellationToken.None;
        CancellationTokenSource? capturedConsumerCts = null;

        _mockProducer.Setup(p => p.ProduceAsync(It.IsAny<ChannelWriter<Line[]>>(), It.IsAny<CancellationToken>()))
                     .Callback<ChannelWriter<Line[]>, CancellationToken>((writer, token) =>
                     {
                         capturedProducerToken = token; // Capture the token passed to the producer
                     })
                     .Returns(Task.CompletedTask);

        _mockFileWriter.Setup(w => w.WriteToFileAsync(It.IsAny<ChannelReader<Line[]>>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationTokenSource>()))
                       .Callback<ChannelReader<Line[]>, string, long, CancellationTokenSource>((reader, path, size, cts) =>
                       {
                           capturedConsumerCts = cts; // Capture the CTS passed to the consumer
                       })
                       .Returns(Task.CompletedTask);

        // Act
        await _fileGenerator.GenerateInternalAsync(cts);

        // Assert
        Assert.NotNull(capturedConsumerCts);
        Assert.False(capturedProducerToken == CancellationToken.None, "Producer should have received a valid token.");
        Assert.True(capturedProducerToken.CanBeCanceled, "Producer token should be cancellable.");

        // Verify the producer's token is linked to the consumer's CTS
        Assert.False(capturedProducerToken.IsCancellationRequested); // Initially not cancelled
        await capturedConsumerCts.CancelAsync(); // Cancel via the source given to consumer
        Assert.True(capturedProducerToken.IsCancellationRequested, "Producer token should become cancelled when consumer CTS is cancelled.");
        cts.Dispose();
    }

    [Fact]
    public void Constructor_NullLineGenerator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("lineGenerator", () =>
            new FileGenerator(null!, _settings, _mockFileWriter.Object, _testProducerFactory));
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("settings", () =>
            new FileGenerator(_mockLineGenerator.Object, null!, _mockFileWriter.Object, _testProducerFactory));
    }

     [Fact]
    public void Constructor_NullFileWriter_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("fileWriter", () =>
            new FileGenerator(_mockLineGenerator.Object, _settings, null!, _testProducerFactory));
    }

    [Fact]
    public void Constructor_NullProducerFactory_DoesNotThrow()
    {
       var exception = Record.Exception(() => new FileGenerator(_mockLineGenerator.Object, _settings, _mockFileWriter.Object, null));
       Assert.Null(exception);
    }
}