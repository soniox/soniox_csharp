using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Soniox.Client.FileUtils;

public static class AsyncFileReader
{
    public static async IAsyncEnumerable<byte[]> ReadFileChunks(
        string filePath,
        int bufferSize = 131072,
        [EnumeratorCancellation] CancellationToken cancellationToken = default(CancellationToken)
    )
    {
        await using var fileStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: bufferSize, useAsync: true
        );

        while (true)
        {
            byte[] buffer = new byte[bufferSize];
            int numRead = await fileStream.ReadAsync(buffer, cancellationToken);
            if (numRead == 0)
            {
                break;
            }
            Array.Resize(ref buffer, numRead);
            yield return buffer;
        }
    }
}
