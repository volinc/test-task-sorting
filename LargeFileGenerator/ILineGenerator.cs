using LargeFileShared;

namespace LargeFileGenerator;

public interface ILineGenerator
{
    Line Generate();
    Line Generate(Line line);
}