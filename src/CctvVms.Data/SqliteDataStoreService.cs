using CctvVms.Core.Contracts;
using CctvVms.Core.Domain;
using Microsoft.Data.Sqlite;

namespace CctvVms.Data;

public sealed class SqliteDataStoreService : IDataStoreService
{
    private readonly string _connectionString;

    public SqliteDataStoreService(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = @"
CREATE TABLE IF NOT EXISTS Devices (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    IpAddress TEXT NOT NULL,
    Username TEXT NOT NULL,
    Password TEXT NOT NULL,
    NvrType TEXT NOT NULL,
    Status INTEGER NOT NULL,
    CreatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Cameras (
    Id TEXT PRIMARY KEY,
    DeviceId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Channel INTEGER NOT NULL,
    RtspMainUrl TEXT NOT NULL,
    RtspSubUrl TEXT NOT NULL,
    Status INTEGER NOT NULL,
    FOREIGN KEY(DeviceId) REFERENCES Devices(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Layouts (
    Id TEXT PRIMARY KEY,
    LayoutName TEXT NOT NULL,
    LayoutJson TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS UserSettings (
    SettingKey TEXT PRIMARY KEY,
    SettingValue TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS PlaybackRecords (
    Id TEXT PRIMARY KEY,
    CameraId TEXT NOT NULL,
    StartUtc TEXT NOT NULL,
    EndUtc TEXT NOT NULL,
    SourceUri TEXT NOT NULL
);
";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceEntity>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceEntity>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IpAddress, Username, Password, NvrType, Status, CreatedUtc FROM Devices ORDER BY Name";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DeviceEntity
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                IpAddress = reader.GetString(2),
                Username = reader.GetString(3),
                Password = reader.GetString(4),
                NvrType = reader.GetString(5),
                Status = (DeviceStatus)reader.GetInt32(6),
                CreatedUtc = DateTime.Parse(reader.GetString(7))
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<CameraEntity>> GetCamerasByDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var result = new List<CameraEntity>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DeviceId, Name, Channel, RtspMainUrl, RtspSubUrl, Status FROM Cameras WHERE DeviceId = $deviceId ORDER BY Channel";
        command.Parameters.AddWithValue("$deviceId", deviceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CameraEntity
            {
                Id = reader.GetString(0),
                DeviceId = reader.GetString(1),
                Name = reader.GetString(2),
                Channel = reader.GetInt32(3),
                RtspMainUrl = reader.GetString(4),
                RtspSubUrl = reader.GetString(5),
                Status = (DeviceStatus)reader.GetInt32(6)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<CameraEntity>> GetAllCamerasAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<CameraEntity>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DeviceId, Name, Channel, RtspMainUrl, RtspSubUrl, Status FROM Cameras ORDER BY Name";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CameraEntity
            {
                Id = reader.GetString(0),
                DeviceId = reader.GetString(1),
                Name = reader.GetString(2),
                Channel = reader.GetInt32(3),
                RtspMainUrl = reader.GetString(4),
                RtspSubUrl = reader.GetString(5),
                Status = (DeviceStatus)reader.GetInt32(6)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<PlaybackRecordEntity>> GetPlaybackRecordsAsync(string cameraId, DateTime rangeStartUtc, DateTime rangeEndUtc, CancellationToken cancellationToken = default)
    {
        var result = new List<PlaybackRecordEntity>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, CameraId, StartUtc, EndUtc, SourceUri
FROM PlaybackRecords
WHERE CameraId = $cameraId
  AND EndUtc > $rangeStartUtc
  AND StartUtc < $rangeEndUtc
ORDER BY StartUtc;";
        command.Parameters.AddWithValue("$cameraId", cameraId);
        command.Parameters.AddWithValue("$rangeStartUtc", rangeStartUtc.ToString("O"));
        command.Parameters.AddWithValue("$rangeEndUtc", rangeEndUtc.ToString("O"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PlaybackRecordEntity
            {
                Id = reader.GetString(0),
                CameraId = reader.GetString(1),
                StartUtc = DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                EndUtc = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                SourceUri = reader.GetString(4)
            });
        }

        return result;
    }

    public async Task UpsertDeviceAsync(DeviceEntity device, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO Devices (Id, Name, IpAddress, Username, Password, NvrType, Status, CreatedUtc)
VALUES ($id, $name, $ip, $username, $password, $nvrType, $status, $createdUtc)
ON CONFLICT(Id) DO UPDATE SET
    Name = excluded.Name,
    IpAddress = excluded.IpAddress,
    Username = excluded.Username,
    Password = excluded.Password,
    NvrType = excluded.NvrType,
    Status = excluded.Status;
";

        command.Parameters.AddWithValue("$id", device.Id);
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$ip", device.IpAddress);
        command.Parameters.AddWithValue("$username", device.Username);
        command.Parameters.AddWithValue("$password", device.Password);
        command.Parameters.AddWithValue("$nvrType", device.NvrType);
        command.Parameters.AddWithValue("$status", (int)device.Status);
        command.Parameters.AddWithValue("$createdUtc", device.CreatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var deleteCameras = connection.CreateCommand();
        deleteCameras.CommandText = "DELETE FROM Cameras WHERE DeviceId = $deviceId";
        deleteCameras.Parameters.AddWithValue("$deviceId", deviceId);
        await deleteCameras.ExecuteNonQueryAsync(cancellationToken);

        await using var deleteDevice = connection.CreateCommand();
        deleteDevice.CommandText = "DELETE FROM Devices WHERE Id = $deviceId";
        deleteDevice.Parameters.AddWithValue("$deviceId", deviceId);
        await deleteDevice.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertCameraAsync(CameraEntity camera, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO Cameras (Id, DeviceId, Name, Channel, RtspMainUrl, RtspSubUrl, Status)
VALUES ($id, $deviceId, $name, $channel, $main, $sub, $status)
ON CONFLICT(Id) DO UPDATE SET
    DeviceId = excluded.DeviceId,
    Name = excluded.Name,
    Channel = excluded.Channel,
    RtspMainUrl = excluded.RtspMainUrl,
    RtspSubUrl = excluded.RtspSubUrl,
    Status = excluded.Status;
";

        command.Parameters.AddWithValue("$id", camera.Id);
        command.Parameters.AddWithValue("$deviceId", camera.DeviceId);
        command.Parameters.AddWithValue("$name", camera.Name);
        command.Parameters.AddWithValue("$channel", camera.Channel);
        command.Parameters.AddWithValue("$main", camera.RtspMainUrl);
        command.Parameters.AddWithValue("$sub", camera.RtspSubUrl);
        command.Parameters.AddWithValue("$status", (int)camera.Status);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteCameraAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Cameras WHERE Id = $cameraId";
        command.Parameters.AddWithValue("$cameraId", cameraId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LayoutEntity>> GetLayoutsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<LayoutEntity>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, LayoutName, LayoutJson, UpdatedUtc FROM Layouts ORDER BY UpdatedUtc DESC";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LayoutEntity
            {
                Id = reader.GetString(0),
                LayoutName = reader.GetString(1),
                LayoutJson = reader.GetString(2),
                UpdatedUtc = DateTime.Parse(reader.GetString(3))
            });
        }

        return result;
    }

    public async Task UpsertLayoutAsync(LayoutEntity layout, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO Layouts (Id, LayoutName, LayoutJson, UpdatedUtc)
VALUES ($id, $name, $json, $updated)
ON CONFLICT(Id) DO UPDATE SET
    LayoutName = excluded.LayoutName,
    LayoutJson = excluded.LayoutJson,
    UpdatedUtc = excluded.UpdatedUtc;
";

        command.Parameters.AddWithValue("$id", layout.Id);
        command.Parameters.AddWithValue("$name", layout.LayoutName);
        command.Parameters.AddWithValue("$json", layout.LayoutJson);
        command.Parameters.AddWithValue("$updated", layout.UpdatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetUserSettingsAsync(CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingKey, SettingValue FROM UserSettings";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            output[reader.GetString(0)] = reader.GetString(1);
        }

        return output;
    }

    public async Task SaveUserSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO UserSettings (SettingKey, SettingValue)
VALUES ($key, $value)
ON CONFLICT(SettingKey) DO UPDATE SET
    SettingValue = excluded.SettingValue;
";

        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
