# Notes on Implementing Smoothing

## Poisson Recon

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
