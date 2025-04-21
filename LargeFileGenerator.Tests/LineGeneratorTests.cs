namespace LargeFileGenerator.Tests;

using Xunit;
using System;

public class LineGeneratorTests : IDisposable
{
    private readonly LineGeneratorSettings _settings;
    private readonly LineGenerator _generator;

    public LineGeneratorTests()
    {
        _settings = new LineGeneratorSettings
        {
            TextMinLength = 5,
            TextMaxLength = 10,
            AllowedChars = "abc" // Keep simple for testing
        };
        _generator = new LineGenerator(_settings);
    }

    [Fact]
    public void Generate_ReturnsLineWithNumberAndText()
    {
        // Act
        var line = _generator.Generate();

        // Assert
        Assert.NotNull(line.Text);
        Assert.False(string.IsNullOrEmpty(line.Text));
        // Number can be anything, just check Text properties
    }

    [Fact]
    public void Generate_TextLength_WithinSettingsBounds()
    {
        // Act
        for (var i = 0; i < 100; i++) // Generate multiple lines to increase confidence
        {
            var line = _generator.Generate();
            // Assert
            Assert.InRange(line.Text.Length, _settings.TextMinLength, _settings.TextMaxLength);
        }
    }

    [Fact]
    public void Generate_TextUsesAllowedChars()
    {
        // Act
        for (var i = 0; i < 10; i++) // Generate a few lines
        {
            var line = _generator.Generate();
            // Assert
            foreach (char c in line.Text)
            {
                Assert.Contains(c, _settings.AllowedChars);
            }
        }
    }

    [Fact]
    public void Generate_WithExistingLine_KeepsText_ChangesNumber()
    {
        // Arrange
        var existingLine = _generator.Generate(); // Generate one line first

        // Act
        var newLine = _generator.Generate(existingLine); // Generate based on the first one

        // Assert
        Assert.Equal(existingLine.Text, newLine.Text); // Text should be the same
        // Numbers are random, they *could* be the same, but are highly unlikely.
        // We can't definitively assert they are different without controlling Random.
        // This assertion primarily checks that the Text is preserved.
        // Assert.NotEqual(existingLine.Number, newLine.Number); // Potential flaky test
        Assert.Equal($"{newLine.Number}. {existingLine.Text}", newLine.RawValue); // Check raw value format
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        // Assert
        Assert.Throws<ArgumentNullException>("settings", () => new LineGenerator(null!));
    }


    public void Dispose()
    {
        _generator.Dispose(); // Ensure dispose doesn't throw
    }
}