namespace Genie4.Core.Commanding;

public interface ICommandHost
{
    void Echo(string text);
    void SendToGame(string text, bool userInput = false, string origin = "");
    void RunScript(string text);
}
