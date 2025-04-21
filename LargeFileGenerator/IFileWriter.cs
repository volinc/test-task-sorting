using System.Threading.Channels;
using LargeFileShared;

namespace LargeFileGenerator;

// E.g. consumer
public interface IFileWriter
{
    Task WriteToFileAsync(ChannelReader<Line[]> reader, string filePath, long targetSizeBytes, CancellationTokenSource cts);
}