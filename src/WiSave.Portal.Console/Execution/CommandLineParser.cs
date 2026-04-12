namespace WiSave.Portal.Console.Execution;

internal interface ICommandLineParser
{
    CommandLineParseResult Parse(string[] args);
}

internal sealed class CommandLineParser : ICommandLineParser
{
    public CommandLineParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return CommandLineParseResult.Interactive();
        }

        var commandName = args[0].Trim();
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return CommandLineParseResult.Failure("Command name cannot be empty.");
        }

        var arguments = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                return CommandLineParseResult.Failure($"Unexpected argument '{token}'. Expected '--name value'.");
            }

            var parameterName = token[2..].Trim();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return CommandLineParseResult.Failure("Parameter name after '--' cannot be empty.");
            }

            string? parameterValue = "true";
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parameterValue = args[++index];
            }

            arguments[parameterName] = parameterValue;
        }

        return CommandLineParseResult.Success(new CommandInvocation(commandName, arguments));
    }
}
