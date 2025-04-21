using LargeFileShared;

namespace LargeFileGenerator.Tests;

public class LineTests
{
    [Theory]
    [InlineData("123. Hello World", 123L, "Hello World")]
    [InlineData("0. SingleWord", 0L, "SingleWord")]
    [InlineData("-999. Negative Number", -999L, "Negative Number")]
    [InlineData("9876543210. Text with . inside.", 9876543210L, "Text with . inside.")]
    public void TryParse_ValidInput_ReturnsTrueAndCorrectLine(string input, long expectedNumber, string expectedText)
    {
        // Act
        var success = Line.TryParse(input, out var result);

        // Assert
        Assert.True(success);
        Assert.Equal(expectedNumber, result.Number);
        Assert.Equal(expectedText, result.Text);
        Assert.Equal(input, result.RawValue); // Ensure RawValue is stored correctly
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoSeparator")]
    [InlineData(". MissingNumber")]
    [InlineData("123 MissingDotSpace")]
    [InlineData("abc. NotANumber")]
    [InlineData("123.")] // Missing space after dot
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        // Act
        var success = Line.TryParse(input, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default, result); // Result should be default Line struct
    }

    [Fact]
    public void Constructor_FormatsRawValueCorrectly()
    {
        // Arrange
        const long number = 456;
        const string text = "Another test";
        const string expectedRawValue = "456. Another test";

        // Act
        var line = new Line(number, text);

        // Assert
        Assert.Equal(number, line.Number);
        Assert.Equal(text, line.Text);
        Assert.Equal(expectedRawValue, line.RawValue);
    }

    [Fact]
    public void Equals_And_GetHashCode_Consistent()
    {
        // Arrange
        var line1A = new Line(1, "Test");
        var line1B = new Line(1, "Test");
        var line2 = new Line(2, "Test");
        var line3 = new Line(1, "Different");

        // Assert
        Assert.True(line1A.Equals(line1B));
        Assert.True(line1A == line1B); // Test operator
        Assert.False(line1A.Equals(line2));
        Assert.False(line1A == line2);
        Assert.False(line1A.Equals(line3));
        Assert.False(line1A == line3);
        Assert.False(line1A.Equals(null));

        Assert.Equal(line1A.GetHashCode(), line1B.GetHashCode());
        // Hash codes are not guaranteed to be different for non-equal objects,
        // but they often are. No strict assert here, just consistency check.
        Assert.NotEqual(line1A.GetHashCode(), line2.GetHashCode()); // Could collide
        Assert.NotEqual(line1A.GetHashCode(), line3.GetHashCode()); // Could collide
    }

    [Fact]
    public void CompareTo_SortsCorrectly()
    {
        // Arrange
        var lineA1 = new Line(1, "Apple"); // Text comes first
        var lineA2 = new Line(2, "Apple");
        var lineB1 = new Line(1, "Banana");

        // Assert
        Assert.Equal(0, lineA1.CompareTo(new Line(1, "Apple"))); // Equal
        Assert.True(lineA1.CompareTo(lineA2) < 0); // Same text, smaller number
        Assert.True(lineA2.CompareTo(lineA1) > 0); // Same text, larger number
        Assert.True(lineA1.CompareTo(lineB1) < 0); // Different text ("Apple" < "Banana")
        Assert.True(lineB1.CompareTo(lineA1) > 0); // Different text ("Banana" > "Apple")

        // Test operators
        Assert.True(lineA1 < lineA2);
        Assert.True(lineA1 <= lineA2);
        Assert.True(lineA2 > lineA1);
        Assert.True(lineA2 >= lineA1);
        Assert.True(lineA1 < lineB1);
        Assert.True(lineA1 != lineB1);
    }

     [Fact]
    public void ToString_ReturnsRawValue()
    {
        // Arrange
        var line = new Line(789, "String representation");

        // Act & Assert
        Assert.Equal("789. String representation", line.ToString());
    }
}