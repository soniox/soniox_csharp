using Soniox.Proto.SpeechService;
using Soniox.Types;
using Soniox.Client;

using var client = new SpeechClient();

var fileId = await client.TranscribeFileAsync(
    "../test_data_files/test_audio_long.flac",
    new TranscriptionConfig { });

Console.WriteLine($"File ID: {fileId}");

TranscribeAsyncFileStatus status;
while (true)
{
    Console.WriteLine("Calling GetTranscribeAsyncFileStatus.");
    status = await client.GetTranscribeAsyncFileStatus(fileId);
    if (status.Status is "COMPLETED" or "FAILED")
    {
        break;
    }
    await Task.Delay(2000);
}

if (status.Status == "COMPLETED")
{
    Console.WriteLine("Calling GetTranscribeAsyncResult");
    var completeResult = await client.GetTranscribeAsyncResult(fileId);
    Result result = (completeResult as SingleResult)!.Result;
    Console.WriteLine(string.Join(" ", result.Words.Select(word => word.Text).ToList()));
}

Console.WriteLine("Calling DeleteTranscribeAsyncFile.");
await client.DeleteTranscribeAsyncFile(fileId);