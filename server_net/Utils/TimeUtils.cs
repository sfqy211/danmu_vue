namespace Danmu.Server.Utils;

public static class TimeUtils
{
    public static long? ToUnixMilliseconds(DateTime? dateTime)
    {
        return dateTime.HasValue ? ToUnixMilliseconds(dateTime.Value) : null;
    }

    public static long? ToUnixMilliseconds(DateTime dateTime)
    {
        if (dateTime == default)
        {
            return null;
        }

        return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
    }
}
