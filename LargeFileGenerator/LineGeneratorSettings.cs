namespace LargeFileGenerator;

public sealed class LineGeneratorSettings
{
    public int TextMinLength { get; set; } = 5;
    public int TextMaxLength { get; set; } = 1000;
    public string AllowedChars { get; set; } = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
}