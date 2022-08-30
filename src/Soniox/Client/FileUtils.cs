using Grpc.Core;

namespace Soniox.Client.FileUtils
{
    public class AsyncFileReader : IAsyncDisposable, IDisposable, IAsyncStreamReader<byte[]>
    {
        private int _bufferSize;
        private FileStream _fileStream;
        private byte[]? _currentBuffer = null;

        public AsyncFileReader(string filePath, int bufferSize = 4096)
        {
            _bufferSize = bufferSize;
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: bufferSize, useAsync: true);
        }

        public ValueTask DisposeAsync()
        {
            return _fileStream.DisposeAsync();
        }

        public void Dispose()
        {
            _fileStream.Dispose();
        }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[_bufferSize];
            int numRead = await _fileStream.ReadAsync(buffer, cancellationToken);
            if (numRead == 0)
            {
                _currentBuffer = null;
                return false;
            }
            Array.Resize(ref buffer, numRead);
            _currentBuffer = buffer;
            return true;
        }

        public byte[] Current
        {
            get
            {
                if (_currentBuffer == null)
                {
                    throw new InvalidOperationException("AsyncFileReader.Current accessed but no buffer is ready");
                }
                return _currentBuffer;
            }
        }
    }

}
