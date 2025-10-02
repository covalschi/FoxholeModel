namespace FModel.Services;

public static class ApplicationService
{
    public static ApplicationViewState ApplicationView { get; } = new();

    public sealed class ApplicationViewState
    {
        public CUE4ParseView CUE4Parse { get; } = new();
        public StatusView Status { get; } = new();
    }

    public sealed class CUE4ParseView
    {
        public ProviderInfo Provider { get; } = new();
        public bool ModelIsOverwritingMaterial { get; set; }
        public bool ModelIsWaitingAnimation { get; set; }
    }

    public sealed class ProviderInfo
    {
        public string ProjectName { get; set; } = "FOXHOLE";
    }

    public sealed class StatusView
    {
        public void UpdateStatusLabel(string message)
        {
            // no-op for headless mode
        }
    }
}
