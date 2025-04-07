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

    public ICallbackCommand? CreateCommand(string commandName)
    {
        if (_commandMap.TryGetValue(commandName, out var commandType))
        {
            return _serviceProvider.GetService(commandType) as ICallbackCommand;
        }

        return default;
    }

    public ICallbackCommand? CreateCommand(CallbackQuery callbackQuery)
    {
        if (callbackQuery?.Data == null)
            return default;

        var commandName = callbackQuery.Data.Split(':')[0];
        return CreateCommand(commandName);
    }

    public ICallbackCommand? GetCommand(string commandName)
    {
        if (_commandMap.TryGetValue(commandName, out var commandType))
        {
            return _serviceProvider.GetService(commandType) as ICallbackCommand;
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
    ICallbackCommand? CreateCommand(string commandName);
    ICallbackCommand? CreateCommand(CallbackQuery callbackQuery);
    ICallbackCommand? GetCommand(string commandName);
}
