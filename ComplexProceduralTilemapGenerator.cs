using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public class ComplexProceduralTilemapGenerator : MonoBehaviour
{
    public enum TileTypes
    {
        Water,
        Island,
        Biome1,
        Biome2,
        Biome3,
        Biome4
    }
    public enum BiomeTileTypes
    {
        Biome1,
        Biome2,
        Biome3,
        Biome4
    }

    [SerializeField] bool _applyToTilemap;
    [SerializeField] Tilemap _tilemap;
    [SerializeField] GameObject _tilemapPrefab; // Prefab reference


    [Header("Dimensions")]
    [SerializeField] int _width = 128;
    [SerializeField] int _height = 128;

    [Header("Seed")]
    [SerializeField] int _seed;  // Single seed for the whole map
    [SerializeField] bool _randomSeed;  // Whether to use a random seed

    [Header("Tile Types")]
    [SerializeField] TileBase[] _tiles; // Tiles for water, biomes, roads, etc.
    [SerializeField] float[] _tileWeights; // Weights for biome types.

    [Header("Features")]
    [SerializeField] float _islandRadius = 100f; // Island radius
    [SerializeField] float _islandCenterRadius = 50f;
    [SerializeField] float _noiseCenterRadius = 0.2f;
    [SerializeField] float _floodFillCenterNeighbourChance = 0.3f;

    [SerializeField] int _riverCount = 2; // Number of rivers
    [SerializeField] int _roadCount = 3; // Number of roads

    [Header("Biomes")]
    [SerializeField] int _averageBiomeSize = 50;
    [SerializeField] float _minBiomeSizeFactor = 0.8f;
    [SerializeField] float _maxBiomeSizeFactor = 1f;
    [SerializeField] float _poissonMinDistanceFactor = 0.5f;

    [SerializeField] float _floodFillNeighbourChance = 0.3f;
    [SerializeField] float _smoothingDominantColorChance = 0.1f;

    [Header("Output")]
    [SerializeField] string _outputPath = "Resources/Tilemaps/GeneratedTilemap.png";

    System.Random _random;
    List<Vector2Int> _islandPixels = new List<Vector2Int>();
    int _islandPixelsCount;

    public void GenerateTilemap()
    {
        if (_tilemap == null)
        {
            if (_tilemapPrefab == null)
            {
                Debug.LogError("Tilemap prefab is not assigned. Please assign it in the inspector.");
                return;
            }

            GameObject newTilemapObject = Instantiate(_tilemapPrefab);
            _tilemap = newTilemapObject.GetComponentInChildren<Tilemap>();

            if (_tilemap == null)
            {
                Debug.LogError("The prefab does not contain a Tilemap component.");
                return;
            }
        }

        if (_randomSeed)
        {
            _seed = Random.Range(-100_000, 100_000);
        }

        // Initialize the random number generator using the single seed
        _random = new System.Random(_seed);

        // Create an empty pixel map
        Color[,] pixelMap = new Color[_width, _height];

        // Generate the map features
        GenerateIsland(pixelMap);
        GenerateIslandBiomes(pixelMap);
        GenerateIslandTilesInCenterOfIsland(pixelMap);
        SmoothBiomeEdges(pixelMap);


        // Apply the pixel map to the tilemap
        if (_applyToTilemap)
            ApplyToTilemap(pixelMap);

        // Save the pixel map as a PNG image
        SavePixelMapAsPNG(pixelMap);
    }

    void GenerateIslandTilesInCenterOfIsland(Color[,] pixelMap)
    {
        // Determine the island center and radius
        Vector2Int islandCenter = new Vector2Int(_width / 2, _height / 2);

        // List to store central island pixels
        List<Vector2Int> centerIslandPixels = new List<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        // Start from the central point
        queue.Enqueue(islandCenter);

        // Perform flood-fill-like expansion from the center
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            // Add randomness to the radius check for more organic shapes
            float randomRadius = _islandCenterRadius * (0.8f + (float)_random.NextDouble() * 0.4f); // Random factor between 0.8 and 1.2

            if (visited.Contains(current) ||
                !IsValidPosition(current) ||
                !IsWithinRadiusWithNoise(current, islandCenter, randomRadius))
                continue;

            visited.Add(current);

            // Add to the central island pixels
            centerIslandPixels.Add(current);

            // Spread outward to neighbors
            foreach (var neighbor in GetNeighbors(current))
            {
                if (!visited.Contains(neighbor) && _random.NextDouble() > _floodFillCenterNeighbourChance)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        // Apply the center island tiles to the pixel map
        foreach (var position in centerIslandPixels)
        {
            pixelMap[position.x, position.y] = GetColorForTileIndex(TileTypes.Island);
        }

        Debug.Log($"Generated {centerIslandPixels.Count} central island tiles.");
    }

    // Helper method to check if a position is valid within the pixel map bounds
    bool IsValidPosition(Vector2Int position)
    {
        return position.x >= 0 && position.x < _width && position.y >= 0 && position.y < _height;
    }

    // Helper method to check if a position is within a certain radius with added noise
    bool IsWithinRadiusWithNoise(Vector2Int position, Vector2Int center, float radius)
    {
        float dx = position.x - center.x;
        float dy = position.y - center.y;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        // Add noise to the boundary
        float noise = (float)_random.NextDouble() * (radius * _noiseCenterRadius); // Up to 10% of the radius
        return distance <= radius + noise;
    }


    void GenerateIsland(Color[,] pixelMap)
    {
        _islandPixels.Clear();

        Vector2 islandCenter = new Vector2(_width / 2, _height / 2);

        // Ensure that _seed is a float between -1f and 1f for consistent noise generation
        float seedX = _seed * 0.1f; // Adjusted seed offset for larger maps
        float seedY = _seed * 0.1f;

        // Parameters for generating more organic and irregular island shapes
        float lowFrequency = 0.01f;  // Low frequency for general island shape
        float highFrequency = 0.05f;  // High frequency for finer details (rocky shores, hills)
        float distortionFrequency = 0.005f; // Larger distortion for a more irregular shape

        // Generate the island with Perlin noise
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                Vector2 currentPos = new Vector2(x, y);
                float distanceToCenter = (currentPos - islandCenter).magnitude;

                // Perlin noise for the general shape (larger features of the island)
                float noiseValue = Mathf.PerlinNoise((x + seedX) * lowFrequency, (y + seedY) * lowFrequency);

                // Higher frequency noise for smaller island features (rocky shores, hills, etc.)
                float noiseDetail = Mathf.PerlinNoise((x + seedX) * highFrequency, (y + seedY) * highFrequency);

                // Distortion noise to make the island more organic and less round
                float noiseDistortion = Mathf.PerlinNoise((x + seedX) * distortionFrequency, (y + seedY) * distortionFrequency);

                // Combine the noises with added distortion for irregularity
                float combinedNoise = noiseValue + noiseDetail * 0.3f + noiseDistortion * 0.2f;
                combinedNoise = Mathf.Clamp01(combinedNoise); // Ensure the value is between 0 and 1

                // Define the sharpness of the coastline using a threshold
                float coastlineThreshold = Mathf.InverseLerp(_islandRadius * 0.6f, _islandRadius * 1.2f, distanceToCenter);

                // Mark pixels based on the noise value and coastline threshold
                if (combinedNoise > coastlineThreshold)
                {
                    pixelMap[x, y] = GetColorForTileIndex(TileTypes.Island);

                    _islandPixels.Add(new Vector2Int(x, y));
                }
                else
                {
                    pixelMap[x, y] = GetColorForTileIndex(TileTypes.Water);
                }
            }
        }

        _islandPixelsCount = _islandPixels.Count;
    }
    void GenerateIslandBiomes(Color[,] pixelMap)
    {
        int biomeCount = Mathf.Max(0, _tiles.Length - 2);
        if (biomeCount == 0)
        {
            Debug.LogWarning("No biome tiles are defined. Skipping biome generation.");
            return;
        }


        // Biome size constraints
        int minBiomeSize = Mathf.FloorToInt(_averageBiomeSize * _minBiomeSizeFactor);
        int maxBiomeSize = Mathf.CeilToInt(_averageBiomeSize * _maxBiomeSizeFactor);
        float minDistance = (_islandPixels.Count / _averageBiomeSize) * _poissonMinDistanceFactor;

        int currentBiomeIndex = 0;

        while (_islandPixels.Count > 0 && currentBiomeIndex < biomeCount)
        {
            // Use Poisson disk sampling to find a start position for the biome
            List<Vector2Int> startPositions = PoissonDiskSampling(_islandPixels, minDistance, 1);

            if (startPositions.Count == 0)
            {
                Debug.LogWarning("No valid positions left for creating a new biome.");
                break;
            }

            HashSet<Vector2Int> biomeArea = new HashSet<Vector2Int>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(startPositions[0]);

            while (queue.Count > 0 && biomeArea.Count < maxBiomeSize)
            {
                Vector2Int current = queue.Dequeue();
                if (!biomeArea.Contains(current) && _islandPixels.Contains(current))
                {
                    biomeArea.Add(current);
                    _islandPixels.Remove(current);

                    foreach (var neighbor in GetNeighbors(current))
                    {
                        if (_islandPixels.Contains(neighbor) && _random.NextDouble() > _floodFillNeighbourChance)
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            if (biomeArea.Count >= minBiomeSize)
            {
                TileTypes tileType = (TileTypes)(currentBiomeIndex + 2);
                foreach (var pos in biomeArea)
                {
                    pixelMap[pos.x, pos.y] = GetColorForTileIndex(tileType);
                }

                currentBiomeIndex = (currentBiomeIndex + 1) % biomeCount;
            }
        }

        Debug.Log($"Biomes generated. Remaining island pixels: {_islandPixels.Count}.");
    }

    // Poisson disk sampling implementation
    List<Vector2Int> PoissonDiskSampling(List<Vector2Int> validPoints, float minDistance, int maxSamples)
    {
        List<Vector2Int> sampledPoints = new List<Vector2Int>();
        List<Vector2Int> candidatePoints = new List<Vector2Int>(validPoints);

        while (sampledPoints.Count < maxSamples && candidatePoints.Count > 0)
        {
            // Randomly pick a candidate point
            Vector2Int candidate = candidatePoints[_random.Next(candidatePoints.Count)];

            // Check the distance constraint against all sampled points
            bool valid = true;
            foreach (var point in sampledPoints)
            {
                if (Vector2Int.Distance(candidate, point) < minDistance)
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                sampledPoints.Add(candidate);
            }

            candidatePoints.Remove(candidate); // Remove candidate to avoid duplicate checks
        }

        return sampledPoints;
    }


    void SmoothBiomeEdges(Color[,] pixelMap)
    {
        for (int y = 1; y < _height - 1; y++)
        {
            for (int x = 1; x < _width - 1; x++)
            {
                Color currentColor = pixelMap[x, y];
                if (currentColor == GetColorForTileIndex(TileTypes.Water))
                    continue;

                Dictionary<Color, int> neighborCounts = new Dictionary<Color, int>();

                foreach (var neighbor in GetNeighbors(new Vector2Int(x, y)))
                {
                    Color neighborColor = pixelMap[neighbor.x, neighbor.y];
                    if (!neighborCounts.ContainsKey(neighborColor))
                        neighborCounts[neighborColor] = 0;
                    neighborCounts[neighborColor]++;
                }

                Color dominantColor = neighborCounts.OrderByDescending(n => n.Value).First().Key;
                if (dominantColor != currentColor && _random.NextDouble() > _smoothingDominantColorChance)
                {
                    pixelMap[x, y] = dominantColor;
                }
            }
        }
    }

    IEnumerable<Vector2Int> GetNeighbors(Vector2Int position)
    {
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = position.x + dx[i];
            int ny = position.y + dy[i];
            if (nx >= 0 && ny >= 0 && nx < _width && ny < _height)
            {
                yield return new Vector2Int(nx, ny);
            }
        }
    }


    // Convert the tile index into a color
    Color GetColorForTileIndex(TileTypes tileType)
    {
        // Use simple predefined colors for different types
        switch (tileType)
        {
            case TileTypes.Water: return Color.blue;
            case TileTypes.Island: return Color.green;
            case TileTypes.Biome1: return Color.yellow;
            case TileTypes.Biome2: return Color.red;
            case TileTypes.Biome3: return Color.cyan;
            case TileTypes.Biome4: return Color.black;
            default: return Color.white;
        }
    }
    void ApplyToTilemap(Color[,] pixelMap)
    {
        _tilemap.ClearAllTiles();

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                Color color = pixelMap[x, y];
                int tileIndex = GetTileIndexFromColor(color);

                if (tileIndex >= 0 && tileIndex < _tiles.Length)
                {
                    TileBase tile = _tiles[tileIndex];
                    if (tile != null)
                    {
                        Vector3Int position = new Vector3Int(x - _width / 2, y - _height / 2, 0);
                        _tilemap.SetTile(position, tile);
                    }
                }
            }
        }
    }

    int GetTileIndexFromColor(Color color)
    {
        if (color == GetColorForTileIndex(TileTypes.Water)) return (int)TileTypes.Water;
        if (color == GetColorForTileIndex(TileTypes.Island)) return (int)TileTypes.Island;
        if (color == GetColorForTileIndex(TileTypes.Biome1)) return (int)TileTypes.Biome1;
        if (color == GetColorForTileIndex(TileTypes.Biome2)) return (int)TileTypes.Biome2;
        if (color == GetColorForTileIndex(TileTypes.Biome3)) return (int)TileTypes.Biome3;
        if (color == GetColorForTileIndex(TileTypes.Biome4)) return (int)TileTypes.Biome4;

        return -1; // Invalid color
    }

    // Save the pixel map as a PNG file
    void SavePixelMapAsPNG(Color[,] pixelMap)
    {
        Texture2D texture = new Texture2D(_width, _height);

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                texture.SetPixel(x, y, pixelMap[x, y]);
            }
        }

        texture.Apply();

        byte[] bytes = texture.EncodeToPNG();
        string path = Path.Combine(Application.dataPath, _outputPath);
        File.WriteAllBytes(path, bytes);

        Debug.Log($"Tilemap saved as PNG at: {_outputPath}");
    }

    public void ClearTilemap()
    {
        _tilemap.ClearAllTiles();
    }

    public void DestroyTilemap()
    {
        if (_tilemap != null)
        {
            DestroyImmediate(_tilemap.transform.parent.gameObject);
            _tilemap = null;
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(ComplexProceduralTilemapGenerator))]
public class ComplexProceduralTilemapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ComplexProceduralTilemapGenerator generator = (ComplexProceduralTilemapGenerator)target;
        if (GUILayout.Button("Generate Tilemap"))
        {
            generator.GenerateTilemap();
            AssetDatabase.Refresh();
        }

        GUILayout.Space(24);
        if (GUILayout.Button("Destroy Tilemap"))
        {
            generator.ClearTilemap();
            generator.DestroyTilemap();
        }
    }
}
#endif
