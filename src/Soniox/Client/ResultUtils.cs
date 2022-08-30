using System.Diagnostics;
using Soniox.Proto.SpeechService;


namespace Soniox.Client.ResultUtils
{

    public static class UpdateResult
    {
        public static void Update(Result result, Result newResult)
        {
            Debug.Assert(result.Channel == newResult.Channel);
            while (result.Words.Count > 0 && !result.Words[result.Words.Count - 1].IsFinal)
            {
                result.Words.RemoveAt(result.Words.Count - 1);
            }
            result.Words.AddRange(newResult.Words);
            result.FinalProcTimeMs = newResult.FinalProcTimeMs;
            result.TotalProcTimeMs = newResult.TotalProcTimeMs;
            result.Speakers.Clear();
            result.Speakers.AddRange(newResult.Speakers);
        }
    }

}
