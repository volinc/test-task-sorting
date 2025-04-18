namespace LargeFileGenerator;

public sealed class LineGeneratorSettings
{
    public int TextMinLength { get; init; } = 5;
    public int TextMaxLength { get; init; } = 100;
    public string AllowedChars { get; init; } = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
}