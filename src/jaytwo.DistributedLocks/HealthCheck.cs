namespace jaytwo.DistributedLocks;

public class HealthCheck
{
    public static async Task<IReadOnlyDictionary<string, object>> RunHealthCheckAsync(Func<Task<IReadOnlyDictionary<string, object>>> callback)
    {
        try
        {
            return await callback();
        }
        catch (Exception ex)
        {
            var result = new Dictionary<string, object>();
            result.Add("exception", ex.Message);

            return result;
        }
    }
}
