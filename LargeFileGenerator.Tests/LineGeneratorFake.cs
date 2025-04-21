using LargeFileShared;

namespace LargeFileGenerator.Tests;

public sealed class LineGeneratorFake : ILineGenerator
{
    public Line Generate() => new(0, string.Empty);

    public Line Generate(Line line) => new(1, line.Text);
}