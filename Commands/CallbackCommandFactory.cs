using Telegram.Bot.Types;

namespace Telegram.Bot.Commands;

public class CallbackCommandFactory : ICallbackCommandFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _commandMap;

    public CallbackCommandFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _commandMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterCommand<TCommand>(params string[] commandNames) where TCommand : ICallbackCommand
    {
        if (commandNames == null || commandNames.Length == 0)
            throw new ArgumentException("At least one command name must be provided.", nameof(commandNames));

        foreach (var commandName in commandNames)
        {
            _commandMap[commandName] = typeof(TCommand);
        }
    }

    public ICallbackCommand? CreateCommand(CallbackQuery callbackQuery)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data))
            return null;

        var commandKey = ExtractCommandKey(data);

        if (_commandMap.TryGetValue(commandKey, out var commandType))
        {
            return (ICallbackCommand)_serviceProvider.GetRequiredService(commandType);
        }

        return default;
    }

    private static string ExtractCommandKey(string data)
    {
        var separatorIndex = data.IndexOf(':');
        return separatorIndex >= 0 ? data.Substring(0, separatorIndex) : data;
    }
}


public interface ICallbackCommandFactory
{
    void RegisterCommand<TCommand>(params string[] commandNames) where TCommand : ICallbackCommand;
    ICallbackCommand? CreateCommand(CallbackQuery callbackQuery);
}
