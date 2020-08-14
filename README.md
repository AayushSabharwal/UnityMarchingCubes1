# UnityMarchingCubes1
My first attempt at marching cubes using Unity. Implemented using compute shaders.

## How to run
Just download the project and open the scene. A decent specifications system (I'm using an i5 8300H, Nvidia 1050Ti and 8GB of RAM) is highly recommended. While running for a large region, GPU usage can go up to 100%. The "MarchingCubes" gameobject contains the requisite scripts. Only enable one of the two. The first script, "MarchingCubes" was an initial raw attempt at the algorithm, which calculates every chunk in the specified region sequentially. This has severe delay in rendering the mesh, since most of the chunks are empty and a waste of computation. The second script overcomes this by finding the first chunk with a mesh, and going to it's neighbours that are likely to contain other parts of the mesh. Note that the "Wait For Frame Chunk" option does not function on this script. Additionally, the script can use AsynchronousGPUReadback.Request to fetch data instead of stalling the main thread while waiting for the GPU. To do this, uncomment the overriden ProcessChunk method in the NewMarchingCubes script. This offers significant performance improvements. However, it is recommended to keep this disabled since it is a known bug that AsynchronousGPUReadback has a memory leak, resulting in the editor eventually crashing.

## References
- Polygonising a scalar field (Paul Bourke) (http://paulbourke.net/geometry/polygonise/)
- Getting started with compute shaders in Unity (Kylle Halladay) (http://kylehalladay.com/blog/tutorial/2014/06/27/Compute-Shaders-Are-Nifty.html)
- Noise shader library (keijiro) (https://github.com/keijiro/NoiseShader)
- Coding Adventure: Marching Cubes (Sebastian Lague) (https://www.youtube.com/watch?v=M3iI2l0ltbE)
