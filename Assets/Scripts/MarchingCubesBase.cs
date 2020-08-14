using System;
using System.Collections.Generic;
using MEC;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class MarchingCubesBase : MonoBehaviour
{
    [Header("Shader Data")]
    [SerializeField]
    protected ComputeShader marchCompute;
    [SerializeField]
    protected bool useNoise = true;
    [SerializeField]
    protected ComputeShader noiseDensityCompute;
    [SerializeField]
    protected ComputeShader functionDensityCompute;
    [SerializeField]
    protected NoiseType noiseType;
    [Header("Chunk Data")]
    [SerializeField]
    protected bool waitForFrameChunkUpdate;
    [SerializeField]
    protected Vector3Int viewDistance = Vector3Int.one;
    [SerializeField]
    protected int chunkSize = 16;
    [SerializeField]
    protected Transform chunksParent;
    [SerializeField]
    protected Material chunkMaterial;
    [SerializeField]
    protected GameObject instantiableChunk;
    [SerializeField]
    protected IndexFormat meshIndexFormat = IndexFormat.UInt16;
    [SerializeField]
    protected Transform target;
    [Header("Sampling Data")]
    [SerializeField]
    protected float sampleInterval = 1;
    [SerializeField]
    protected float surfaceLevel;
    [SerializeField]
    protected Octave[] octaves;

    protected int PointsPerAxis => Mathf.CeilToInt(chunkSize / sampleInterval) + 1;
    protected int VoxelsPerAxis => PointsPerAxis - 1;
    protected int NumPoints => PointsPerAxis * PointsPerAxis * PointsPerAxis;
    protected int NumVoxels => VoxelsPerAxis * VoxelsPerAxis * VoxelsPerAxis;
    protected int MaxTriangleCount => NumVoxels * 5;
    protected int NumChunks => (2 * viewDistance.x + 1) * (2 * viewDistance.y + 1) * (2 * viewDistance.z + 1);
    protected int NumTriangles => TriCountArr[0];
    protected ComputeShader CurrentDensityShader => useNoise ? noiseDensityCompute : functionDensityCompute;
    protected Vector3Int CurrentPlayerChunk => Vector3Int.FloorToInt(target.position / chunkSize);
    
    protected ComputeBuffer PointsBuffer;
    protected ComputeBuffer TrianglesBuffer;
    protected ComputeBuffer TriCountBuffer;

    protected Triangle[] Triangles;
    protected int[] MeshTriangles;
    protected readonly int[] TriCountArr = {0};
    protected Vector3[] Vertices;
    protected Dictionary<Vector3Int, Chunk> Chunks;
    protected Queue<Chunk> RecyclableChunks;
    protected Vector3Int[] ChunksToRemove;
    protected bool ProcessingChunk;

    protected void Initialize()
    {
        Chunks = new Dictionary<Vector3Int, Chunk>();
        RecyclableChunks = new Queue<Chunk>();
        ChunksToRemove = new Vector3Int[NumChunks];

        for (int i = 0; i < NumChunks; i++)
        {
            RecyclableChunks.Enqueue(MakeNewChunk());
        }

        SetGenericComputeParameters();
    }
    private Vector4[] OctavesToVector4Array()
    {
        Vector4[] arr = new Vector4[octaves.Length];
        for (int i = 0; i < octaves.Length; i++)
        {
            arr[i] = octaves[i];
        }

        return arr;
    }

    protected Chunk MakeNewChunk()
    {
        GameObject g = Instantiate(instantiableChunk, Vector3.zero, Quaternion.identity, chunksParent);
        MeshFilter filter = g.GetComponent<MeshFilter>();
        g.GetComponent<MeshRenderer>().sharedMaterial = chunkMaterial;
        Mesh m = new Mesh {indexFormat = meshIndexFormat};
        filter.sharedMesh = m;
        return new Chunk(filter, m);
    }

    protected void SetGenericComputeParameters()
    {
        //densityCompute.SetBuffer(0, "pts", _pointsBuffer);
        CurrentDensityShader.SetInt("ppa", PointsPerAxis);
        //CurrentDensityShader.SetVector("offset", chunkStart);
        CurrentDensityShader.SetFloat("sampleInterval", sampleInterval);
        CurrentDensityShader.SetVectorArray("octaves", OctavesToVector4Array());
        CurrentDensityShader.SetInt("nOctaves", octaves.Length);
        // marchCompute.SetBuffer(0, "pts", _pointsBuffer);
        // marchCompute.SetBuffer(0, "Result", _trianglesBuffer);
        marchCompute.SetFloat("surfaceLevel", surfaceLevel);
        marchCompute.SetInt("ppa", PointsPerAxis);
    }

    private void SetComputeBuffers()
    {
        CurrentDensityShader.SetBuffer(0, "pts", PointsBuffer);
        marchCompute.SetBuffer(0, "pts", PointsBuffer);
        marchCompute.SetBuffer(0, "Result", TrianglesBuffer);
    }

    protected void CreateBuffers()
    {
        if (!Application.isPlaying || PointsBuffer == null || !PointsBuffer.IsValid() || NumPoints != PointsBuffer.count)
        {
            if (Application.isPlaying)
            {
                ReleaseBuffers();
            }
            
            TrianglesBuffer = new ComputeBuffer(MaxTriangleCount, sizeof(float) * 3 * 3, ComputeBufferType.Append);
            PointsBuffer = new ComputeBuffer(NumPoints, sizeof(float) * 4);
            TriCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
            SetComputeBuffers();
        }
    }

    
    private void OnDisable()
    {
        ReleaseBuffers();
    }
    protected void GetPoints(Vector3 chunkIndex)
    {
        chunkIndex *= chunkSize;
        int threadsPerAxis = Mathf.CeilToInt(PointsPerAxis / 8f);
        if (useNoise)
        {
            noiseDensityCompute.shaderKeywords = null;
            switch (noiseType)
            {
                case NoiseType.Perlin:
                    noiseDensityCompute.EnableKeyword("PNOISE");
                    break;
                case NoiseType.Simplex:
                    noiseDensityCompute.EnableKeyword("SNOISE");
                    break;
                default:
                    Debug.LogError("INVALID OPTION");
                    break;
            }

            noiseDensityCompute.SetVector("offset", chunkIndex);
            noiseDensityCompute.Dispatch(0, threadsPerAxis, threadsPerAxis, threadsPerAxis);
        }
        else
        {
            functionDensityCompute.SetVector("offset", chunkIndex);
            functionDensityCompute.Dispatch(0, threadsPerAxis, threadsPerAxis, threadsPerAxis);
        }
    }

    protected void March()
    {
        TrianglesBuffer.SetCounterValue(0);
        int threadsPerAxis = Mathf.CeilToInt(VoxelsPerAxis / 8f);
        marchCompute.Dispatch(0, threadsPerAxis, threadsPerAxis, threadsPerAxis);
    }

    protected IEnumerator<float> WaitAssignChunk(Chunk oldChunk, Vector3Int newChunkIndex)
    {
        yield return Timing.WaitForOneFrame;
        AssignChunk(oldChunk, newChunkIndex);
    }

    protected void AssignChunk(Chunk oldChunk, Vector3Int newChunkIndex)
    {
        ComputeBuffer.CopyCount(TrianglesBuffer, TriCountBuffer, 0);
        TriCountBuffer.GetData(TriCountArr);
        if (Triangles == null || Triangles.Length != NumTriangles)
        {
            Triangles = new Triangle[NumTriangles];
            Vertices = new Vector3[NumTriangles * 3];
            MeshTriangles = new int[NumTriangles * 3];
        }
        TrianglesBuffer.GetData(Triangles);
        oldChunk.Mesh.Clear();
        if (NumTriangles > 1)
        {
            

            for (int i = 0; i < NumTriangles; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Vertices[3 * i + j] = Triangles[i][j];
                    MeshTriangles[3 * i + j] = 3 * i + j;
                }
            }

            oldChunk.Mesh.vertices = Vertices;
            oldChunk.Mesh.triangles = MeshTriangles;
            oldChunk.Mesh.RecalculateNormals();
            oldChunk.Mesh.Optimize();
        }

        oldChunk.Move(newChunkIndex * chunkSize);
        oldChunk.Filter.sharedMesh = oldChunk.Mesh;
        Chunks[newChunkIndex] = oldChunk;
        OnAfterAssignChunk(oldChunk, newChunkIndex);
        ProcessingChunk = false;
    }

    protected virtual void OnAfterAssignChunk(Chunk oldChunk, Vector3Int newChunkIndex)
    {
        
    }

    protected virtual void ProcessChunk(Vector3Int chunkIndex, Chunk chunk)
    {
        if (ProcessingChunk)
        {
            return;
        }

        ProcessingChunk = true;
        
        GetPoints(chunkIndex);
        March();
        //Timing.RunCoroutine(AssignChunk(c, inspectedChunk));
        if (waitForFrameChunkUpdate)
        {
            Timing.RunCoroutine(WaitAssignChunk(chunk, chunkIndex));
        }
        else
        {
            AssignChunk(chunk, chunkIndex);
        }
    }

    protected void ReleaseBuffers()
    {
        if (TrianglesBuffer != null)
        {
            TrianglesBuffer.Release();
            PointsBuffer.Release();
            TriCountBuffer.Release();
        }
    }

    private void OnValidate()
    {
        if (viewDistance.x <= 0)
        {
            viewDistance.x = 1;
        }

        if (viewDistance.y <= 0)
        {
            viewDistance.y = 1;
        }

        if (viewDistance.z <= 0)
        {
            viewDistance.z = 1;
        }
    }
}

public class Chunk
{
    public MeshFilter Filter;
    public Mesh Mesh;

    public Chunk(MeshFilter filter, Mesh mesh)
    {
        Filter = filter;
        Mesh = mesh;
    }

    public void Move(Vector3 to)
    {
        Filter.gameObject.transform.position = to;
    }
}

[Serializable]
public struct Octave
{
    public float amplitude, frequency, yOffset;

    public static implicit operator Vector4(Octave o)
    {
        return new Vector4(o.amplitude, o.frequency, o.yOffset);
    }
}

public struct Triangle
{
    public float3 a, b, c;
    public float3 this[int i] =>
        i == 0 ? a :
        i == 1 ? b : c;
}

public enum NoiseType
{
    Simplex,
    Perlin
}