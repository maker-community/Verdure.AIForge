using BotSharp.Plugin.IoTServer.Models;

namespace BotSharp.Plugin.IoTServer.Services.Manager;
/// <summary>
/// 设备查询/更新
/// </summary>
public interface IIoTDeviceService
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
    /// <param name="device">设备</param>
    /// <returns>设备列表</returns>
    Task<List<IoTDeviceModel>> QueryAsync(IoTDeviceModel device);

    /// <summary>
    /// 查询验证码
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>设备</returns>
    Task<IoTDeviceModel> QueryVerifyCodeAsync(IoTDeviceModel device);

    /// <summary>
    /// 查询并生成验证码
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>设备</returns>
    Task<IoTDeviceModel> GenerateCodeAsync(IoTDeviceModel device);

    /// <summary>
    /// 关系设备验证码语音路径
    /// </summary>
    /// <param name="device">设备</param>
    /// <returns>影响的行数</returns>
    Task<int> UpdateCodeAsync(IoTDeviceModel device);

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
