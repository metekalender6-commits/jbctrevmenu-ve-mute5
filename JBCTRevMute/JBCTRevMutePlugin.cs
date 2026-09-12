using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace JBCTRevMute;

public class JBCTRevMutePlugin : BasePlugin
{
    public override string ModuleName => "JB Mute + CT Revive";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "sen";
    public override string ModuleDescription => "T sesini otomatik susturur, CT icin paylasimli revive sistemi + gecici god mode (CounterStrikeSharp)";

    // Sunucu konsolundan "css_ctrev_max 3" gibi degistirilebilir
    public FakeConVar<int> CtRevMax = new("css_ctrev_max", "CT takiminin round basina toplam revive hakki", 3);

    private int _ctRevLeft;
    private const float GodDuration = 3.0f;
    private const string ReviveSound = "sounds/ui/panorama/round_report_round_end_01.vsnd"; // sunucunda calmiyorsa degistir

    private readonly HashSet<int> _godSlots = new();

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

        // Pre: oyun bu event'i yaymadan / isleme almadan once yakala
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeathPre, HookMode.Pre);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurtPre, HookMode.Pre);

        _ctRevLeft = CtRevMax.Value;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _ctRevLeft = CtRevMax.Value;
        _godSlots.Clear();

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsValid(player)) continue;

            if (player.Team == CsTeam.Terrorist)
                MuteVoice(player);
            else
                UnmuteVoice(player);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsValid(player)) return HookResult.Continue;

        if (player!.Team == CsTeam.Terrorist)
            MuteVoice(player);

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsValid(player)) return HookResult.Continue;

        if ((CsTeam)@event.Team == CsTeam.Terrorist)
            MuteVoice(player!);
        else
            UnmuteVoice(player!);

        return HookResult.Continue;
    }

    private void MuteVoice(CCSPlayerController player)
    {
        player.VoiceFlags = VoiceFlags.Muted;
    }

    private void UnmuteVoice(CCSPlayerController player)
    {
        player.VoiceFlags = VoiceFlags.Normal;
    }

    // ---- Komutlar (css/generic yetkisi ister) ----

    [ConsoleCommand("css_mute", "Hedefin sesini kapatir")]
    [CommandHelper(minArgs: 1, usage: "<hedef>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/generic")]
    public void OnMuteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var target = FindTarget(command.GetArg(1));
        if (target is null)
        {
            command.ReplyToCommand("[SM] Hedef bulunamadi.");
            return;
        }

        MuteVoice(target);
        Server.PrintToChatAll($"[SM] {caller?.PlayerName ?? "SERVER"}, {target.PlayerName} adli oyuncunun sesini kapatti.");
    }

    [ConsoleCommand("css_unmute", "Hedefin sesini acar")]
    [CommandHelper(minArgs: 1, usage: "<hedef>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/generic")]
    public void OnUnmuteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var target = FindTarget(command.GetArg(1));
        if (target is null)
        {
            command.ReplyToCommand("[SM] Hedef bulunamadi.");
            return;
        }

        UnmuteVoice(target);
        Server.PrintToChatAll($"[SM] {caller?.PlayerName ?? "SERVER"}, {target.PlayerName} adli oyuncunun sesini acti.");
    }

    [ConsoleCommand("css_ctrev0", "CT revive hakkini sifirlar")]
    [RequiresPermissions("@css/generic")]
    public void OnCtRevResetCommand(CCSPlayerController? caller, CommandInfo command)
    {
        _ctRevLeft = 0;
        Server.PrintToChatAll($"[SM] {caller?.PlayerName ?? "SERVER"} CT revive hakkini sifirladi. Bu round icin revive kalmadi.");
    }

    // ---- Revive + god mode mantigi ----

    private HookResult OnPlayerDeathPre(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (!IsValid(victim)) return HookResult.Continue;

        if (victim!.Team != CsTeam.CounterTerrorist) return HookResult.Continue;
        if (_ctRevLeft <= 0) return HookResult.Continue;

        var pawn = victim.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid) return HookResult.Continue;

        _ctRevLeft--;

        // Canini geri yukle, olum event'ini iptal et
        pawn.Health = 100;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

        int slot = victim.Slot;
        _godSlots.Add(slot);
        AddTimer(GodDuration, () => _godSlots.Remove(slot));

        Server.ExecuteCommand($"play {ReviveSound}");
        Server.PrintToChatAll($"[SM] CT revlenmistir! Kalan hak: {_ctRevLeft} (3 saniye dokunulmazlik)");

        return HookResult.Handled; // olum event'ini engelle
    }

    private HookResult OnPlayerHurtPre(EventPlayerHurt @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (!IsValid(victim)) return HookResult.Continue;

        if (_godSlots.Contains(victim!.Slot))
        {
            @event.DmgHealth = 0;
            @event.DmgArmor = 0;
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    private static bool IsValid(CCSPlayerController? player)
    {
        return player is not null && player.IsValid && !player.IsBot && player.PawnIsAlive;
    }

    private CCSPlayerController? FindTarget(string arg)
    {
        return Utilities.GetPlayers().FirstOrDefault(p =>
            p.IsValid &&
            p.PlayerName.Contains(arg, StringComparison.OrdinalIgnoreCase));
    }
}
