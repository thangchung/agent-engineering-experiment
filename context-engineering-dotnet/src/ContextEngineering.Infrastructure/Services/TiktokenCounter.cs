using ContextEngineering.Core.Interfaces;
using Microsoft.DeepDev;

namespace ContextEngineering.Infrastructure.Services;

/// <summary>
/// Token counter using tiktoken (cl100k_base encoding for GPT-4/GPT-3.5-turbo).
/// </summary>
public class TiktokenCounter : ITokenCounter
{
    private readonly ITokenizer _tokenizer;

    public TiktokenCounter()
    {
        _tokenizer = TokenizerBuilder.CreateByModelNameAsync("gpt-4").GetAwaiter().GetResult();
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        var tokens = _tokenizer.Encode(text, []);
        return tokens.Count;
    }

    public int CountTokens(IEnumerable<string> texts)
    {
        return texts.Sum(CountTokens);
    }
}
