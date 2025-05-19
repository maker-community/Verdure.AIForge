namespace BotSharp.Plugin.IoTServer.Models;

/// <summary>
/// 基础实体类
/// </summary>
/// <author>Joey</author>
public class Base
{
    /// <summary>
    /// 分页
    /// </summary>
    private int? start;

    private int? limit;

    /// <summary>
    /// 创建日期
    /// </summary>
    private DateTime? createTime;

    /// <summary>
    /// 用户ID
    /// </summary>
    private int? userId;

    /// <summary>
    /// 开始/结束时间筛选
    /// </summary>
    private string startTime;

    private string endTime;

    public int? Start
    {
        get { return start; }
        set { start = value; }
    }

    public int? Limit
    {
        get { return limit; }
        set { limit = value; }
    }

    public int? UserId
    {
        get { return userId; }
        set { userId = value; }
    }

    public string StartTime
    {
        get { return startTime; }
        set { startTime = value; }
    }

    public string EndTime
    {
        get { return endTime; }
        set { endTime = value; }
    }

    public DateTime? CreateTime
    {
        get { return createTime; }
        set { createTime = value; }
    }
}