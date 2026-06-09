public interface IMaskFlowRepository
{
    Task EnsureSchemaAsync();
    Task<MaskFlowState?> LoadAsync();
    Task SaveAsync(MaskFlowState state);
}
