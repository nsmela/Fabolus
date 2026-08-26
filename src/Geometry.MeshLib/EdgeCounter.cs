namespace GeometryMeshLib;

/// <summary>
/// Tallies directed edges while triangulating. Edges with no opposing twin are the boundary,
/// which is what the extrusion paths walk to build side walls. Shared by
/// <see cref="GeometryGenerators"/> and <see cref="Polygons"/>, which both extrude.
/// </summary>
internal static class EdgeCounter
{
    public static void Add(Dictionary<(int, int), int> edgeCounts, int a, int b) =>
        edgeCounts[(a, b)] = edgeCounts.TryGetValue((a, b), out int count) ? count + 1 : 1;
}
