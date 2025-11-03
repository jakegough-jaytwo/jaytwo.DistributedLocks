using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

internal static class EventIds
{
    private const string Prefix = "DISTRIBUTED_LOCK:";

    private static EventId EventIdFromString(string eventName)
        => new EventId(HashToInt(eventName), eventName);

    private static int HashToInt(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

        // Make non-negative by clearing the sign bit
        return (int)(BitConverter.ToUInt32(hashBytes, 0) & 0x7FFF_FFFF);
    }

    public static class ProviderEvents
    {
        public static readonly EventId DefaultNamespaceApplied = EventIdFromString(Prefix + "DEFAULT_NAMESPACE_APPLIED");

        public static readonly EventId DefaultNamespaceAppliedWithoutEnvironment = EventIdFromString(Prefix + "DEFAULT_NAMESPACE_APPLIED_WITHOUT_ENVIRONMENT");
    }

    public static class LockEvents
    {
        public static readonly EventId Requested = EventIdFromString(Prefix + "REQUESTED");

        public static readonly EventId Acquired = EventIdFromString(Prefix + "ACQUIRED");

        public static readonly EventId Error = EventIdFromString(Prefix + "ERROR");

        public static readonly EventId TimedOut = EventIdFromString(Prefix + "TIMED_OUT");

        public static readonly EventId Cancelled = EventIdFromString(Prefix + "CANCELLED");

        public static readonly EventId Released = EventIdFromString(Prefix + "RELEASED");

        public static readonly EventId ReleaseFailed = EventIdFromString(Prefix + "RELEASE_FAILED");
    }
}
