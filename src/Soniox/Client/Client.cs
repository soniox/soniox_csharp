using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Grpc.Net.Client;
using Soniox.Types;
using Soniox.Client.Proto;
using Soniox.Client.FileUtils;
using Soniox.Client.ResultUtils;

namespace Soniox.Client;

public class SpeechClient : IDisposable
{
    public static string DEFAULT_API_HOST = "https://api.soniox.com:443";

    public static string GetApiHost(string? apiHost = null)
    {
        string testApiHost = apiHost ?? "";
        if (testApiHost != "")
        {
            return testApiHost;
        }

        testApiHost = Environment.GetEnvironmentVariable("SONIOX_API_HOST") ?? "";
        if (testApiHost != "")
        {
            return testApiHost;
        }

        return DEFAULT_API_HOST;
    }

    public static string GetApiKey(string? apiKey = null)
    {
        string testApiKey = apiKey ?? "";
        if (testApiKey != "")
        {
            return testApiKey;
        }

        testApiKey = Environment.GetEnvironmentVariable("SONIOX_API_KEY") ?? "";
        if (testApiKey != "")
        {
            return testApiKey;
        }

        throw new Exception(
            "Soniox API key not specified. Please specify it using the " +
            "SONIOX_API_KEY environment variable or the ApiKey parameter to Client()."
        );
    }

    public static GrpcChannel CreateChannel(string? apiHost = null)
    {
        string theApiHost = GetApiHost(apiHost);
        return GrpcChannel.ForAddress(theApiHost);
    }

    private readonly GrpcChannel _channel;

    public SpeechClient(
        string? apiKey = null,
        string? apiHost = null
    )
    {
        ApiKey = GetApiKey(apiKey);
        _channel = CreateChannel(apiHost);
        ServiceClient = new SpeechService.SpeechServiceClient(_channel);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }

    public string ApiKey { get; }

    public SpeechService.SpeechServiceClient ServiceClient { get; }

    public async Task<CompleteResultType> Transcribe(
        byte[] audio,
        TranscriptionConfig config,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var request = new TranscribeRequest
        {
            ApiKey = ApiKey,
            Config = config,
            Audio = ByteString.CopyFrom(audio)
        };
        var response = await ServiceClient.TranscribeAsync(request, cancellationToken: cancellationToken);

        if (config.EnableSeparateRecognitionPerChannel)
        {
            if (response.Result != null)
            {
                throw new Exception("response.Result is not null (separate recognition)");
            }

            if (response.ChannelResults.Count == 0)
            {
                throw new Exception("response.ChannelResults.Count is empty (separate recognition)");
            }

            return new SeparateRecognitionResult(response.ChannelResults.ToList());
        }
        else
        {
            if (response.Result == null)
            {
                throw new Exception("response.Result is null (not separate recognition)");
            }

            if (response.ChannelResults.Count != 0)
            {
                throw new Exception("response.ChannelResults.Count is not empty (not separate recognition)");
            }

            return new SingleResult(response.Result);
        }
    }

    public async Task<CompleteResultType> TranscribeFileShort(
        string audioFilePath,
        TranscriptionConfig config,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var audio = await File.ReadAllBytesAsync(audioFilePath, cancellationToken);

        return await Transcribe(audio, config, cancellationToken);
    }

    public async IAsyncEnumerable<Result> TranscribeStream(
        IAsyncEnumerable<byte[]> audioChunksEnumerable,
        TranscriptionConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default(CancellationToken))
    {
        using var call = ServiceClient.TranscribeStream(cancellationToken: cancellationToken);

        var firstRequest = new TranscribeStreamRequest
        {
            ApiKey = ApiKey,
            Config = config,
        };
        await call.RequestStream.WriteAsync(firstRequest, cancellationToken);

        CancellationTokenSource intWriteCancellationTokenSource = new CancellationTokenSource();
        CancellationTokenSource intReadCancellationTokenSource = new CancellationTokenSource();

        using var linkedWriteCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            intWriteCancellationTokenSource.Token, cancellationToken
        );
        using var linkedReadCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            intReadCancellationTokenSource.Token, cancellationToken
        );

        var writeCancellationToken = linkedWriteCancellationTokenSource.Token;
        var readCancellationToken = linkedReadCancellationTokenSource.Token;

        var writeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (byte[] audio in audioChunksEnumerable.WithCancellation(writeCancellationToken))
                {
                    var audioRequest = new TranscribeStreamRequest
                    {
                        Audio = ByteString.CopyFrom(audio),
                    };
                    await call.RequestStream.WriteAsync(audioRequest, writeCancellationToken);
                }
                await call.RequestStream.CompleteAsync();
            }
            catch
            {
                intReadCancellationTokenSource.Cancel();
                throw;
            }
        }, writeCancellationToken);

        bool ok = false;
        try
        {
            while (await call.ResponseStream.MoveNext(readCancellationToken))
            {
                var response = call.ResponseStream.Current;
                var result = response.Result;
                if (result != null)
                {
                    yield return result;
                }
            }
            ok = true;
        }
        finally
        {
            if (ok || intReadCancellationTokenSource.IsCancellationRequested)
            {
                await writeTask;
            }
            else
            {
                intWriteCancellationTokenSource.Cancel();
                try
                {
                    await writeTask;
                }
                catch { }
            }
        }
    }

    public async Task<CompleteResultType> TranscribeCompleteStream(
        IAsyncEnumerable<byte[]> audioChunksEnumerable,
        TranscriptionConfig config,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var resultsEnumerable = TranscribeStream(audioChunksEnumerable, config).
            WithCancellation(cancellationToken);

        if (config.EnableSeparateRecognitionPerChannel)
        {
            Dictionary<int, Result> channelResults = new Dictionary<int, Result>();

            await foreach (Result partialResult in resultsEnumerable)
            {
                int channel = partialResult.Channel;
                if (!channelResults.ContainsKey(channel))
                {
                    channelResults.Add(channel, partialResult);
                }
                else
                {
                    UpdateResult.Update(channelResults[channel], partialResult);
                }
            }

            var results = channelResults.Values.ToList();
            results.Sort((x, y) => x.Channel.CompareTo(y.Channel));
            return new SeparateRecognitionResult(results);
        }
        else
        {
            Result result = new Result();

            await foreach (Result partialResult in resultsEnumerable)
            {
                UpdateResult.Update(result, partialResult);
            }

            return new SingleResult(result);
        }
    }

    public async Task<CompleteResultType> TranscribeFileStream(
        string audioFilePath,
        TranscriptionConfig config,
        int chunkSize = 131072,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var audioChunksEnumerable = AsyncFileReader.ReadFileChunks(audioFilePath, chunkSize);

        return await TranscribeCompleteStream(audioChunksEnumerable, config, cancellationToken);
    }

    public async Task<string> TranscribeAsync(
        IAsyncEnumerable<byte[]> audioChunksEnumerable,
        TranscriptionConfig config,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        using var call = ServiceClient.TranscribeAsync(cancellationToken: cancellationToken);

        var firstRequest = new TranscribeAsyncRequest
        {
            ApiKey = ApiKey,
            Config = config,
            EnableEof = true,
        };
        await call.RequestStream.WriteAsync(firstRequest, cancellationToken);

        await foreach (byte[] audio in audioChunksEnumerable.WithCancellation(cancellationToken))
        {
            var audioRequest = new TranscribeAsyncRequest
            {
                Audio = ByteString.CopyFrom(audio)
            };
            await call.RequestStream.WriteAsync(audioRequest, cancellationToken);
        }

        var eofRequest = new TranscribeAsyncRequest
        {
            Eof = true,
        };
        await call.RequestStream.WriteAsync(eofRequest, cancellationToken);

        await call.RequestStream.CompleteAsync();

        var response = await call.ResponseAsync;

        return response.FileId;
    }

    public async Task<string> TranscribeFileAsync(
        string audioFilePath,
        TranscriptionConfig config,
        int chunkSize = 131072,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var audioChunksEnumerable = AsyncFileReader.ReadFileChunks(audioFilePath, chunkSize);

        return await TranscribeAsync(audioChunksEnumerable, config, cancellationToken);
    }

    public async Task<TranscribeAsyncFileStatus> GetTranscribeAsyncFileStatus(
        string fileId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var request = new GetTranscribeAsyncStatusRequest
        {
            ApiKey = ApiKey,
            FileId = fileId
        };

        var response = await ServiceClient.GetTranscribeAsyncStatusAsync(
            request, cancellationToken: cancellationToken);

        if (response.Files.Count != 1)
        {
            throw new Exception("Unexpected number of files returned.");
        }

        return response.Files[0];
    }

    public async Task<List<TranscribeAsyncFileStatus>> GetTranscribeAsyncAllFilesStatus(
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var request = new GetTranscribeAsyncStatusRequest
        {
            ApiKey = ApiKey
        };

        var response = await ServiceClient.GetTranscribeAsyncStatusAsync(
            request, cancellationToken: cancellationToken);

        return response.Files.ToList();
    }

    public async Task<CompleteResultType> GetTranscribeAsyncResult(
        string fileId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var request = new GetTranscribeAsyncResultRequest
        {
            ApiKey = ApiKey,
            FileId = fileId
        };

        Result? result = null;
        Dictionary<int, Result>? channelResults = null;

        using (var call = ServiceClient.GetTranscribeAsyncResult(request, cancellationToken: cancellationToken))
        {
            while (await call.ResponseStream.MoveNext(cancellationToken))
            {
                var response = call.ResponseStream.Current;

                if (result == null && channelResults == null)
                {
                    if (response.SeparateRecognitionPerChannel)
                    {
                        channelResults = new Dictionary<int, Result>();
                    }
                }
                else
                {
                    if (response.SeparateRecognitionPerChannel)
                    {
                        Debug.Assert(result == null);
                        Debug.Assert(channelResults != null);
                    }
                    else
                    {
                        Debug.Assert(result != null);
                        Debug.Assert(channelResults == null);
                    }
                }

                if (response.SeparateRecognitionPerChannel)
                {
                    int channel = response.Result.Channel;
                    if (!channelResults!.ContainsKey(channel))
                    {
                        channelResults.Add(channel, response.Result);
                    }
                    else
                    {
                        UpdateResult.Update(channelResults[channel], response.Result);
                    }
                }
                else
                {
                    if (result == null)
                    {
                        result = response.Result;
                    }
                    else
                    {
                        UpdateResult.Update(result, response.Result);
                    }
                }
            }
        }

        if (result == null && channelResults == null)
        {
            throw new Exception("Did not receive any result from GetTranscribeAsyncResult");
        }

        if (result != null)
        {
            return new SingleResult(result);
        }
        else
        {
            var results = channelResults!.Values.ToList();
            results.Sort((x, y) => x.Channel.CompareTo(y.Channel));
            return new SeparateRecognitionResult(results);
        }
    }

    public async Task DeleteTranscribeAsyncFile(
        string fileId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var request = new DeleteTranscribeAsyncFileRequest
        {
            ApiKey = ApiKey,
            FileId = fileId
        };

        await ServiceClient.DeleteTranscribeAsyncFileAsync(
            request, cancellationToken: cancellationToken);
    }

    // Sync wrappers:

    public CompleteResultType SyncTranscribe(
        byte[] audio,
        TranscriptionConfig config,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.Run<CompleteResultType>(async () =>
            await Transcribe(audio, config, cancellationToken), cancellationToken).GetAwaiter().GetResult();
    }

    public CompleteResultType SyncTranscribeFileShort(
        string audioFilePath,
        TranscriptionConfig config,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.Run<CompleteResultType>(async () =>
                await TranscribeFileShort(audioFilePath, config, cancellationToken), cancellationToken).GetAwaiter()
            .GetResult();
    }

    public string SyncTranscribeFileAsync(
        string audioFilePath,
        TranscriptionConfig config,
        int chunkSize = 4096,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.Run<string>(async () =>
                await TranscribeFileAsync(audioFilePath, config, chunkSize, cancellationToken), cancellationToken)
            .GetAwaiter().GetResult();
    }

    public TranscribeAsyncFileStatus SyncGetTranscribeAsyncFileStatus(
        string fileId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.Run<TranscribeAsyncFileStatus>(async () =>
            await GetTranscribeAsyncFileStatus(fileId, cancellationToken), cancellationToken).GetAwaiter().GetResult();
    }

    public List<TranscribeAsyncFileStatus> SyncGetTranscribeAsyncAllFilesStatus(
        CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.Run<List<TranscribeAsyncFileStatus>>(async () =>
            await GetTranscribeAsyncAllFilesStatus(cancellationToken), cancellationToken).GetAwaiter().GetResult();
    }

    public CompleteResultType SyncGetTranscribeAsyncResult(
        string fileId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.Run<CompleteResultType>(async () =>
            await GetTranscribeAsyncResult(fileId, cancellationToken), cancellationToken).GetAwaiter().GetResult();
    }

    public void SyncDeleteTranscribeAsyncFile(
        string fileId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        Task.Run(async () =>
            await DeleteTranscribeAsyncFile(fileId, cancellationToken), cancellationToken).GetAwaiter().GetResult();
    }
}