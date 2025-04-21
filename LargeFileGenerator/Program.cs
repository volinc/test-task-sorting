using LargeFileGenerator;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LargeFileGenerator.Tests")]

var outputFile = args.Length > 0 ? args[0] : null;
outputFile = string.IsNullOrWhiteSpace(outputFile) 
    ? FileGeneratorSettings.DefaultOutputFile 
    : outputFile;

var targetSizeBytesString = args.Length > 1 ? args[1] : null;
if (!long.TryParse(targetSizeBytesString, out var targetSizeBytes))
    targetSizeBytes = FileGeneratorSettings.DefaultTargetSizeBytes;
    
var lineGeneratorSettings = new LineGeneratorSettings
{
    TextMinLength = 5,
    TextMaxLength = 1000,
};
var lineGenerator = new LineGenerator(lineGeneratorSettings);
var fileGeneratorSettings = new FileGeneratorSettings
{
    OutputFilePath = outputFile,
    TargetSizeBytes = targetSizeBytes
};
var fileWriter = new FileWriter();
var fileGenerator = new FileGenerator(lineGenerator, fileGeneratorSettings, fileWriter);
await fileGenerator.GenerateAsync();