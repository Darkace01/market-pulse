using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

internal sealed partial class AddToWatchlistCommand : InvokableCommand
{
    private readonly SettingsManager _settings;
    private readonly string _group;
    private readonly MarketSymbol _symbol;

    public AddToWatchlistCommand(SettingsManager settings, string group, MarketSymbol symbol)
    {
        _settings = settings;
        _group = group;
        _symbol = symbol;
        Name = $"Add to {group}";
        Icon = new IconInfo("");
    }

    public override ICommandResult Invoke()
    {
        _settings.AddSymbol(_group, _symbol);
        return CommandResult.ShowToast($"Added {_symbol.Label} to {_group}");
    }
}

internal sealed partial class RemoveFromWatchlistCommand : InvokableCommand
{
    private readonly SettingsManager _settings;
    private readonly string _group;
    private readonly MarketSymbol _symbol;

    public RemoveFromWatchlistCommand(SettingsManager settings, string group, MarketSymbol symbol)
    {
        _settings = settings;
        _group = group;
        _symbol = symbol;
        Name = $"Remove from {group}";
        Icon = new IconInfo("");
    }

    public override ICommandResult Invoke()
    {
        _settings.RemoveSymbol(_group, _symbol.Key);
        return CommandResult.ShowToast($"Removed {_symbol.Label} from {_group}");
    }
}

/// <summary>Opens whatever URL the supplier returns at invoke time; used by the rotating ticker.</summary>
internal sealed partial class OpenDynamicUrlCommand : InvokableCommand
{
    private readonly Func<string?> _url;

    public OpenDynamicUrlCommand(Func<string?> url)
    {
        _url = url;
        Name = "Open chart";
    }

    public override ICommandResult Invoke()
    {
        if (_url() is { Length: > 0 } url)
        {
            ShellHelpers.OpenInShell(url);
        }

        return CommandResult.Dismiss();
    }
}
