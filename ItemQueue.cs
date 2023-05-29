using TNRD.Zeepkist.GTR.DTOs.Rabbit;

namespace TNRD.Zeepkist.GTR.Backend.WorldRecordProcessor;

internal class ItemQueue
{
    private readonly AutoResetEvent resetEvent = new(true);
    private readonly Dictionary<int, List<ProcessWorldRecordRequest>> levelToRequests = new();

    public bool HasItems()
    {
        return levelToRequests.Values.Any(x => x.Count > 0);
    }

    public void AddToQueue(ProcessWorldRecordRequest item)
    {
        resetEvent.WaitOne();

        try
        {
            if (!levelToRequests.ContainsKey(item.Level))
                levelToRequests.Add(item.Level, new List<ProcessWorldRecordRequest>());

            levelToRequests[item.Level].Add(item);
        }
        finally
        {
            resetEvent.Set();
        }
    }

    public Dictionary<int, List<ProcessWorldRecordRequest>> GetItemsFromQueue()
    {
        resetEvent.WaitOne();

        try
        {
            Dictionary<int, List<ProcessWorldRecordRequest>> copy =
                levelToRequests.ToDictionary(x => x.Key, y => y.Value);
            levelToRequests.Clear();
            return copy;
        }
        finally

        {
            resetEvent.Set();
        }
    }
}
