using System.Text.Json;

namespace McpServer.Services;

internal static class ExecuteToolResultExtractor
{
    public static bool TryExtractJsonObject(string? text, out JsonElement result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (TryParseCandidate(trimmed, out result))
        {
            return true;
        }

        if (TryStripCodeFence(trimmed, out string fencedJson) && TryParseCandidate(fencedJson, out result))
        {
            return true;
        }

        if (TryExtractFirstCodeFence(trimmed, out string embeddedFenceJson) && TryParseCandidate(embeddedFenceJson, out result))
        {
            return true;
        }

        return TryExtractEmbeddedObject(trimmed, out result);
    }

    private static bool TryParseCandidate(string candidate, out JsonElement result)
    {
        result = default;

        try
        {
            using JsonDocument document = JsonDocument.Parse(candidate);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return false;
            }

            result = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryStripCodeFence(string text, out string json)
    {
        json = string.Empty;
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        int firstNewLineIndex = text.IndexOf('\n');
        if (firstNewLineIndex < 0)
        {
            return false;
        }

        json = text[(firstNewLineIndex + 1)..^3].Trim();
        return json.Length > 0;
    }

    private static bool TryExtractFirstCodeFence(string text, out string json)
    {
        json = string.Empty;

        int fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        while (fenceStart >= 0)
        {
            int firstNewLineIndex = text.IndexOf('\n', fenceStart);
            if (firstNewLineIndex < 0)
            {
                return false;
            }

            int fenceEnd = text.IndexOf("```", firstNewLineIndex + 1, StringComparison.Ordinal);
            if (fenceEnd < 0)
            {
                return false;
            }

            string candidate = text[(firstNewLineIndex + 1)..fenceEnd].Trim();
            if (candidate.Length > 0)
            {
                json = candidate;
                return true;
            }

            fenceStart = text.IndexOf("```", fenceEnd + 3, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryExtractEmbeddedObject(string text, out JsonElement result)
    {
        result = default;
        JsonElement? bestMatch = null;
        int bestScore = int.MinValue;

        int objectStartIndex = text.IndexOf('{');
        while (objectStartIndex >= 0)
        {
            if (TryFindMatchingBrace(text, objectStartIndex, out int objectEndIndex))
            {
                string candidate = text.Substring(objectStartIndex, objectEndIndex - objectStartIndex + 1);
                if (TryParseCandidate(candidate, out JsonElement parsed))
                {
                    int score = ScoreCandidate(parsed, candidate.Length);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = parsed;
                    }
                }
            }

            objectStartIndex = text.IndexOf('{', objectStartIndex + 1);
        }

        if (bestMatch is JsonElement selected)
        {
            result = selected;
            return true;
        }

        return false;
    }

    private static int ScoreCandidate(JsonElement candidate, int candidateLength)
    {
        int score = candidateLength;
        if (candidate.ValueKind is not JsonValueKind.Object)
        {
            return score;
        }

        if (candidate.TryGetProperty("result", out _))
        {
            score += 10_000;
        }

        if (candidate.TryGetProperty("errors", out _))
        {
            score += 5_000;
        }

        if (candidate.TryGetProperty("data", out _))
        {
            score += 2_000;
        }

        return score;
    }

    private static bool TryFindMatchingBrace(string text, int startIndex, out int endIndex)
    {
        endIndex = -1;
        int depth = 0;
        bool inString = false;
        bool isEscaped = false;

        for (int index = startIndex; index < text.Length; index++)
        {
            char character = text[index];
            if (inString)
            {
                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    isEscaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character == '{')
            {
                depth++;
                continue;
            }

            if (character != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                endIndex = index;
                return true;
            }
        }

        return false;
    }
}