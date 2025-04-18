using LargeFileGenerator;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LargeFileGenerator.Tests")]

var outputFile = args[0];
outputFile = string.IsNullOrWhiteSpace(outputFile) 
    ? FileGeneratorSettings.DefaultOutputFile 
    : outputFile;

var lineGeneratorSettings = new LineGeneratorSettings
{
    TextMinLength = 5,
};
ILineGenerator lineGenerator = new LineGenerator(lineGeneratorSettings);
var fileGeneratorSettings = new FileGeneratorSettings
{
    OutputFile = outputFile,
    TargetSizeBytes = 10L * 1024 * 1024 * 1024 // 10GB
};
IFileGenerator fileGenerator = new FileGenerator(lineGenerator, fileGeneratorSettings);
await fileGenerator.GenerateAsync();