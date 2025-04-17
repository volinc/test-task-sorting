namespace LargeFileShared;

public readonly struct Line : IComparable<Line>, IEquatable<Line>
{
    public Line(long number, string text) : this(number, text, FormatAsRowValue(number, text))
    {
    }

    private static string FormatAsRowValue(long number, string text) => $"{number}. {text}";
    
    private Line(long number, string text, string rowValue)
    {
        Number = number;
        Text = text;
        RowValue = rowValue;
    }
    
    public long Number { get; }
    public string Text { get; }
    public string RowValue { get; }
    
    public static bool TryParse(string? value, out Line result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var separatorIndex = value.IndexOf(". ", StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            Console.Error.WriteLine($"Warning: Skipping invalid value (missing or misplaced '. '): {value}");
            return false;
        }

        var numberSpan = value.AsSpan(start: 0, length: separatorIndex);
        if (!long.TryParse(numberSpan, out var number))
        {
            Console.Error.WriteLine($"Warning: Skipping invalid value (cannot parse number): {value}");
            return false;
        }

        var text = value[(separatorIndex + 2)..];

        result = new Line(number, text, value);
        return true;
    }

    public int CompareTo(Line other)
    {
        var textComparison = string.Compare(Text, other.Text, StringComparison.Ordinal);
        return textComparison != 0 
            ? textComparison 
            : Number.CompareTo(other.Number);
    }

    public bool Equals(Line other)
    {
        return CompareTo(other) == 0;
    }
    
    public override string ToString() => RowValue;
    public override bool Equals(object? obj) => obj is Line other && CompareTo(other) == 0;
    public override int GetHashCode() => HashCode.Combine(Text, Number);
    
    public static bool operator ==(Line left, Line right) => left.Equals(right);
    public static bool operator !=(Line left, Line right) => !(left == right);
    public static bool operator <(Line left, Line right) => left.CompareTo(right) < 0;
    public static bool operator <=(Line left, Line right) => left.CompareTo(right) <= 0;
    public static bool operator >(Line left, Line right) => left.CompareTo(right) > 0;
    public static bool operator >=(Line left, Line right) => left.CompareTo(right) >= 0;
}