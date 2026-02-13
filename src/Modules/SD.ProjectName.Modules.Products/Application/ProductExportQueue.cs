using System.Threading.Channels;

namespace SD.ProjectName.Modules.Products.Application
{
    public class ProductExportQueue
    {
        private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

        public async ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            await _channel.Writer.WriteAsync(jobId, cancellationToken);
        }

        public IAsyncEnumerable<Guid> DequeueAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }
    }
}
