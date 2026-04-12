namespace WiSave.Portal.Console.Shell;

internal sealed class SystemConsoleOutput : IConsoleOutput
{
    public void Write(string value) => System.Console.Write(value);

    public void WriteLine(string? value) => System.Console.WriteLine(value);

    public string? ReadLine() => System.Console.ReadLine();

    public void Clear() => System.Console.Clear();
}
