# Parting line Notes

## references
- https://sakshik.medium.com/understanding-the-frenet-serret-frame-3b9c730e8b1c
- https://www.youtube.com/watch?v=ZrixRt-JTo8

## Converting to 2D to offset then using original path's y offsets
- works well for simple round shapes
- complicated shapes get "out of tune" and go crazy
- input path, inner path, and outer path need to have the same number of vertices
- our EvenEdgeLoop class ended up adding extra segments at the end, creating defects
- decided that evening the loop would distort the loop significantly and might be the wrong approach