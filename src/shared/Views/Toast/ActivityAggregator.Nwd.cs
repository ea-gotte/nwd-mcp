#nullable disable
namespace Bimwright.Nwd.Shared.Views.Toast
{
    public sealed partial class ActivityAggregator
    {
        public bool RecordResult(string title, string body, bool success, string imagePath,
            bool frameUsable, long? durationMs = null) =>
            RecordResult(title, body, success, !string.IsNullOrEmpty(imagePath), frameUsable, imagePath, durationMs);
    }
}
