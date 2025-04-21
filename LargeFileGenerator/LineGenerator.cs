using System.Security.Cryptography;
using LargeFileShared;

namespace LargeFileGenerator;

public sealed class LineGenerator : IDisposable, ILineGenerator
{
    private readonly LineGeneratorSettings _settings;

    private readonly RandomNumberGenerator _randomNumberGenerator = RandomNumberGenerator.Create();

    public LineGenerator(LineGeneratorSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }
    
    public Line Generate()
    {
        var number = Random.Shared.NextInt64();
        var textLength = Random.Shared.Next(_settings.TextMinLength, _settings.TextMaxLength);
        var text = GenerateRandomText(textLength);
        return new Line(number, text);
    }

    public Line Generate(Line line)
    {
        var number = Random.Shared.NextInt64();
        return new Line(number, line.Text);
    }
    
    private string GenerateRandomText(int length)
    {
        var bytes = new byte[length];
        _randomNumberGenerator.GetBytes(bytes);

        var result = new char[length];
        for (var i = 0; i < length; i++)
            result[i] = _settings.AllowedChars[bytes[i] % _settings.AllowedChars.Length];

        return new string(result);
    }

    public void Dispose()
    {
        _randomNumberGenerator.Dispose();
    }
}