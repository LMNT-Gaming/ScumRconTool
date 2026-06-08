using System.IO;
namespace ScumRconTool.ViewModels;

public sealed class ScriptFileViewModel : ObservableObject
{
    public ScriptFileViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }
    public string Name { get; }

    private bool _hasErrors;
    public bool HasErrors
    {
        get => _hasErrors;
        set => SetProperty(ref _hasErrors, value);
    }
}
