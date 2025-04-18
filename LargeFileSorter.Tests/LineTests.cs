using LargeFileShared;

namespace LargeFileSorter.Tests;

public class LineTests
{
    [Theory]
    [InlineData("415. Apple", 415, "Apple")]
    [InlineData("1. Banana", 1, "Banana")]
    [InlineData("30432. Something something something", 30432, "Something something something")]
    [InlineData("0. Zero", 0, "Zero")]
    [InlineData("-10. Negative", -10, "Negative")]
    [InlineData("9223372036854775807. Max Long", 9223372036854775807, "Max Long")]
    [InlineData("10. Text with . period", 10, "Text with . period")]
    [InlineData("5. ", 5, "")] // Text is empty
    public void TryParse_ValidLines_ReturnsTrueAndCorrectData(string line, long expectedNumber, string expectedText)
    {
        // Act
        bool result = Line.TryParse(line, out var entry);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedNumber, entry.Number);
        Assert.Equal(expectedText, entry.Text);
        Assert.Equal(line, entry.RawValue); // Ensure original line is preserved
    }

    [Theory]
    [InlineData("Apple")] // Missing number and separator
    [InlineData("123 Apple")] // Missing period
    [InlineData(". Apple")] // Missing number
    [InlineData("123.Apple")] // Missing space
    [InlineData("NotANumber. Apple")] // Invalid number
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void TryParse_InvalidLines_ReturnsFalse(string? value)
    {
        // Act
        var result = Line.TryParse(value, out _);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CompareTo_SortsCorrectly()
    {
        // Arrange
        Line.TryParse("415. Apple", out var apple1);
        Line.TryParse("1. Apple", out var apple2); // Same text, different number
        Line.TryParse("2. Banana is yellow", out var banana);
        Line.TryParse("32. Cherry is the best", out var cherry);
        Line.TryParse("30432. Something something something", out var something);

        var list = new List<Line> { something, cherry, banana, apple1, apple2 };
        var expected = new List<Line> { apple2, apple1, banana, cherry, something }; // Expected order

        // Act
        list.Sort(); // Uses CompareTo

        // Assert
        Assert.Equal(expected, list);

        // Detailed comparison checks
        Assert.True(apple2.CompareTo(apple1) < 0); // 1. Apple < 415. Apple
        Assert.True(apple1.CompareTo(apple2) > 0); // 415. Apple > 1. Apple
        Assert.True(apple1.CompareTo(banana) < 0); // Apple < Banana
        Assert.True(banana.CompareTo(apple1) > 0); // Banana > Apple
        Assert.True(cherry.CompareTo(something) < 0); // Cherry < Something
        Assert.True(apple1.CompareTo(apple1) == 0); // Equal
    }
}