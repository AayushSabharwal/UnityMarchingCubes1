using System.Collections.Generic;
using UnityEngine;

public class MarchingCubes : MarchingCubesBase
{

    private void Start()
    {
        Application.targetFrameRate = 1000;

        Initialize();
        //GenerateChunks(Vector3Int.zero);
    }

    private void Update()
    {
        //Debug.Log(currentPlayerChunk);
        GenerateChunks();
    }

    //private IEnumerator<float> AssignChunk(Chunk oldChunk, Vector3Int newChunkIndex)

    private void GenerateChunks()
    {
        int removedChunks = 0;
        foreach (KeyValuePair<Vector3Int, Chunk> kvPair in Chunks)
        {
            Vector3Int delta = kvPair.Key - CurrentPlayerChunk;
            if (Mathf.Abs(delta.x) > viewDistance.x || Mathf.Abs(delta.y) > viewDistance.y ||
                Mathf.Abs(delta.z) > viewDistance.z)
            {
                RecyclableChunks.Enqueue(kvPair.Value);
                ChunksToRemove[removedChunks] = kvPair.Key;
                removedChunks++;
            }
        }

        for (int i = 0; i < removedChunks; i++)
        {
            Chunks.Remove(ChunksToRemove[i]);
        }

        if (ProcessingChunk)
        {
            return;
        }

        CreateBuffers();

        for (int i = CurrentPlayerChunk.x - viewDistance.x;
             i <= CurrentPlayerChunk.x + viewDistance.x;
             i++)
        {
            for (int j = CurrentPlayerChunk.y - viewDistance.y;
                 j <= CurrentPlayerChunk.y + viewDistance.y;
                 j++)
            {
                for (int k = CurrentPlayerChunk.z - viewDistance.z;
                     k <= CurrentPlayerChunk.z + viewDistance.z;
                     k++)
                {
                    Vector3Int inspectedChunk = new Vector3Int(i, j, k);

                    if (!Chunks.ContainsKey(inspectedChunk))
                    {
                        Chunk c;
                        if (RecyclableChunks.Count > 0)
                        {
                            c = RecyclableChunks.Dequeue();
                        }
                        else
                        {
                            return;
                        }

                        ProcessChunk(inspectedChunk, c);
                        return;
                    }
                }
            }
        }
    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(CurrentPlayerChunk * chunkSize + Vector3.one / 2f * chunkSize,
                            (viewDistance * 2 + Vector3Int.one) * chunkSize);
    }
}