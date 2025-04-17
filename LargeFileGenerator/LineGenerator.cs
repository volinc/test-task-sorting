using System.Security.Cryptography;
using LargeFileShared;

namespace LargeFileGenerator;

public sealed class LineGenerator : IDisposable, ILineGenerator
{
    // Can be extracted into configuration
    private const int TextMaxLength = 100;
    private const string AllowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    
    private readonly RandomNumberGenerator _randomNumberGenerator = RandomNumberGenerator.Create();
    
    public Line Generate()
    {
        var number = Random.Shared.NextInt64();
        var textLength = Random.Shared.Next(TextMaxLength);
        var text = GenerateRandomText(textLength);
        return new Line(number, text);
    }
    
    private string GenerateRandomText(int length)
    {
        var bytes = new byte[length];
        _randomNumberGenerator.GetBytes(bytes);

        var result = new char[length];
        for (var i = 0; i < length; i++)
            result[i] = AllowedChars[bytes[i] % AllowedChars.Length];

        return new string(result);
    }

    public void Dispose()
    {
        _randomNumberGenerator.Dispose();
    }
}