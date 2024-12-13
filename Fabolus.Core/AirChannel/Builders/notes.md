# Notes on Channel generators
## Angled Channel Generator
Ref: https://math.stackexchange.com/questions/886982/formula-to-display-a-3d-90-degree-pipe-bend
- a circle equation shoudl suffice
- x & y on curve should be the x on the circle
- z on curve should be the y
- starting angle should determine how far along the circle to start
- ends at (1,0) on circle
- starts as far back as (0, -1) on circle
- create a formula/function
- direction/normal is multiplied by the x coordinate to give direction and distance

## Design
Core is used to define curves, the interface generates the mesh. Once I'm more familiar with DMesh and the mesh generation, we can move the mesh generation to the Core library.