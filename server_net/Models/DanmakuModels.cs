namespace Danmu.Server.Models;

public class DanmakuMessage
{
    public string Type { get; set; } = "comment";
    public long Timestamp { get; set; }
    public string? Text { get; set; }
    public double? Price { get; set; }
    public bool IsPriceTotal { get; set; }
    public string? Name { get; set; } // Gift/Guard name
    public int? Count { get; set; }
    public int? GuardLevel { get; set; }
    public Sender Sender { get; set; } = new();
}

public class RecordedDanmakuEvent
{
    public string Kind { get; set; } = "event";
    public string Version { get; set; } = "danmu-event-v1";
    public string Type { get; set; } = "unknown";
    public long Timestamp { get; set; }
    public string User { get; set; } = "";
    public string Uid { get; set; } = "";
    public string? Text { get; set; }
    public string? Name { get; set; }
    public int Count { get; set; } = 1;
    public double? Price { get; set; }
    public bool IsPriceTotal { get; set; }
    public int? GuardLevel { get; set; }
    public string? RawCommand { get; set; }
}

public class Sender
{
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
}

public class AnalysisResult
{
    public int TotalCount { get; set; }
    public Dictionary<string, UserStat> UserStats { get; set; } = new();
    public List<List<object>> Timeline { get; set; } = new(); // [timestamp, count]
    public List<KeywordStat> TopKeywords { get; set; } = new();
}

public class UserStat
{
    public int Count { get; set; }
    public int ScCount { get; set; }
    public string Uid { get; set; } = "";
}

public class KeywordStat
{
    public string Word { get; set; } = "";
    public int Count { get; set; }
}

public class GiftAnalysisResult
{
    public double TotalPrice { get; set; }
    public Dictionary<string, GiftUserStat> UserStats { get; set; } = new();
    public List<List<object>> Timeline { get; set; } = new(); // [timestamp, price]
    public List<GiftStat> TopGifts { get; set; } = new();
    public GuardStat GuardStats { get; set; } = new();
}

public class GiftUserStat
{
    public double TotalPrice { get; set; }
    public double GiftPrice { get; set; }
    public double ScPrice { get; set; }
    public double GuardPrice { get; set; }
    public string Uid { get; set; } = "";
}

public class GiftStat
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public double Price { get; set; }
}

public class GuardStat
{
    public double TotalPrice { get; set; }
    public int Count { get; set; }
    public Dictionary<string, int> CountByLevel { get; set; } = new()
    {
        { "1", 0 }, { "2", 0 }, { "3", 0 }
    };
}
