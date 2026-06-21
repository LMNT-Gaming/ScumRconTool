namespace ScumRconTool.Models;

public sealed class UpdateInfo
{
    public string? version { get; set; }
    public string? downloadUrl { get; set; }
    public string? patchNotesUrl { get; set; }
    public bool mandatory { get; set; }
    public string? sha256 { get; set; }
}
