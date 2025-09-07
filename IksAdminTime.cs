using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using IksAdminApi;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IksAdminTime
{
    public partial class IksAdminTime : BasePlugin
    {
        internal static DataBaseService? _dataBaseService;

        public override string ModuleName => "[IKS] Admin Time";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.1.1";

        private readonly ConcurrentDictionary<string, int> _spectatorJoinTime = new();

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _dataBaseService = new DataBaseService(AdminModule.Api);

            try
            {
                _ = _dataBaseService.InitializeDatabase();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Database init failed: {ex.Message}");
            }

            AdminModule.Api.OnFullConnect += (steamId, ip) =>
            {
                _ = OnFullConnect(steamId, ip);
            };

            RegisterListener<Listeners.OnClientDisconnect>(playerSlot =>
            {
                _ = OnPlayerDisconnect(playerSlot);
            });

            RegisterEventHandler<EventPlayerTeam>(OnPlayerTeamChange);
        }

        private HookResult OnPlayerTeamChange(EventPlayerTeam @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player == null || !player.IsValid || !AdminUtils.IsAdmin(player))
            {
                return HookResult.Continue;
            }

            string steamId = player.GetSteamId();
            if (steamId == null)
            {
                return HookResult.Continue;
            }

            CsTeam newTeam = (CsTeam)@event.Team;
            CsTeam oldTeam = (CsTeam)@event.Oldteam;

            if (newTeam == CsTeam.Spectator)
            {
                _spectatorJoinTime[steamId] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            else if (oldTeam == CsTeam.Spectator)
            {
                if (_spectatorJoinTime.TryRemove(steamId, out int joinTime))
                {
                    int duration = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - joinTime;
                    _ = _dataBaseService!.AddSpectatorTimeAsync(steamId, AdminModule.Api.ThisServer.Id, duration);
                }
            }

            return HookResult.Continue;
        }

        private async Task OnFullConnect(string steamId, string ip)
        {
            try
            {
                if (_dataBaseService == null || AdminModule.Api == null)
                    return;

                if (!ulong.TryParse(steamId, out ulong steamId64))
                    return;

                if (AdminModule.Api.ServerAdmins.TryGetValue(steamId64, out Admin? admin))
                {
                    await _dataBaseService.OnAdminConnect(steamId64, admin.CurrentName, AdminModule.Api.ThisServer.Id);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"OnFullConnect failed for SteamID {steamId}, IP {ip}: {ex}");
            }
        }

        private async Task OnPlayerDisconnect(int playerSlot)
        {
            string? steamId = null;
            try
            {
                if (_dataBaseService == null)
                    return;

                CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);
                if (player == null || !player.IsValid || !AdminUtils.IsAdmin(player))
                    return;

                steamId = player.GetSteamId();
                if (steamId == null)
                    return;

                await _dataBaseService.OnAdminDisconnect(steamId, AdminModule.Api.ThisServer.Id);

                if (_spectatorJoinTime.TryRemove(steamId, out int specJoinTime))
                {
                    int duration = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - specJoinTime;
                    await _dataBaseService.AddSpectatorTimeAsync(steamId, AdminModule.Api.ThisServer.Id, duration);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"OnPlayerDisconnect failed for SteamID {steamId ?? "unknown"}: {ex}");
            }
        }
    }
}