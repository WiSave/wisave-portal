namespace WiSave.Portal.Console.Shell;

internal interface IConsoleOutput
{
    void Write(string value);
    void WriteLine(string? value);
    string? ReadLine();
    void Clear();
}
