using Genie4.Core.Commanding;

namespace Genie5.Core.Smoke;

public sealed class ConsoleCommandHost : ICommandHost
{
    public void Echo(string text)
    {
        Console.WriteLine($"[echo] {text}");
    }

    public void SendToGame(string text, bool userInput = false, string origin = "")
    {
        Console.WriteLine($"[send] {text}");
    }

    public void RunScript(string text)
    {
        Console.WriteLine($"[script] {text}");
    }
}
