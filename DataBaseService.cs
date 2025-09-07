using Dapper;
using IksAdminApi;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace IksAdminTime
{
    public class DataBaseService
    {
        private readonly ILogger<DataBaseService> _logger;
        private readonly string _connectionString;

        public DataBaseService(IIksAdminApi api)
        {
            if (string.IsNullOrWhiteSpace(api.DbConnectionString))
                throw new ArgumentException("Database connection string is null или пустая");

            _connectionString = api.DbConnectionString;

            ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<DataBaseService>();
        }

        public async Task InitializeDatabase()
        {
            try
            {
                await using var connection = await GetOpenConnectionAsync();
                await CreateTablesAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database initialization failed");
                throw;
            }
        }

        private async Task CreateTablesAsync(MySqlConnection connection)
        {
            try
            {
                const string createTableQuery = @"
CREATE TABLE IF NOT EXISTS `iks_admin_time` (
    `id` INT PRIMARY KEY AUTO_INCREMENT,
    `admin_id` VARCHAR(32) NOT NULL,
    `admin_name` VARCHAR(64) NOT NULL,
    `connect_time` INT NOT NULL,
    `disconnect_time` INT NOT NULL DEFAULT -1 COMMENT '-1 = Admin online',
    `played_time` INT NOT NULL DEFAULT 0,
    `spectator_time` INT NOT NULL DEFAULT 0,
    `server_id` VARCHAR(32) NOT NULL,
    UNIQUE KEY `uk_admin_server` (`admin_id`, `server_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

                await connection.ExecuteAsync(createTableQuery);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tables");
                throw;
            }
        }

        public async Task OnAdminConnect(ulong steamId, string name, int serverId)
        {
            const string query = @"
INSERT INTO `iks_admin_time` 
    (admin_id, admin_name, connect_time, server_id)
VALUES 
    (@SteamId, @Name, @Time, @ServerId)
ON DUPLICATE KEY UPDATE 
    admin_name = VALUES(admin_name),
    connect_time = VALUES(connect_time),
    disconnect_time = -1;";

            var parameters = new { SteamId = steamId, Name = name, Time = GetCurrentUnixTime(), ServerId = serverId };

            try
            {
                await using var connection = await GetOpenConnectionAsync();

                await connection.ExecuteAsync(query, parameters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to log admin connect for SteamID {steamId} on server {serverId}");
            }
        }

        public async Task OnAdminDisconnect(string steamId, int serverId)
        {
            const string query = @"
UPDATE `iks_admin_time`
SET 
    disconnect_time = @Time,
    played_time = played_time + (@Time - connect_time)
WHERE 
    admin_id = @SteamId 
    AND server_id = @ServerId
    AND disconnect_time = -1;";

            var parameters = new { SteamId = steamId, ServerId = serverId, Time = GetCurrentUnixTime() };

            try
            {
                await using var connection = await GetOpenConnectionAsync();

                await connection.ExecuteAsync(query, parameters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to log admin disconnect for SteamID {steamId} on server {serverId}");
            }
        }

        public async Task AddSpectatorTimeAsync(string steamId, int serverId, int duration)
        {
            const string query = @"
UPDATE `iks_admin_time`
SET spectator_time = spectator_time + @Duration
WHERE admin_id = @SteamId AND server_id = @ServerId;";

            var parameters = new { SteamId = steamId, ServerId = serverId, Duration = duration };

            try
            {
                await using var connection = await GetOpenConnectionAsync();

                await connection.ExecuteAsync(query, parameters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to add spectator time for SteamID {steamId} on server {serverId}");
            }
        }

        private async Task<MySqlConnection> GetOpenConnectionAsync()
        {
            var connection = new MySqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open MySQL connection");
                throw;
            }

            return connection;
        }

        private static int GetCurrentUnixTime()
        {
            return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
