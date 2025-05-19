using BotSharp.Plugin.IoTServer.Models;
using BotSharp.Plugin.IoTServer.Repository.Entities;

namespace BotSharp.Plugin.IoTServer.Repository.LiteDB;

public class LiteDBIoTDeviceRepository : IIoTDeviceRepository
{
    private readonly LiteDBIoTDbContext _dbContext;

    public LiteDBIoTDeviceRepository(LiteDBIoTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 添加设备
    /// </summary>
    public async Task<int> AddAsync(IoTDeviceModel device)
    {
        var entity = MapToEntity(device);
        _dbContext.IoTDevices.Insert(entity);
        return await Task.FromResult(1);
    }

    /// <summary>
    /// 删除设备
    /// </summary>
    public async Task<int> DeleteAsync(IoTDeviceModel device)
    {
        bool deleted = _dbContext.IoTDevices.Delete(device.Id);
        return await Task.FromResult(deleted ? 1 : 0);
    }

    /// <summary>
    /// 查询设备信息
    /// </summary>
    public async Task<List<IoTDeviceModel>> GetListAsync(IoTDeviceModel device)
    {
        var query = _dbContext.IoTDevices.FindAll();

        // 应用筛选条件
        if (!string.IsNullOrEmpty(device.DeviceId))
        {
            query = query.Where(x => x.DeviceId == device.DeviceId);
        }

        if (!string.IsNullOrEmpty(device.AgentId))
        {
            query = query.Where(x => x.AgentId == device.AgentId);
        }

        if (!string.IsNullOrEmpty(device.State))
        {
            query = query.Where(x => x.State == device.State);
        }

        var result = query.ToList().Select(MapToModel).ToList();
        return await Task.FromResult(result);
    }

    /// <summary>
    /// 获取设备验证码
    /// </summary>
    public async Task<IoTDeviceModel> GetVerifyCodeAsync(IoTDeviceModel device)
    {
        var entity = _dbContext.IoTDevices.FindOne(x => x.DeviceId == device.DeviceId);
        if (entity == null)
        {
            return null;
        }

        return await Task.FromResult(MapToModel(entity));
    }

    /// <summary>
    /// 保存设备验证码
    /// </summary>
    public async Task<int> SaveVerifyCodeAsync(IoTDeviceModel device)
    {
        var entity = _dbContext.IoTDevices.FindOne(x => x.DeviceId == device.DeviceId);
        if (entity == null)
        {
            return await Task.FromResult(0);
        }

        entity.Code = device.Code;
        bool updated = _dbContext.IoTDevices.Update(entity);

        return await Task.FromResult(updated ? 1 : 0);
    }

    /// <summary>
    /// 更新设备信息
    /// </summary>
    public async Task<int> UpdateAsync(IoTDeviceModel device)
    {
        var entity = _dbContext.IoTDevices.FindOne(x => x.Id == device.Id);
        if (entity == null)
        {
            return await Task.FromResult(0);
        }

        // 更新实体属性
        entity = MapToEntity(device, entity);
        bool updated = _dbContext.IoTDevices.Update(entity);

        return await Task.FromResult(updated ? 1 : 0);
    }

    /// <summary>
    /// 更新设备验证码语音路径
    /// </summary>
    public async Task<int> UpdateCodePathAsync(IoTDeviceModel device)
    {
        var entity = _dbContext.IoTDevices.FindOne(x => x.DeviceId == device.DeviceId);
        if (entity == null)
        {
            return await Task.FromResult(0);
        }

        entity.AudioPath = device.AudioPath;
        bool updated = _dbContext.IoTDevices.Update(entity);

        return await Task.FromResult(updated ? 1 : 0);
    }

    /// <summary>
    /// 将模型转换为实体
    /// </summary>
    private IoTDevice MapToEntity(IoTDeviceModel model, IoTDevice entity = null)
    {
        entity = entity ?? new IoTDevice();

        entity.Id = model.Id;
        entity.AgentId = model.AgentId;
        entity.ConversationId = model.ConversationId;
        entity.DeviceId = model.DeviceId;
        entity.SessionId = model.SessionId;
        entity.DeviceName = model.DeviceName;
        entity.State = model.State;
        entity.TotalMessage = model.TotalMessage;
        entity.Code = model.Code;
        entity.AudioPath = model.AudioPath;
        entity.LastLogin = model.LastLogin;
        entity.WifiName = model.WifiName;
        entity.Ip = model.Ip;
        entity.ChipModelName = model.ChipModelName;
        entity.Version = model.Version;

        return entity;
    }

    /// <summary>
    /// 将实体转换为模型
    /// </summary>
    private IoTDeviceModel MapToModel(IoTDevice entity)
    {
        return new IoTDeviceModel
        {
            Id = entity.Id,
            AgentId = entity.AgentId,
            ConversationId = entity.ConversationId,
            DeviceId = entity.DeviceId,
            SessionId = entity.SessionId,
            DeviceName = entity.DeviceName,
            State = entity.State,
            TotalMessage = entity.TotalMessage,
            Code = entity.Code,
            AudioPath = entity.AudioPath,
            LastLogin = entity.LastLogin,
            WifiName = entity.WifiName,
            Ip = entity.Ip,
            ChipModelName = entity.ChipModelName,
            Version = entity.Version
        };
    }
}
