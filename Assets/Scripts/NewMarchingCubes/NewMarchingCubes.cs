using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class NewMarchingCubes : MarchingCubesBase
{
    [SerializeField]
    [Range(0f, 0.1f)]
    private float borderApproximationThreshold = 0.001f;
    private Queue<Vector3Int> _chunksToCompute;
    private HashSet<Vector3Int> _emptyChunks;
    private HashSet<Vector3Int> _processedChunks;
    private bool _foundMeshChunk;
    private Vector3Int _lastPlayerChunk;
    private (Vector3Int startIndex, Vector3Int size) _lastGenParams;
    private void Awake()
    {
        Application.targetFrameRate = 1000;
        _chunksToCompute = new Queue<Vector3Int>();
        _emptyChunks = new HashSet<Vector3Int>();
        _processedChunks = new HashSet<Vector3Int>();
        _lastPlayerChunk = CurrentPlayerChunk;
        Initialize();
        GenerateRegionChunks(CurrentPlayerChunk-viewDistance, viewDistance*2);
    }

    // protected override void ProcessChunk(Vector3Int chunkIndex, Chunk chunk)
    // {
    //     if (ProcessingChunk)
    //     {
    //         return;
    //     }
    //     ProcessingChunk = true;
    //     GetPoints(chunkIndex);
    //     March();
    //     AsyncGPUReadback.Request(TrianglesBuffer, delegate
    //                                               {
    //                                                   AssignChunk(chunk, chunkIndex);
    //                                                   ReleaseBuffers();
    //                                               });
    // }

    private void GenerateRegionChunks(Vector3Int startChunkIndex, Vector3Int chunkRegionSize)
    {
        if (ProcessingChunk)
        {
            return;
        }

        _lastGenParams = (startChunkIndex, chunkRegionSize);
        CreateBuffers();
        _foundMeshChunk = false;
        for (int i = startChunkIndex.x; i <= startChunkIndex.x + chunkRegionSize.x && !_foundMeshChunk; i++)
        {
            for (int j = startChunkIndex.y; j <= startChunkIndex.y + chunkRegionSize.y && !_foundMeshChunk; j++)
            {
                for (int k = startChunkIndex.z; k <= startChunkIndex.z + chunkRegionSize.z && !_foundMeshChunk; k++)
                {
                    Vector3Int currentChunkIndex = new Vector3Int(i, j, k);
                    if (_processedChunks.Contains(currentChunkIndex) || _emptyChunks.Contains(currentChunkIndex))
                    {
                        continue;
                    }

                    if (ProcessingChunk)
                    {
                        continue;
                    }
                    Chunk c;
                    if (RecyclableChunks.Count > 0)
                    {
                        c = RecyclableChunks.Dequeue();
                        // Debug.Log("DQ1");
                    }
                    else
                    {
                        return;
                    }

                    ProcessChunk(currentChunkIndex, c);
                    _processedChunks.Add(currentChunkIndex);
                }
            }
        }
    }

    protected override void OnAfterAssignChunk(Chunk oldChunk, Vector3Int newChunkIndex)
    {
        if (NumTriangles > 1)
        {
            _foundMeshChunk = true;
            Vector3 chunkBordersMin = newChunkIndex * chunkSize;
            Vector3 chunkBordersMax = (newChunkIndex + Vector3Int.one) * chunkSize;
            foreach (Vector3 vertex in oldChunk.Mesh.vertices)
            {
                if (Approximately(vertex.x, chunkBordersMax.x, borderApproximationThreshold) &&
                    chunkBordersMax.x <= CurrentPlayerChunk.x + viewDistance.x &&
                    !Chunks.ContainsKey(newChunkIndex + Vector3Int.right))
                {
                    _chunksToCompute.Enqueue(newChunkIndex + Vector3Int.right);
                }

                if (Approximately(vertex.y, chunkBordersMax.y, borderApproximationThreshold) &&
                    chunkBordersMax.y <= CurrentPlayerChunk.y + viewDistance.y &&
                    !Chunks.ContainsKey(newChunkIndex + Vector3Int.up))
                {
                    _chunksToCompute.Enqueue(newChunkIndex + Vector3Int.up);
                }

                if (Approximately(vertex.z, chunkBordersMax.z, borderApproximationThreshold) &&
                    chunkBordersMax.z <= CurrentPlayerChunk.z + viewDistance.z &&
                    !Chunks.ContainsKey(newChunkIndex + new Vector3Int(0, 0, 1)))
                {
                    _chunksToCompute.Enqueue(newChunkIndex + new Vector3Int(0, 0, 1));
                }

                if (Approximately(vertex.x, chunkBordersMin.x, borderApproximationThreshold) &&
                    chunkBordersMax.x >= CurrentPlayerChunk.x - viewDistance.x &&
                    !Chunks.ContainsKey(newChunkIndex - Vector3Int.right))
                {
                    _chunksToCompute.Enqueue(newChunkIndex - Vector3Int.right);
                }

                if (Approximately(vertex.y, chunkBordersMin.y, borderApproximationThreshold) &&
                    chunkBordersMax.y >= CurrentPlayerChunk.y - viewDistance.y &&
                    !Chunks.ContainsKey(newChunkIndex - Vector3Int.up))
                {
                    _chunksToCompute.Enqueue(newChunkIndex - Vector3Int.up);
                }

                if (Approximately(vertex.z, chunkBordersMin.z, borderApproximationThreshold) &&
                    chunkBordersMax.z >= CurrentPlayerChunk.z - viewDistance.z &&
                    !Chunks.ContainsKey(newChunkIndex - new Vector3Int(0, 0, 1)))
                {
                    _chunksToCompute.Enqueue(newChunkIndex - new Vector3Int(0, 0, 1));
                }
            }
        }
        else
        {
            Chunks.Remove(newChunkIndex);
            RecyclableChunks.Enqueue(oldChunk);
            _emptyChunks.Add(newChunkIndex);
        }
    }

    private void RecycleChunks()
    {
        int removedChunks = 0;
        foreach (KeyValuePair<Vector3Int, Chunk> kvPair in Chunks)
        {
            Vector3Int delta = kvPair.Key - CurrentPlayerChunk;
            if (Mathf.Abs(delta.x) > viewDistance.x || Mathf.Abs(delta.y) > viewDistance.y ||
                Mathf.Abs(delta.z) > viewDistance.z)
            {
                RecyclableChunks.Enqueue(kvPair.Value);
                _processedChunks.Remove(kvPair.Key);
                ChunksToRemove[removedChunks] = kvPair.Key;
                removedChunks++;
            }
        }

        for (int i = 0; i < removedChunks; i++)
        {
            Chunks.Remove(ChunksToRemove[i]);
        }
    }

    private void Update()
    {
        while (!ProcessingChunk && _chunksToCompute.Count > 0 && RecyclableChunks.Count > 0)
        {
            Vector3Int currentChunkIndex = _chunksToCompute.Dequeue();
            if (Chunks.ContainsKey(currentChunkIndex) || _emptyChunks.Contains(currentChunkIndex))
            {
                continue;
            }
            CreateBuffers();
            Chunk c = RecyclableChunks.Dequeue();
            // Debug.Log("DQ2");
            ProcessChunk(currentChunkIndex, c);
        }
        
        if (_chunksToCompute.Count == 0 && Chunks.Count < NumChunks && !ProcessingChunk)
        {
            GenerateRegionChunks(CurrentPlayerChunk - viewDistance, viewDistance * 2);
        }
        // if (_lastPlayerChunk != CurrentPlayerChunk)
        // {
        //     Vector3Int delta = CurrentPlayerChunk - _lastPlayerChunk;
        //     int multiplier = delta.x < 0 || delta.y < 0 || delta.z < 0 ? -1 : 1;
        //     Vector3Int chunkRegionSize = viewDistance * (Vector3Int.one - delta * multiplier) + delta * multiplier;
        //     GenerateRegionChunks(_lastPlayerChunk+delta*viewDistance, chunkRegionSize);
        //     _lastPlayerChunk = CurrentPlayerChunk;
        // }
        //
        // if (!_foundMeshChunk)
        // {
        //     GenerateRegionChunks(_lastGenParams.startIndex, _lastGenParams.size);
        // }
        RecycleChunks();
    }

    private bool Approximately(float a, float b, float threshold)
    {
        return (a < b ? b - a : a - b) <= threshold;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(CurrentPlayerChunk, 2 * viewDistance * chunkSize);
    }
}