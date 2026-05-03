namespace Danmu.Server.Services;

public static class BilibiliApiUrls
{
    public const string ApiBase = "https://api.bilibili.com";
    public const string LiveApiBase = "https://api.live.bilibili.com";
    public const string PassportBase = "https://passport.bilibili.com";
    public const string BilibiliBase = "https://www.bilibili.com";
    public const string LiveBase = "https://live.bilibili.com";
    public const string SpaceBase = "https://space.bilibili.com";

    public const string WebNav = ApiBase + "/x/web-interface/nav";
    public const string WebCard = ApiBase + "/x/web-interface/card";
    public const string RelationStat = ApiBase + "/x/relation/stat";
    public const string SpaceNavNum = ApiBase + "/x/space/navnum";
    public const string BiliTicket = ApiBase + "/bapis/bilibili.api.ticket.v1.Ticket/GenWebTicket";

    public const string RoomInit = LiveApiBase + "/room/v1/Room/room_init";
    public const string RoomInfo = LiveApiBase + "/room/v1/Room/get_info";
    public const string DanmuInfo = LiveApiBase + "/xlive/web-room/v1/index/getDanmuInfo";
    public const string DanmuConf = LiveApiBase + "/room/v1/Danmu/getConf";
    public const string AnchorInRoom = LiveApiBase + "/live_user/v1/UserInfo/get_anchor_in_room";
    public const string MasterInfo = LiveApiBase + "/live_user/v1/Master/info";
    public const string GuardTopList = LiveApiBase + "/xlive/app-room/v1/guardTab/topList";

    public const string PassportTvAuthCode = "http://passport.bilibili.com/x/passport-tv-login/qrcode/auth_code";
    public const string PassportTvPoll = "http://passport.bilibili.com/x/passport-tv-login/qrcode/poll";
    public const string PassportOauthRefresh = PassportBase + "/x/passport-login/oauth2/refresh_token";
    public const string PassportWebKey = PassportBase + "/x/passport-login/web/key";
    public const string PassportWebCookieRefresh = PassportBase + "/x/passport-login/web/cookie/refresh";
    public const string PassportWebConfirmRefresh = PassportBase + "/x/passport-login/web/confirm/refresh";
}
