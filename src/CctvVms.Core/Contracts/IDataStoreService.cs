using CctvVms.Core.Domain;

namespace CctvVms.Core.Contracts;

public interface IDataStoreService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceEntity>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CameraEntity>> GetCamerasByDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CameraEntity>> GetAllCamerasAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaybackRecordEntity>> GetPlaybackRecordsAsync(string cameraId, DateTime rangeStartUtc, DateTime rangeEndUtc, CancellationToken cancellationToken = default);

    Task UpsertDeviceAsync(DeviceEntity device, CancellationToken cancellationToken = default);
    Task DeleteDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    Task UpsertCameraAsync(CameraEntity camera, CancellationToken cancellationToken = default);
    Task DeleteCameraAsync(string cameraId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LayoutEntity>> GetLayoutsAsync(CancellationToken cancellationToken = default);
    Task UpsertLayoutAsync(LayoutEntity layout, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetUserSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveUserSettingAsync(string key, string value, CancellationToken cancellationToken = default);
}
