using BotSharp.Plugin.IoTServer.Models;
using BotSharp.Plugin.IoTServer.Repository.Entities;

namespace BotSharp.Plugin.IoTServer.Repository;

/// <summary>
/// IoT设备数据仓储接口
/// </summary>
public interface IIoTDeviceRepository
{
    /// <summary>
    /// 添加设备
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>影响的行数</returns>
    Task<int> AddAsync(IoTDeviceModel device);

    /// <summary>
    /// 查询设备信息
    /// </summary>
    /// <param name="device">设备查询条件</param>
    /// <returns>设备列表</returns>
    Task<List<IoTDeviceModel>> GetListAsync(IoTDeviceModel device);

    /// <summary>
    /// 获取设备验证码
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>包含验证码的设备信息</returns>
    Task<IoTDeviceModel> GetVerifyCodeAsync(IoTDeviceModel device);

    /// <summary>
    /// 保存设备验证码
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>影响的行数</returns>
    Task<int> SaveVerifyCodeAsync(IoTDeviceModel device);

    /// <summary>
    /// 更新设备验证码语音路径
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>影响的行数</returns>
    Task<int> UpdateCodePathAsync(IoTDeviceModel device);

    /// <summary>
    /// 更新设备信息
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>影响的行数</returns>
    Task<int> UpdateAsync(IoTDeviceModel device);

    /// <summary>
    /// 删除设备
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>影响的行数</returns>
    Task<int> DeleteAsync(IoTDeviceModel device);
}
