using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using IksAdminApi;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IksAdminTime
{
    public partial class IksAdminTime : BasePlugin, IPluginConfig<IksAdminTimeConfig>
    {
        public IksAdminTimeConfig Config { get; set; } = new();
        internal static DataBaseService? _dataBaseService;

        public override string ModuleName => "[IKS] Admin Time";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private readonly IIksAdminApi _adminApi = AdminModule.Api;
        private int _serverId;
        private readonly ConcurrentDictionary<string, int> _spectatorJoinTime = new();

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _adminApi.OnFullConnect += OnFullConnect;
            RegisterListener<Listeners.OnClientDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventPlayerTeam>(OnPlayerTeamChange);
        }

        public async void OnConfigParsed(IksAdminTimeConfig config)
        {
            Config = config;
            _serverId = config.ServerID;
            _dataBaseService = new DataBaseService(_adminApi);
            await _dataBaseService.InitializeDatabase();
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
                    _ = _dataBaseService!.AddSpectatorTimeAsync(steamId, _serverId, duration);
                }
            }

            return HookResult.Continue;
        }

        private async void OnFullConnect(string steamId, string ip)
        {
            if (!ulong.TryParse(steamId, out ulong steamId64))
            {
                return;
            }

            if (_adminApi.ServerAdmins.TryGetValue(steamId64, out Admin? admin))
            {
                await _dataBaseService!.OnAdminConnect(steamId64, admin.CurrentName, _serverId);
            }
        }

        private async void OnPlayerDisconnect(int playerSlot)
        {
            try
            {
                CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);
                if (player == null || !player.IsValid || !AdminUtils.IsAdmin(player))
                {
                    return;
                }

                string steamId = player.GetSteamId();
                if (steamId == null)
                {
                    return;
                }

                await _dataBaseService!.OnAdminDisconnect(steamId, _serverId);

                if (_spectatorJoinTime.TryRemove(steamId, out int specJoinTime))
                {
                    int duration = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - specJoinTime;
                    await _dataBaseService.AddSpectatorTimeAsync(steamId, _serverId, duration);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"OnPlayerDisconnect: {ex.Message}");
            }
        }
    }
}