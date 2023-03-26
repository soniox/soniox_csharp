using System.Text;
using System.Collections.Generic;
using Soniox.Client.Proto;

namespace Soniox.Types;

public class CompleteResultType { }

public class SingleResult : CompleteResultType
{
    public SingleResult(Result result)
    {
        Result = result;
    }

    public Result Result { get; private set; }

    public override string ToString()
    {
        return "SingleResult: " + Result.ToString();
    }
}

public class SeparateRecognitionResult : CompleteResultType
{
    public SeparateRecognitionResult(List<Result> channelResults)
    {
        ChannelResults = channelResults;
    }

    public List<Result> ChannelResults { get; private set; }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("SeparateRecognitionResult:");
        foreach (var result in ChannelResults)
        {
            builder.Append($"\nChannel {result.Channel}: ");
            builder.Append(result.ToString());
        }
        return builder.ToString();
    }
}