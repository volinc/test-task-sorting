namespace LargeFileGenerator.Tests;

public class FileGeneratorSettingsTests
{
    [Fact]
    public void DefaultSettingsAreCorrect()
    {
        var settings = new FileGeneratorSettings();
        Assert.Equal("large_file.txt", settings.FilePath);
        Assert.Equal(10L * 1024 * 1024 * 1024, settings.TargetSizeBytes);
        Assert.Equal(10_000, settings.LinesPerBatch);
        Assert.Equal(100, settings.ChannelCapacity);
        Assert.NotNull(settings.ShouldUseExistingLine);
    }

    [Fact]
    public void CanSetSettingsProperties()
    {
        var settings = new FileGeneratorSettings
        {
            FilePath = "test.txt",
            TargetSizeBytes = 1024,
            LinesPerBatch = 500,
            ChannelCapacity = 50,
            ShouldUseExistingLine = (int _, out int index) =>
            {
                index = 0;
                return false;
            }
        };
        Assert.Equal("test.txt", settings.FilePath);
        Assert.Equal(1024, settings.TargetSizeBytes);
        Assert.Equal(500, settings.LinesPerBatch);
        Assert.Equal(50, settings.ChannelCapacity);
        Assert.NotNull(settings.ShouldUseExistingLine);
    }
}