namespace SemanticCache.Api.Infrastructure.Services;

public class VectorSimilarityService
{
    /// <summary>
    /// Calculates the cosine similarity between two embedding vectors.
    /// Returns a value between -1 and 1, where 1 means identical vectors.
    /// </summary>
    public float CalculateCosineSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException("Embeddings must have the same dimension");
        }

        var dotProduct = 0.0;
        var magnitude1 = 0.0;
        var magnitude2 = 0.0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }

        var magnitude = Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2);
        
        if (magnitude == 0)
        {
            return 0;
        }

        return (float)(dotProduct / magnitude);
    }

    /// <summary>
    /// Finds the most similar embedding from a list of candidates.
    /// Returns the index and similarity score of the best match, or null if no match exceeds the threshold.
    /// </summary>
    public (int Index, float Similarity)? FindMostSimilar(
        float[] queryEmbedding,
        IEnumerable<float[]> candidateEmbeddings,
        float similarityThreshold = 0.85f)
    {
        int bestIndex = -1;
        float bestSimilarity = -1;
        int currentIndex = 0;

        foreach (var candidate in candidateEmbeddings)
        {
            var similarity = CalculateCosineSimilarity(queryEmbedding, candidate);
            
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestIndex = currentIndex;
            }
            
            currentIndex++;
        }

        if (bestSimilarity >= similarityThreshold)
        {
            return (bestIndex, bestSimilarity);
        }

        return null;
    }
}
