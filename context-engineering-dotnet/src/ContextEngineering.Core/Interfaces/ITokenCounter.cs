namespace ContextEngineering.Core.Interfaces;

public interface ITokenCounter
{
    int CountTokens(string text);
    int CountTokens(IEnumerable<string> texts);
}
