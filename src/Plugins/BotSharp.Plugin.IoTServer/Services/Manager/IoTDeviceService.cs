using BotSharp.Plugin.IoTServer.Models;
using BotSharp.Plugin.IoTServer.Repository;

namespace BotSharp.Plugin.IoTServer.Services.Manager;

public class IoTDeviceService : IIoTDeviceService
{
    private readonly IIoTDeviceRepository _deviceRepository;

    public IoTDeviceService(IIoTDeviceRepository deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    public async Task<int> AddAsync(IoTDeviceModel device)
    {
        return await _deviceRepository.AddAsync(device);
    }

    public async Task<int> DeleteAsync(IoTDeviceModel device)
    {
        return await _deviceRepository.DeleteAsync(device);
    }

    public async Task<IoTDeviceModel> GenerateCodeAsync(IoTDeviceModel device)
    {
        // 保存验证码到设备，然后返回设备模型
        await _deviceRepository.SaveVerifyCodeAsync(device);
        return device;
    }

    public async Task<List<IoTDeviceModel>> QueryAsync(IoTDeviceModel device)
    {
        return await _deviceRepository.GetListAsync(device);
    }

    public async Task<IoTDeviceModel> QueryVerifyCodeAsync(IoTDeviceModel device)
    {
        return await _deviceRepository.GetVerifyCodeAsync(device);
    }

    public async Task<int> UpdateAsync(IoTDeviceModel device)
    {
        return await _deviceRepository.UpdateAsync(device);
    }

    public async Task<int> UpdateCodeAsync(IoTDeviceModel device)
    {
        return await _deviceRepository.UpdateCodePathAsync(device);
    }
}
