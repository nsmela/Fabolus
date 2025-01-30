# Notes on Implementing Smoothing

## Poisson Recon
- https://mark-borg.github.io/blog/2017/interop/
- a lot of data types there, each you want access to would require marshalling: https://learn.microsoft.com/en-us/dotnet/framework/interop/interop-marshalling
- start, with the tiniest programs on both ends and then grow them out
- get the c++ app to return an int, then a string, then a custom data type
- then load the library as a dll and work on returning data types
- you'll need to make a .dll from c++ to load in c#
- command lines are parsed here: https://github.com/mkazhdan/PoissonRecon/blob/4ffa838cd3ea4348ac02b2883692d4338312150d/Src/PoissonRecon.cpp#L584
- which lead to Execute: https://github.com/mkazhdan/PoissonRecon/blob/4ffa838cd3ea4348ac02b2883692d4338312150d/Src/PoissonRecon.cpp#L272
## Marching Cubes

## Mesh distance map
- use MeshQueries.NearestPointDistance
```
using g3;

// ... (assuming 'mesh', 'point', and 'spatial' as above) ...

int nearestTriangleID = spatial.FindNearestTriangle(point); 

DistPoint3Triangle3 distInfo = new DistPoint3Triangle3(point, mesh.GetTriangle(nearestTriangleID));
distInfo.GetSquared(); // Calculate the squared distance

double distance = Math.Sqrt(distInfo.DistanceSquared); 
Console.WriteLine($"Distance from point to mesh: {distance}");
```
- calculate distance for each point
- if within the compared mesh, make value negative
- set spread for values: -2.0mm to 0 to +2.0mm
- upper and lower thresholds setable
- colour for within (red?), normal (green?) and excessive (blue?)
- calculate float value from 0 to 1.0 (-2 to 2)
- set float for each point and export as texture coordinates
- create color gradient at start or when settings change
