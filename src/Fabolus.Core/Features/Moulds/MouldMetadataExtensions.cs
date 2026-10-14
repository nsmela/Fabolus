using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Moulds;

public static class MouldRecordExtensions {
    /// <summary>
    /// The mould this entry *is*, or null if it is not a generated mould. A recorded
    /// <see cref="MouldDefinition"/> is the signal that mould generation has been baked in - a
    /// mesh still being edited in the Mould tab carries <see cref="MeshRecord.PendingMould"/>
    /// instead.
    /// </summary>
    public static MouldDefinition? MouldDefinition(this MeshRecord record) =>
        record.Command<MouldDefinition>();

    public static MeshRecord WithMouldDefinition(this MeshRecord record, MouldDefinition definition) =>
        record.WithCommand(definition);

    public static MeshRecord WithPendingMould(this MeshRecord record, MouldDefinition definition) =>
        record with { PendingMould = definition };
}
