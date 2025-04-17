using LargeFileGenerator;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LargeFileGenerator.Tests")]

ILineGenerator lineGenerator = new LineGenerator();
var fileGeneratorSettings = new FileGeneratorSettings
{
    TargetSizeBytes = 10L * 1024 * 1024
};
IFileGenerator fileGenerator = new FileGenerator(lineGenerator, fileGeneratorSettings);
await fileGenerator.GenerateAsync();