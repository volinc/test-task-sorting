using System.Threading.Channels;
using LargeFileShared;

namespace LargeFileGenerator;

public interface ILineProducer
{
    Task ProduceAsync(ChannelWriter<Line[]> channelWriter, CancellationToken cancellationToken);
}